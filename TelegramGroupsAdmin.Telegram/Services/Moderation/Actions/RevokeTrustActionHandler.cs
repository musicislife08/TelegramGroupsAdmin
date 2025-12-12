using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles revoke trust intents by expiring all active trust records.
/// Called by the orchestrator when an action handler returns ShouldRevokeTrust: true.
/// This protects against compromised accounts that were previously trusted.
/// </summary>
public class RevokeTrustActionHandler : IActionHandler<RevokeTrustIntent, RevokeTrustResult>
{
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<RevokeTrustActionHandler> _logger;

    public RevokeTrustActionHandler(
        IUserActionsRepository userActionsRepository,
        ILogger<RevokeTrustActionHandler> logger)
    {
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    public async Task<RevokeTrustResult> HandleAsync(RevokeTrustIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Revoking trust for user {UserId} by {Executor}. Reason: {Reason}",
            intent.UserId, intent.Executor.GetDisplayText(), intent.Reason);

        try
        {
            // Expire all active trust actions for this user globally (all chats)
            await _userActionsRepository.ExpireTrustsForUserAsync(
                intent.UserId,
                chatId: null,
                ct);

            _logger.LogInformation(
                "Trust revoked for user {UserId} by {Executor}",
                intent.UserId, intent.Executor.GetDisplayText());

            return RevokeTrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke trust for user {UserId}", intent.UserId);
            return RevokeTrustResult.Failed(ex.Message);
        }
    }
}
