using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Thin orchestration layer that routes moderation intents to action handlers
/// and dispatches events for side-effects (audit, notifications, training data).
///
/// This replaces ModerationActionService with a cleaner separation:
/// - Action handlers own the domain logic (Telegram API calls, DB updates)
/// - Side-effect handlers react to events (audit log, notifications, training)
/// - Orchestrator only routes and coordinates
/// </summary>
public class ModerationOrchestrator
{
    private readonly IActionHandler<BanIntent, BanResult> _banHandler;
    private readonly IActionHandler<UnbanIntent, UnbanResult> _unbanHandler;
    private readonly IActionHandler<WarnIntent, WarnResult> _warnHandler;
    private readonly IActionHandler<TrustIntent, TrustResult> _trustHandler;
    private readonly IActionHandler<RevokeTrustIntent, RevokeTrustResult> _revokeTrustHandler;
    private readonly IActionHandler<DeleteIntent, DeleteResult> _deleteHandler;
    private readonly IActionHandler<TempBanIntent, TempBanResult> _tempBanHandler;
    private readonly IActionHandler<RestrictIntent, RestrictResult> _restrictHandler;
    private readonly IActionHandler<MarkAsSpamIntent, MarkAsSpamResult> _markAsSpamHandler;
    private readonly IModerationEventDispatcher _dispatcher;
    private readonly ILogger<ModerationOrchestrator> _logger;

    public ModerationOrchestrator(
        IActionHandler<BanIntent, BanResult> banHandler,
        IActionHandler<UnbanIntent, UnbanResult> unbanHandler,
        IActionHandler<WarnIntent, WarnResult> warnHandler,
        IActionHandler<TrustIntent, TrustResult> trustHandler,
        IActionHandler<RevokeTrustIntent, RevokeTrustResult> revokeTrustHandler,
        IActionHandler<DeleteIntent, DeleteResult> deleteHandler,
        IActionHandler<TempBanIntent, TempBanResult> tempBanHandler,
        IActionHandler<RestrictIntent, RestrictResult> restrictHandler,
        IActionHandler<MarkAsSpamIntent, MarkAsSpamResult> markAsSpamHandler,
        IModerationEventDispatcher dispatcher,
        ILogger<ModerationOrchestrator> logger)
    {
        _banHandler = banHandler;
        _unbanHandler = unbanHandler;
        _warnHandler = warnHandler;
        _trustHandler = trustHandler;
        _revokeTrustHandler = revokeTrustHandler;
        _deleteHandler = deleteHandler;
        _tempBanHandler = tempBanHandler;
        _restrictHandler = restrictHandler;
        _markAsSpamHandler = markAsSpamHandler;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Mark message as spam, delete it, ban user globally, and revoke trust.
    /// </summary>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        Actor executor,
        string reason,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null) return protectionResult;

