using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Records moderation actions in the audit log (user_actions table).
/// Called directly by orchestrator after successful actions.
/// NOTE: LogWarnAsync only creates audit trail - warning records are inserted by WarnHandler.
/// </summary>
public class AuditHandler : IAuditHandler
{
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ITelegramUserRepository _userRepository;
    private readonly IManagedChatsRepository _chatsRepository;
    private readonly ILogger<AuditHandler> _logger;

    public AuditHandler(
        IUserActionsRepository userActionsRepository,
        ITelegramUserRepository userRepository,
        IManagedChatsRepository chatsRepository,
        ILogger<AuditHandler> logger)
    {
        _userActionsRepository = userActionsRepository;
        _userRepository = userRepository;
        _chatsRepository = chatsRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogBanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var record = CreateRecord(userId, UserActionType.Ban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogTempBanAsync(long userId, Actor executor, TimeSpan duration, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);
        var record = CreateRecord(userId, UserActionType.Ban, executor, reason, expiresAt: expiresAt);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogUnbanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var record = CreateRecord(userId, UserActionType.Unban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Unban, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogWarnAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        // NOTE: This creates an AUDIT TRAIL entry only.
        // The actual warning record is inserted by WarnHandler into the warnings table.
        var record = CreateRecord(userId, UserActionType.Warn, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Warn, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogTrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var record = CreateRecord(userId, UserActionType.Trust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Trust, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogUntrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var record = CreateRecord(userId, UserActionType.Untrust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Untrust, user, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogDeleteAsync(long messageId, long chatId, long userId, Actor executor, CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var chat = await _chatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        // userId is required for FK constraint to telegram_users (TargetUser navigation)
        var record = CreateRecord(userId, UserActionType.Delete, executor, null, messageId: messageId);
        await _userActionsRepository.InsertAsync(record, cancellationToken);

        _logger.LogDebug(
            "Recorded {ActionType} action for message {MessageId} from {User} in {Chat} by {Executor}",
            UserActionType.Delete, messageId, user.ToLogDebug(userId), chat.ToLogDebug(chatId), executor.GetDisplayText());
    }

    /// <inheritdoc />
    public async Task LogRestrictAsync(long userId, long chatId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var record = CreateRecord(userId, UserActionType.Mute, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Mute, user, userId, executor);
    }

    private static UserActionRecord CreateRecord(
        long userId,
        UserActionType actionType,
        Actor executor,
        string? reason,
        long? messageId = null,
        DateTimeOffset? expiresAt = null)
    {
        return new UserActionRecord(
            Id: 0,
            UserId: userId,
            ActionType: actionType,
            MessageId: messageId,
            IssuedBy: executor,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            Reason: reason);
    }

    private void LogRecorded(UserActionType actionType, TelegramUser? user, long userId, Actor executor)
    {
        _logger.LogDebug(
            "Recorded {ActionType} action for {User} by {Executor}",
            actionType, user.ToLogDebug(userId), executor.GetDisplayText());
    }
}
