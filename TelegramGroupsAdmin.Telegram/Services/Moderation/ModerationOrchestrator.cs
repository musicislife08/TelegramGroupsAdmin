using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Manager/Worker orchestration layer for moderation actions.
/// The "boss" that knows all workers, decides who to call, and owns business rules.
///
/// Key responsibilities:
/// - System account protection (blocks moderation on Telegram system accounts: 777000, 1087968824, etc.)
/// - Business rules: "bans revoke trust", "N warnings = auto-ban"
/// - Workflow composition: warn → check threshold → ban
/// - Direct handler calls (no event broadcasting)
///
/// Workers are domain experts that don't know about each other.
/// Only the orchestrator composes workflows across workers.
/// </summary>
public class ModerationOrchestrator : IModerationOrchestrator
{
    // Domain handlers (workers)
    private readonly IBanHandler _banHandler;
    private readonly ITrustHandler _trustHandler;
    private readonly IWarnHandler _warnHandler;
    private readonly IMessageHandler _messageHandler;
    private readonly IRestrictHandler _restrictHandler;

    // Support handlers
    private readonly IAuditHandler _auditHandler;
    private readonly INotificationHandler _notificationHandler;
    private readonly ITrainingHandler _trainingHandler;

    // Repositories for logging
    private readonly ITelegramUserRepository _userRepository;

    // Configuration
    private readonly IConfigService _configService;
    private readonly ILogger<ModerationOrchestrator> _logger;

    public ModerationOrchestrator(
        IBanHandler banHandler,
        ITrustHandler trustHandler,
        IWarnHandler warnHandler,
        IMessageHandler messageHandler,
        IRestrictHandler restrictHandler,
        IAuditHandler auditHandler,
        INotificationHandler notificationHandler,
        ITrainingHandler trainingHandler,
        ITelegramUserRepository userRepository,
        IConfigService configService,
        ILogger<ModerationOrchestrator> logger)
    {
        _banHandler = banHandler;
        _trustHandler = trustHandler;
        _warnHandler = warnHandler;
        _messageHandler = messageHandler;
        _restrictHandler = restrictHandler;
        _auditHandler = auditHandler;
        _notificationHandler = notificationHandler;
        _trainingHandler = trainingHandler;
        _userRepository = userRepository;
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        Actor executor,
        string reason,
        global::Telegram.Bot.Types.Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Step 1: Ensure message exists in database (backfill if needed for training data)
        await _messageHandler.EnsureExistsAsync(messageId, chatId, telegramMessage, cancellationToken);

        // Step 2: Delete message (best effort - may already be deleted)
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, executor, cancellationToken);
        if (deleteResult.MessageDeleted)
        {
            await _auditHandler.LogDeleteAsync(messageId, chatId, userId, executor, cancellationToken);
        }

        // Step 3: Ban user (self-call - reuses ban logic including trust revocation)
        var banResult = await BanUserAsync(userId, messageId, executor, reason, cancellationToken);

        if (!banResult.Success)
        {
            return new ModerationResult
            {
                Success = false,
                ErrorMessage = banResult.ErrorMessage,
                MessageDeleted = deleteResult.MessageDeleted
            };
        }