        var intent = new MarkAsSpamIntent(messageId, userId, chatId, executor, reason, telegramMessage);
        var result = await _markAsSpamHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for side-effects (training data, audit log)
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.MarkAsSpamAndBan,
            UserId = userId,
            MessageId = messageId,
            ChatId = chatId,
            Executor = executor,
            Reason = reason,
            TelegramMessage = telegramMessage,
            ChatsAffected = result.ChatsAffected,
            TrustRemoved = result.TrustRevoked,
            MessageDeleted = result.MessageDeleted
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = result.ChatsAffected,
            MessageDeleted = result.MessageDeleted,
            TrustRemoved = result.TrustRevoked
        };
    }

    /// <summary>
    /// Ban user globally across all managed chats.
    /// </summary>
    public async Task<ModerationResult> BanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null) return protectionResult;

        var intent = new BanIntent(userId, messageId, executor, reason);
        var result = await _banHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Revoke trust if action handler indicates it should be done
        var trustRevoked = false;
        if (result.ShouldRevokeTrust)
        {
            var revokeTrustResult = await _revokeTrustHandler.HandleAsync(
                new RevokeTrustIntent(userId, executor, $"Trust revoked due to ban: {reason}"), cancellationToken);
            trustRevoked = revokeTrustResult.Success;
        }

        // Dispatch event for side-effects
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Ban,
            UserId = userId,
            MessageId = messageId,
            Executor = executor,
            Reason = reason,
            ChatsAffected = result.ChatsAffected,
            TrustRemoved = trustRevoked
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = result.ChatsAffected,
            TrustRemoved = trustRevoked
        };
    }

    /// <summary>
    /// Warn user globally with automatic ban after threshold.
    /// </summary>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null) return protectionResult;

        var intent = new WarnIntent(userId, messageId, chatId, executor, reason);
        var result = await _warnHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for side-effects (audit log, DM notification, warning threshold check)
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Warn,
            UserId = userId,
            MessageId = messageId,
            ChatId = chatId,
            Executor = executor,
            Reason = reason,
            WarningCount = result.WarningCount
        };

        var dispatchResult = await _dispatcher.DispatchAsync(evt, cancellationToken);

        var moderationResult = new ModerationResult
        {
            Success = true,
            WarningCount = result.WarningCount
        };

        // Handle follow-up action (auto-ban on warning threshold)
        if (dispatchResult.FollowUp == ModerationFollowUp.Ban)
        {
            _logger.LogWarning(
                "Auto-ban triggered: User {UserId} has {WarnCount} warnings",
                userId, result.WarningCount);

            var autoBanReason = $"Exceeded warning threshold ({result.WarningCount} warnings)";
            var banResult = await BanUserAsync(userId, messageId, Actor.AutoBan, autoBanReason, cancellationToken);

            if (banResult.Success)
            {
                moderationResult.AutoBanTriggered = true;
                moderationResult.ChatsAffected = banResult.ChatsAffected;
            }
            else
            {
                _logger.LogError(
                    "Auto-ban failed for user {UserId}: {Error}",
                    userId, banResult.ErrorMessage);
            }
        }

        return moderationResult;
    }

    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// </summary>
    public async Task<ModerationResult> TrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var intent = new TrustIntent(userId, executor, reason);
        var result = await _trustHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for audit logging
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Trust,
            UserId = userId,
            Executor = executor,
            Reason = reason
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult { Success = true };
    }

    /// <summary>
    /// Unban user globally and optionally restore trust.
    /// </summary>
    public async Task<ModerationResult> UnbanUserAsync(
        long userId,
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var intent = new UnbanIntent(userId, executor, reason, restoreTrust);
        var result = await _unbanHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for audit logging
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Unban,
            UserId = userId,
            Executor = executor,
            Reason = reason,
            ChatsAffected = result.ChatsAffected
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        var moderationResult = new ModerationResult
        {
            Success = true,
            ChatsAffected = result.ChatsAffected
        };

        // Handle trust restoration as follow-up action
        if (restoreTrust)
        {
            var trustResult = await TrustUserAsync(
                userId, executor,
                "Trust restored after unban (false positive correction)", cancellationToken);
            moderationResult.TrustRestored = trustResult.Success;
        }

        return moderationResult;
    }

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database.
    /// </summary>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        long userId,
        Actor deletedBy,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var intent = new DeleteIntent(messageId, chatId, userId, deletedBy, reason);
        var result = await _deleteHandler.HandleAsync(intent, cancellationToken);

        // Even if deletion failed, we consider the action successful
        // (message may already be deleted)

        // Dispatch event for audit logging
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Delete,
            UserId = userId,
            MessageId = messageId,
            ChatId = chatId,
            Executor = deletedBy,
            Reason = reason ?? "Manual message deletion",
            MessageDeleted = result.MessageDeleted
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = result.MessageDeleted
        };
    }

    /// <summary>
    /// Temporarily ban user globally with automatic unrestriction.
    /// </summary>
    public async Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null) return protectionResult;

        var intent = new TempBanIntent(userId, messageId, executor, reason, duration);
        var result = await _tempBanHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for side-effects (audit log, DM notification with rejoin links)
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.TempBan,
            UserId = userId,
            MessageId = messageId,
            Executor = executor,
            Reason = reason,
            Duration = duration,
            ExpiresAt = result.ExpiresAt,
            ChatsAffected = result.ChatsAffected
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = result.ChatsAffected
        };
    }

    /// <summary>
    /// Restrict user (mute) globally with automatic unrestriction.
    /// </summary>
    public async Task<ModerationResult> RestrictUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null) return protectionResult;

        var intent = new RestrictIntent(userId, messageId, executor, reason, duration);
        var result = await _restrictHandler.HandleAsync(intent, cancellationToken);

        if (!result.Success)
            return new ModerationResult { Success = false, ErrorMessage = result.ErrorMessage };

        // Dispatch event for audit logging
        var evt = new ModerationEvent
        {
            ActionType = ModerationActionType.Restrict,
            UserId = userId,
            MessageId = messageId,
            Executor = executor,
            Reason = reason,
            Duration = duration,
            ExpiresAt = result.ExpiresAt,
            ChatsAffected = result.ChatsAffected
        };

        await _dispatcher.DispatchAsync(evt, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = result.ChatsAffected
        };
    }

    /// <summary>
    /// Checks if user is Telegram's service account (777000) and returns error if moderation is attempted.
    /// </summary>
    private ModerationResult? CheckServiceAccountProtection(long userId)
    {
        if (userId == TelegramConstants.ServiceAccountUserId)
        {
            _logger.LogWarning(
                "Moderation action blocked for Telegram service account (user {UserId})",
                userId);

            return new ModerationResult
            {
                Success = false,
                ErrorMessage = "Cannot perform moderation actions on Telegram service account (channel/anonymous posts)"
            };
        }

        return null;
    }
}

/// <summary>
/// Result of a moderation action (unchanged from original for backward compatibility).
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
