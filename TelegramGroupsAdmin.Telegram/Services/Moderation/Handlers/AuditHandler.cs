using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
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
    public async Task LogBanAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Ban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, user, executor);
    }

    /// <inheritdoc />
    public async Task LogTempBanAsync(UserIdentity user, Actor executor, TimeSpan duration, string? reason, CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);
        var record = CreateRecord(user.Id, UserActionType.Ban, executor, reason, expiresAt: expiresAt);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Ban, user, executor);
    }

    /// <inheritdoc />
    public async Task LogUnbanAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Unban, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Unban, user, executor);
    }

    /// <inheritdoc />
    public async Task LogWarnAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        // NOTE: This creates an AUDIT TRAIL entry only.
        // The actual warning record is inserted by WarnHandler into the warnings table.
        var record = CreateRecord(user.Id, UserActionType.Warn, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Warn, user, executor);
    }

    /// <inheritdoc />
    public async Task LogTrustAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Trust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Trust, user, executor);
    }

    /// <inheritdoc />
    public async Task LogUntrustAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Untrust, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Untrust, user, executor);
    }

    /// <inheritdoc />
    public async Task LogDeleteAsync(long messageId, ChatIdentity chat, UserIdentity user, Actor executor, CancellationToken cancellationToken = default)
    {
        // userId is required for FK constraint to telegram_users (TargetUser navigation)
        var record = CreateRecord(user.Id, UserActionType.Delete, executor, null, messageId: messageId);
        await _userActionsRepository.InsertAsync(record, cancellationToken);

        _logger.LogDebug(
            "Recorded {ActionType} action for message {MessageId} from {User} in {Chat} by {Executor}",
            UserActionType.Delete, messageId, user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());
    }

    /// <inheritdoc />
    public async Task LogRestrictAsync(UserIdentity user, ChatIdentity? chat, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Mute, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);
        LogRecorded(UserActionType.Mute, user, executor);
    }

    /// <inheritdoc />
    public async Task LogRestorePermissionsAsync(UserIdentity user, ChatIdentity chat, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.RestorePermissions, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);

        _logger.LogDebug(
            "Recorded {ActionType} action for {User} in {Chat} by {Executor}",
            UserActionType.RestorePermissions, user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());
    }

    /// <inheritdoc />
    public async Task LogKickAsync(UserIdentity user, ChatIdentity chat, Actor executor, string? reason, CancellationToken cancellationToken = default)
    {
        var record = CreateRecord(user.Id, UserActionType.Kick, executor, reason);
        await _userActionsRepository.InsertAsync(record, cancellationToken);

        _logger.LogDebug(
            "Recorded {ActionType} action for {User} in {Chat} by {Executor}",
            UserActionType.Kick, user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());
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

    private void LogRecorded(UserActionType actionType, UserIdentity user, Actor executor)
    {
        _logger.LogDebug(
            "Recorded {ActionType} action for {User} by {Executor}",
            actionType, user.ToLogDebug(), executor.GetDisplayText());
    }
}
