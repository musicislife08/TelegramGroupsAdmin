using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles ban intents by executing Telegram ban API calls across all managed chats.
/// This is the domain expert for banning - it owns the Telegram integration.
/// </summary>
public class BanActionHandler : IActionHandler<BanIntent, BanResult>
{
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly ILogger<BanActionHandler> _logger;

    public BanActionHandler(
        ICrossChatExecutor crossChatExecutor,
        ILogger<BanActionHandler> logger)
    {
        _crossChatExecutor = crossChatExecutor;
        _logger = logger;
    }

    public async Task<BanResult> HandleAsync(BanIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Executing ban for user {UserId} by {Executor}",
            intent.UserId, intent.Executor.GetDisplayText());

        try
        {
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.BanChatMemberAsync(chatId, intent.UserId, ct: token),
                "Ban",
                ct);

            _logger.LogInformation(
                "Ban completed for user {UserId}: {Success} succeeded, {Failed} failed",
                intent.UserId, crossResult.SuccessCount, crossResult.FailCount);

            return BanResult.Succeeded(crossResult.SuccessCount, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute ban for user {UserId}", intent.UserId);
            return BanResult.Failed(ex.Message);
        }
    }
}
