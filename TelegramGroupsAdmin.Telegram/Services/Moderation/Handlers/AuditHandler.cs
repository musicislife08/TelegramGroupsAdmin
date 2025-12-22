using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
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
    private readonly ILogger<AuditHandler> _logger;

    public AuditHandler(
        IUserActionsRepository userActionsRepository,
        ILogger<AuditHandler> logger)
    {
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogBanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(userId, UserActionType.Ban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogTempBanAsync(long userId, Actor executor, TimeSpan duration, string? reason, CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);
        var record = CreateRecord(userId, UserActionType.Ban, executor, reason, expiresAt: expiresAt);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogUnbanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(userId, UserActionType.Unban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Unban, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogWarnAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        // NOTE: This creates an AUDIT TRAIL entry only.
        // The actual warning record is inserted by WarnHandler into the warnings table.
        var record = CreateRecord(userId, UserActionType.Warn, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Warn, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogTrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(userId, UserActionType.Trust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Trust, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogUntrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(userId, UserActionType.Untrust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Untrust, userId, executor);
    }

    /// <inheritdoc />
    public async Task LogDeleteAsync(long messageId, long chatId, long userId, Actor executor, CancellationToken cancellationToken = default)
    {
        // userId is required for FK constraint to telegram_users (TargetUser navigation)
        var record = CreateRecord(userId, UserActionType.Delete, executor, null, messageId: messageId);
        await _userActionsRepository.InsertAsync(record, cancellationToken);

        _logger.LogDebug(
            "Recorded {ActionType} action for message {MessageId} from user {UserId} in chat {ChatId} by {Executor}",
            UserActionType.Delete, messageId, userId, chatId, executor.GetDisplayText());
    }

    /// <inheritdoc />
    public async Task LogRestrictAsync(long userId, long chatId, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(userId, UserActionType.Mute, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Mute, userId, executor);
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

    private void LogRecorded(UserActionType actionType, long userId, Actor executor)
    {
        _logger.LogDebug(
            "Recorded {ActionType} action for user {UserId} by {Executor}",
            actionType, userId, executor.GetDisplayText());
    }
}
