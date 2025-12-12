using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles mark as spam intents by deleting the message, banning the user, and revoking trust.
/// This is a composite handler that coordinates DeleteActionHandler, BanActionHandler, and RevokeTrustActionHandler.
///
/// Note: This handler does NOT dispatch side-effects directly - the orchestrator handles that
/// after receiving our result, which ensures proper separation between action execution and side-effects.
/// </summary>
public class MarkAsSpamActionHandler : IActionHandler<MarkAsSpamIntent, MarkAsSpamResult>
{
    private readonly IActionHandler<DeleteIntent, DeleteResult> _deleteHandler;
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly IActionHandler<RevokeTrustIntent, RevokeTrustResult> _revokeTrustHandler;
    private readonly ILogger<MarkAsSpamActionHandler> _logger;

    public MarkAsSpamActionHandler(
        IActionHandler<DeleteIntent, DeleteResult> deleteHandler,
        ICrossChatExecutor crossChatExecutor,
        IActionHandler<RevokeTrustIntent, RevokeTrustResult> revokeTrustHandler,
        ILogger<MarkAsSpamActionHandler> logger)
    {
        _deleteHandler = deleteHandler;
        _crossChatExecutor = crossChatExecutor;
        _revokeTrustHandler = revokeTrustHandler;
        _logger = logger;
    }

    public async Task<MarkAsSpamResult> HandleAsync(MarkAsSpamIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Executing mark as spam for message {MessageId} by user {UserId} in chat {ChatId}",
            intent.MessageId, intent.UserId, intent.ChatId);

        try
        {
            // Step 1: Delete the message
            var deleteIntent = new DeleteIntent(
                intent.MessageId,
                intent.ChatId,
                intent.UserId,
                intent.Executor,
                $"Spam: {intent.Reason}");

            var deleteResult = await _deleteHandler.HandleAsync(deleteIntent, ct);

            // Step 2: Ban the user globally
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.BanChatMemberAsync(chatId, intent.UserId, ct: token),
                "MarkAsSpamBan",
                ct);

            // Step 3: Revoke trust (compromised account protection)
            var revokeTrustIntent = new RevokeTrustIntent(
                intent.UserId,
                intent.Executor,
                $"Trust revoked due to spam: {intent.Reason}");

            var revokeTrustResult = await _revokeTrustHandler.HandleAsync(revokeTrustIntent, ct);

            _logger.LogInformation(
                "Mark as spam completed: Message {MessageId} deleted={Deleted}, " +
                "user {UserId} banned from {ChatsAffected} chats, trust revoked={TrustRevoked}",
                intent.MessageId, deleteResult.MessageDeleted,
                intent.UserId, crossResult.SuccessCount, revokeTrustResult.Success);

            return MarkAsSpamResult.Succeeded(
                chatsAffected: crossResult.SuccessCount,
                messageDeleted: deleteResult.MessageDeleted,
                trustRevoked: revokeTrustResult.Success,
                chatsFailed: crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute mark as spam for message {MessageId}", intent.MessageId);
            return MarkAsSpamResult.Failed(ex.Message);
        }
    }
}
