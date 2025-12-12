using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles unban intents by expiring ban records and executing Telegram unban API calls.
/// This is the domain expert for unbanning - it owns both DB updates and Telegram integration.
/// </summary>
public class UnbanActionHandler : IActionHandler<UnbanIntent, UnbanResult>
{
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<UnbanActionHandler> _logger;

    public UnbanActionHandler(
        ICrossChatExecutor crossChatExecutor,
        IUserActionsRepository userActionsRepository,
        ILogger<UnbanActionHandler> logger)
    {
        _crossChatExecutor = crossChatExecutor;
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    public async Task<UnbanResult> HandleAsync(UnbanIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Executing unban for user {UserId} by {Executor}",
            intent.UserId, intent.Executor.GetDisplayText());

        try
        {
            // First, expire all active bans in our database
            await _userActionsRepository.ExpireBansForUserAsync(intent.UserId, chatId: null, ct);

            // Then, unban from all Telegram chats
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.UnbanChatMemberAsync(chatId, intent.UserId, ct: token),
                "Unban",
                ct);

            _logger.LogInformation(
                "Unban completed for user {UserId}: {Success} succeeded, {Failed} failed",
                intent.UserId, crossResult.SuccessCount, crossResult.FailCount);

            return UnbanResult.Succeeded(crossResult.SuccessCount, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute unban for user {UserId}", intent.UserId);
            return UnbanResult.Failed(ex.Message);
        }
    }
}