        // Step 4: Create training data (non-critical - failure doesn't affect ban success)
        try
        {
            await _trainingHandler.CreateSpamSampleAsync(messageId, executor, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create training data for message {MessageId}, continuing with successful ban",
                messageId);
        }

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            MessageDeleted = deleteResult.MessageDeleted,
            TrustRemoved = banResult.TrustRemoved
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> BanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Primary action: Ban globally
        var banResult = await _banHandler.BanAsync(userId, executor, reason, messageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban failed");

        // Audit successful ban (separate from BanHandler's state tracking record)
        await _auditHandler.LogBanAsync(userId, executor, reason, cancellationToken);

        // Business rule: Bans always revoke trust
        var untrustReason = string.IsNullOrWhiteSpace(reason)
            ? "Trust revoked due to ban"
            : $"Trust revoked due to ban: {reason}";
        var untrustResult = await _trustHandler.UntrustAsync(
            userId, executor, untrustReason, cancellationToken);

        if (untrustResult.Success)
        {
            await _auditHandler.LogUntrustAsync(userId, executor, untrustReason, cancellationToken);
        }

        // Notify admins
        await _notificationHandler.NotifyAdminsBanAsync(userId, executor, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            TrustRemoved = untrustResult.Success
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        // Primary action: Issue warning (writes to warnings table)
        var warnResult = await _warnHandler.WarnAsync(userId, executor, reason, chatId, messageId, cancellationToken);

        if (!warnResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = warnResult.ErrorMessage };

        // Audit successful warning
        await _auditHandler.LogWarnAsync(userId, executor, reason, cancellationToken);

        // Notify user about warning
        await _notificationHandler.NotifyUserWarningAsync(userId, warnResult.WarningCount, reason, cancellationToken);

        var result = new ModerationResult
        {
            Success = true,
            WarningCount = warnResult.WarningCount
        };

        // Business rule: Check warning threshold for auto-ban
        var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(
            ConfigType.Moderation, chatId) ?? WarningSystemConfig.Default;

        if (warningConfig.AutoBanEnabled &&
            warningConfig.AutoBanThreshold > 0 &&
            warnResult.WarningCount >= warningConfig.AutoBanThreshold)
        {
            _logger.LogWarning(
                "Auto-ban triggered: {User} has {WarnCount} warnings (threshold: {Threshold})",
                user.ToLogDebug(userId), warnResult.WarningCount, warningConfig.AutoBanThreshold);

            // Use configured auto-ban reason with {count} placeholder support
            var autoBanReason = !string.IsNullOrWhiteSpace(warningConfig.AutoBanReason)
                ? warningConfig.AutoBanReason.Replace("{count}", warnResult.WarningCount.ToString())
                : $"Exceeded warning threshold ({warnResult.WarningCount}/{warningConfig.AutoBanThreshold} warnings)";
            var banResult = await BanUserAsync(userId, messageId, Actor.AutoBan, autoBanReason, cancellationToken);

            if (banResult.Success)
            {
                result.AutoBanTriggered = true;
                result.ChatsAffected = banResult.ChatsAffected;
                result.TrustRemoved = banResult.TrustRemoved;
            }
            else
            {
                _logger.LogError(
                    "Auto-ban failed for {User}: {Error}",
                    user.ToLogDebug(userId), banResult.ErrorMessage);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var trustResult = await _trustHandler.TrustAsync(userId, executor, reason, cancellationToken);

        if (!trustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = trustResult.ErrorMessage };

        // Audit successful trust
        await _auditHandler.LogTrustAsync(userId, executor, reason, cancellationToken);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UntrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var untrustResult = await _trustHandler.UntrustAsync(userId, executor, reason, cancellationToken);

        if (!untrustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = untrustResult.ErrorMessage };

        // Audit successful untrust
        await _auditHandler.LogUntrustAsync(userId, executor, reason, cancellationToken);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UnbanUserAsync(
        long userId,
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var unbanResult = await _banHandler.UnbanAsync(userId, executor, reason, cancellationToken);

        if (!unbanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = unbanResult.ErrorMessage };

        // Audit successful unban
        await _auditHandler.LogUnbanAsync(userId, executor, reason, cancellationToken);

        var result = new ModerationResult
        {
            Success = true,
            ChatsAffected = unbanResult.ChatsAffected
        };

        // Handle trust restoration as follow-up
        if (restoreTrust)
        {
            var trustResult = await TrustUserAsync(
                userId, executor,
                "Trust restored after unban (false positive correction)", cancellationToken);
            result.TrustRestored = trustResult.Success;
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        long userId,
        Actor deletedBy,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, deletedBy, cancellationToken);

        // Audit the deletion attempt (even if message was already deleted)
        await _auditHandler.LogDeleteAsync(messageId, chatId, userId, deletedBy, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var tempBanResult = await _banHandler.TempBanAsync(userId, executor, duration, reason, messageId, cancellationToken);

        if (!tempBanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = tempBanResult.ErrorMessage };

        // Audit successful temp ban (separate from BanHandler's state tracking record)
        await _auditHandler.LogTempBanAsync(userId, executor, duration, reason, cancellationToken);

        // Notify user about temp ban with rejoin info
        await _notificationHandler.NotifyUserTempBanAsync(userId, duration, tempBanResult.ExpiresAt, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = tempBanResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> RestrictUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var restrictResult = await _restrictHandler.RestrictAsync(
            userId, chatId ?? 0, executor, duration, reason, cancellationToken);

        if (!restrictResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = restrictResult.ErrorMessage };

        // Audit successful restriction
        await _auditHandler.LogRestrictAsync(userId, chatId ?? 0, executor, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = restrictResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> SyncBanToChatAsync(
        User user,
        Chat chat,
        string reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(user.Id, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var banResult = await _banHandler.BanAsync(
            user, chat, Actor.AutoDetection, reason, triggeredByMessageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban sync failed");

        // Audit successful ban sync
        await _auditHandler.LogBanAsync(user.Id, Actor.AutoDetection, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected
        };
    }

    /// <summary>
    /// Checks if user is a Telegram system account (777000, 1087968824, etc.) and returns error if moderation is attempted.
    /// </summary>
    private async Task<ModerationResult?> CheckServiceAccountProtectionAsync(long userId, CancellationToken cancellationToken)
    {
        if (TelegramConstants.IsSystemUser(userId))
        {
            var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
            _logger.LogWarning(
                "Moderation action blocked for Telegram system account ({User})",
                user.ToLogDebug(userId));

            return ModerationResult.SystemAccountBlocked();
        }

        return null;
    }
}

/// <summary>
/// Result of a moderation action.
/// </summary>
public class ModerationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool MessageDeleted { get; set; }
    public bool TrustRemoved { get; set; }
    public bool TrustRestored { get; set; }
    public int ChatsAffected { get; set; }
    public int WarningCount { get; set; }
    public bool AutoBanTriggered { get; set; }

    public static ModerationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static ModerationResult SystemAccountBlocked() =>
        new() { Success = false, ErrorMessage = "Cannot perform moderation actions on Telegram system accounts (channel posts, anonymous admins, etc.)" };
}
