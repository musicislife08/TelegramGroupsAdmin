using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles delete intents by deleting messages from Telegram and marking them in the database.
/// This is the domain expert for message deletion - it owns the Telegram and DB integration.
/// </summary>
public class DeleteActionHandler : IActionHandler<DeleteIntent, DeleteResult>
{
    private readonly BotMessageService _botMessageService;
    private readonly ILogger<DeleteActionHandler> _logger;

    public DeleteActionHandler(
        BotMessageService botMessageService,
        ILogger<DeleteActionHandler> logger)
    {
        _botMessageService = botMessageService;
        _logger = logger;
    }

    public async Task<DeleteResult> HandleAsync(DeleteIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Deleting message {MessageId} from chat {ChatId} by {Executor}",
            intent.MessageId, intent.ChatId, intent.Executor.GetDisplayText());

        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                intent.ChatId,
                (int)intent.MessageId,
                deletionSource: "moderation_action",
                ct);

            _logger.LogInformation(
                "Deleted message {MessageId} from chat {ChatId}",
                intent.MessageId, intent.ChatId);

            return DeleteResult.Succeeded(messageDeleted: true);
        }
        catch (Exception ex)
        {
            // Message deletion failures are often due to the message already being deleted
            // This is a soft failure - we log it but don't fail the entire action
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)",
                intent.MessageId, intent.ChatId);

            // Return success with messageDeleted: false - the action itself succeeded,
            // we just couldn't delete the message (probably already gone)
            return DeleteResult.Succeeded(messageDeleted: false);
        }
    }
}
