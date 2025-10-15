using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Telegram.Jobs;

/// <summary>
/// TickerQ job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    ITelegramBotClient botClient)
{
    private readonly ILogger<DeleteMessageJob> _logger = logger;
    private readonly ITelegramBotClient _botClient = botClient;

    /// <summary>
    /// Payload for delete message job
    /// </summary>
    public record DeletePayload(
        long ChatId,
        int MessageId,
        string Reason
    );

    /// <summary>
    /// Execute delayed message deletion
    /// Scheduled via TickerQ with configurable delay
    /// </summary>
    [TickerFunction(functionName: "DeleteMessage")]
    public async Task ExecuteAsync(TickerFunctionContext<DeletePayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("DeleteMessageJob received null payload");
            return;
        }

        _logger.LogDebug(
            "Deleting message {MessageId} in chat {ChatId} (reason: {Reason})",
            payload.MessageId,
            payload.ChatId,
            payload.Reason);

        try
        {
            await _botClient.DeleteMessage(
                chatId: payload.ChatId,
                messageId: payload.MessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);
            // Don't re-throw - message may have already been deleted manually
        }
    }
}
