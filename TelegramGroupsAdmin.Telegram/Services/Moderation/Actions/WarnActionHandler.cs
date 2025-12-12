using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles warn intents by counting existing warnings.
/// Note: The actual warning record is inserted by AuditLogHandler (it creates UserActionRecord).
/// This handler's job is to calculate and return the warning count for threshold checking.
/// </summary>
public class WarnActionHandler : IActionHandler<WarnIntent, WarnResult>
{
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<WarnActionHandler> _logger;

    public WarnActionHandler(
        IUserActionsRepository userActionsRepository,
        ILogger<WarnActionHandler> logger)
    {
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    public async Task<WarnResult> HandleAsync(WarnIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Processing warning for user {UserId} by {Executor}",
            intent.UserId, intent.Executor.GetDisplayText());

        try
        {
            // Get current warning count BEFORE the new warning (which will be recorded by AuditLogHandler)
            var currentCount = await _userActionsRepository.GetWarnCountAsync(
                intent.UserId,
                intent.ChatId,
                ct);

            // The new count will be after AuditLogHandler inserts the record
            var newCount = currentCount + 1;

            _logger.LogInformation(
                "Warning processed for user {UserId}: total warnings will be {WarnCount}",
                intent.UserId, newCount);

            return WarnResult.Succeeded(newCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process warning for user {UserId}", intent.UserId);
            return WarnResult.Failed(ex.Message);
        }
    }
}
