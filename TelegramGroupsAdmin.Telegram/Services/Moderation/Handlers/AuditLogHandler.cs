using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Records moderation actions in the audit log.
/// Order: 100 (runs after business logic handlers, before notifications)
/// </summary>
public class AuditLogHandler : IModerationHandler
{
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<AuditLogHandler> _logger;

    public int Order => 100;

    // Handle all action types
    public ModerationActionType[] AppliesTo => [];

    public AuditLogHandler(
        IUserActionsRepository userActionsRepository,
        ILogger<AuditLogHandler> logger)
    {
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    public async Task<ModerationFollowUp> HandleAsync(ModerationEvent evt, CancellationToken ct = default)
    {
        var userActionType = MapToUserActionType(evt.ActionType);

        var auditRecord = new UserActionRecord(
            Id: 0,
            UserId: evt.UserId,
            ActionType: userActionType,
            MessageId: evt.MessageId,
            IssuedBy: evt.Executor,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: evt.ExpiresAt,
            Reason: evt.Reason
        );

        await _userActionsRepository.InsertAsync(auditRecord, ct);

        _logger.LogDebug(
            "Recorded {ActionType} action for user {UserId} by {Executor}",
            userActionType, evt.UserId, evt.Executor.GetDisplayText());

        return ModerationFollowUp.None;
    }

    private static UserActionType MapToUserActionType(ModerationActionType actionType)
    {
        return actionType switch
        {
            ModerationActionType.MarkAsSpamAndBan => UserActionType.Ban,
            ModerationActionType.Ban => UserActionType.Ban,
            ModerationActionType.TempBan => UserActionType.Ban,
            ModerationActionType.Warn => UserActionType.Warn,
            ModerationActionType.Trust => UserActionType.Trust,
            ModerationActionType.Unban => UserActionType.Unban,
            ModerationActionType.Delete => UserActionType.Delete,
            ModerationActionType.Restrict => UserActionType.Mute,
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unknown moderation action type")
        };
    }
}
