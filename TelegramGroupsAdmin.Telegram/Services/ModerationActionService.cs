using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized moderation action service used by both bot commands and UI.
/// Ensures consistent behavior for spam marking, banning, warning, trusting, and unbanning.
/// </summary>
public class ModerationActionService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly MessageHistoryRepository _messageHistoryRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly ITelegramUserMappingRepository _telegramUserMappingRepository;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly IConfigService _configService;
    private readonly ILogger<ModerationActionService> _logger;

    public ModerationActionService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        MessageHistoryRepository messageHistoryRepository,
        IManagedChatsRepository managedChatsRepository,
        ITelegramUserMappingRepository telegramUserMappingRepository,
        TelegramBotClientFactory botClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        IConfigService configService,
        ILogger<ModerationActionService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _messageHistoryRepository = messageHistoryRepository;
        _managedChatsRepository = managedChatsRepository;
        _telegramUserMappingRepository = telegramUserMappingRepository;
        _botClientFactory = botClientFactory;
        _telegramOptions = telegramOptions.Value;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Mark message as spam, delete it, ban user globally, remove trust, and create detection result.
    /// Used by: /spam command, Messages.razor "Mark as Spam", Reports "Spam & Ban" action
    /// </summary>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        ITelegramBotClient botClient,
        long messageId,
        long userId,
        long chatId,
        string? executorId, // Web app user ID or null
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // 1. Delete the message from Telegram
            try
            {
                await botClient.DeleteMessage(chatId, (int)messageId, cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 2. Mark message as deleted in database
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(messageId, "spam_action");

            // 3. Create detection result (manual spam classification)
            var detectionResult = new DetectionResultRecord
            {
                MessageId = messageId,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = "manual",
                DetectionMethod = "Manual",
                // IsSpam computed from net_confidence (100 = strong spam)
                Confidence = 100, // 100% spam
                Reason = reason,
                AddedBy = ConvertToActor(executorId),
                UserId = userId,
                UsedForTraining = true, // Manual decisions are always training-worthy
                NetConfidence = 100, // Strong spam signal (manual admin decision)
                CheckResultsJson = null,
                EditVersion = 0
            };
            await _detectionResultsRepository.InsertAsync(detectionResult, cancellationToken);

            // 4. Remove any existing trust actions (compromised account protection)
            await _userActionsRepository.ExpireTrustsForUserAsync(userId, chatId: null, cancellationToken);
            result.TrustRemoved = true;

            // 5. Ban user globally across all managed chats
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // 6. Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

            _logger.LogInformation(
                "Spam action completed: User {UserId} banned from {ChatsAffected} chats, trust removed, message {MessageId} deleted",
                userId, result.ChatsAffected, messageId);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute spam and ban action for user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Mark message as spam without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        string? executorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
        return await MarkAsSpamAndBanAsync(botClient, messageId, userId, chatId, executorId, reason, cancellationToken);
    }

    /// <summary>
    /// Ban user globally across all managed chats.
    /// Used by: /ban command, Reports "Ban" action
    /// </summary>
    public async Task<ModerationResult> BanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Remove trust actions
            await _userActionsRepository.ExpireTrustsForUserAsync(userId, chatId: null, cancellationToken);
            result.TrustRemoved = true;

            // Ban globally
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

            _logger.LogInformation("Ban action completed: User {UserId} banned from {ChatsAffected} chats", userId, result.ChatsAffected);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Warn user globally with automatic ban after threshold.
    /// Used by: /warn command, Reports "Warn" action
    /// </summary>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Insert the warning
            var warnAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Warn,
                MessageId: messageId,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(warnAction, cancellationToken);

            // 2. Get current warning count
            var warnCount = await _userActionsRepository.GetWarnCountAsync(userId, chatId, cancellationToken);

            _logger.LogInformation("Warn action completed: User {UserId} warned (total warnings: {WarnCount})", userId, warnCount);

            var result = new ModerationResult
            {
                Success = true,
                WarningCount = warnCount
            };

            // 3. Check if auto-ban threshold is reached
            var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(ConfigType.Moderation, chatId)
                               ?? WarningSystemConfig.Default;

            if (warningConfig.AutoBanEnabled &&
                warningConfig.AutoBanThreshold > 0 &&
                warnCount >= warningConfig.AutoBanThreshold)
            {
                // 4. Auto-ban user
                var autoBanReason = warningConfig.AutoBanReason.Replace("{count}", warnCount.ToString());
                var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

                var banResult = await BanUserAsync(
                    botClient: botClient,
                    userId: userId,
                    messageId: messageId,
                    executorId: "system:auto-ban",
                    reason: autoBanReason,
                    cancellationToken: cancellationToken);

                if (banResult.Success)
                {
                    result.AutoBanTriggered = true;
                    result.ChatsAffected = banResult.ChatsAffected;
                    _logger.LogWarning(
                        "Auto-ban triggered: User {UserId} banned after {WarnCount} warnings (threshold: {Threshold})",
                        userId, warnCount, warningConfig.AutoBanThreshold);
                }
                else
                {
                    _logger.LogError("Auto-ban failed for user {UserId}: {Error}", userId, banResult.ErrorMessage);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// Used by: /trust command, UI trust button
    /// </summary>
    public async Task<ModerationResult> TrustUserAsync(
        long userId,
        string? executorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(trustAction, cancellationToken);

            _logger.LogInformation("Trust action completed: User {UserId} trusted globally", userId);

            return new ModerationResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trust user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Unban user globally and optionally restore trust.
    /// Used by: /unban command, Messages.razor "Mark as Ham", Reports "Dismiss" action
    /// </summary>
    public async Task<ModerationResult> UnbanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        string? executorId,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Expire all active bans
            await _userActionsRepository.ExpireBansForUserAsync(userId, chatId: null, cancellationToken);

            // Unban from all chats
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.UnbanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        onlyIfBanned: true,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record unban action
            var unbanAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Unban,
                MessageId: null,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(unbanAction, cancellationToken);

            // Optionally restore trust (for false positive corrections)
            if (restoreTrust)
            {
                await TrustUserAsync(userId, executorId, "Trust restored after unban (false positive correction)", cancellationToken);
                result.TrustRestored = true;
            }

            _logger.LogInformation(
                "Unban action completed: User {UserId} unbanned from {ChatsAffected} chats, trust restored: {TrustRestored}",
                userId, result.ChatsAffected, restoreTrust);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Unban user without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> UnbanUserAsync(
        long userId,
        string? executorId,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
        return await UnbanUserAsync(botClient, userId, executorId, reason, restoreTrust, cancellationToken);
    }

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database
    /// Used by: Messages.razor "Delete" button
    /// </summary>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Get bot client from factory (singleton instance)
            var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

            // 1. Delete the message from Telegram
            try
            {
                await botClient.DeleteMessage(chatId, (int)messageId, cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 2. Mark message as deleted in database
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(messageId, "manual_ui_delete");

            _logger.LogInformation("Deleted message {MessageId} from chat {ChatId} via UI", messageId, chatId);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId}", messageId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Temporarily ban user globally with automatic unrestriction.
    /// Used by: /tempban command
    /// </summary>
    public async Task<ModerationResult> TempBanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Temp ban globally (Telegram API handles auto-unrestrict via until_date)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        untilDate: expiresAt.DateTime,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to temp ban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record temp ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: expiresAt,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

            _logger.LogInformation(
                "Temp ban action completed: User {UserId} banned from {ChatsAffected} chats until {ExpiresAt}",
                userId, result.ChatsAffected, expiresAt);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to temp ban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Temp ban user without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
        return await TempBanUserAsync(botClient, userId, messageId, executorId, reason, duration, cancellationToken);
    }

    /// <summary>
    /// Restrict user (mute) globally with automatic unrestriction.
    /// Used by: /mute command
    /// </summary>
    public async Task<ModerationResult> RestrictUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Restrict user globally (Telegram API handles auto-unrestrict via until_date)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.RestrictChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        permissions: new global::Telegram.Bot.Types.ChatPermissions
                        {
                            CanSendMessages = false,
                            CanSendAudios = false,
                            CanSendDocuments = false,
                            CanSendPhotos = false,
                            CanSendVideos = false,
                            CanSendVideoNotes = false,
                            CanSendVoiceNotes = false,
                            CanSendPolls = false,
                            CanSendOtherMessages = false,
                            CanAddWebPagePreviews = false,
                            CanChangeInfo = false,
                            CanInviteUsers = false,
                            CanPinMessages = false,
                            CanManageTopics = false
                        },
                        untilDate: expiresAt.DateTime,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restrict user {UserId} in chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record mute action
            var muteAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Mute,
                MessageId: messageId,
                IssuedBy: ConvertToActor(executorId),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: expiresAt,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(muteAction, cancellationToken);

            _logger.LogInformation(
                "Mute action completed: User {UserId} restricted in {ChatsAffected} chats until {ExpiresAt}",
                userId, result.ChatsAffected, expiresAt);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restrict user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Map Telegram user ID to web app user ID (for audit trail)
    /// </summary>
    public async Task<string?> GetExecutorUserIdAsync(long? telegramUserId, CancellationToken cancellationToken = default)
    {
        if (telegramUserId == null) return null;
        return await _telegramUserMappingRepository.GetUserIdByTelegramIdAsync(telegramUserId.Value, cancellationToken);
    }

    /// <summary>
    /// Get executor identifier with fallback to Telegram username when no account mapping exists.
    /// Returns: Web app user ID (GUID) if mapped, otherwise "telegram:@username" or "telegram:userid"
    /// </summary>
    public async Task<string> GetExecutorIdentifierAsync(long telegramUserId, string? telegramUsername, CancellationToken cancellationToken = default)
    {
        // Try to get mapped web app user ID first
        var userId = await GetExecutorUserIdAsync(telegramUserId, cancellationToken);
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // Fallback to Telegram username
        if (!string.IsNullOrEmpty(telegramUsername))
        {
            return $"telegram:@{telegramUsername.TrimStart('@')}";
        }

        // Last resort: Telegram user ID
        return $"telegram:{telegramUserId}";
    }

    /// <summary>
    /// Convert legacy executor ID string to Actor object (Phase 4.19)
    /// </summary>
    private static Actor ConvertToActor(string? executorId)
    {
        if (string.IsNullOrEmpty(executorId))
        {
            return Actor.FromSystem("unknown");
        }

        // System identifiers (format: "system:identifier" or legacy patterns)
        if (executorId.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
        {
            var identifier = executorId.Substring(7); // Remove "system:" prefix
            return Actor.FromSystem(identifier);
        }

        // Check if it's a GUID (web user ID)
        if (Guid.TryParse(executorId, out _))
        {
            return Actor.FromWebUser(executorId);
        }

        // Telegram user identifiers (format: "telegram:@username" or "telegram:userid")
        if (executorId.StartsWith("telegram:", StringComparison.OrdinalIgnoreCase))
        {
            var telegramPart = executorId.Substring(9); // Remove "telegram:" prefix
            if (telegramPart.StartsWith("@"))
            {
                // Username format
                return Actor.FromSystem(executorId); // Store as system identifier for now
            }
            if (long.TryParse(telegramPart, out var telegramUserId))
            {
                // User ID format
                return Actor.FromTelegramUser(telegramUserId);
            }
        }

        // Legacy patterns from old system
        if (executorId == "Auto-Detection" || executorId == "Web Admin")
        {
            return Actor.FromSystem(executorId.ToLowerInvariant().Replace(" ", "_"));
        }

        // Default: treat as system identifier
        return Actor.FromSystem(executorId);
    }
}

/// <summary>
/// Result of a moderation action
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
}
