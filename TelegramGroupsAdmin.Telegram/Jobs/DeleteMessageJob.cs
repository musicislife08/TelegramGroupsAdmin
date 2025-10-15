using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Services.Telegram;

namespace TelegramGroupsAdmin.Telegram.Jobs;

/// <summary>
/// TickerQ job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    TelegramBotClientFactory botClientFactory,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<DeleteMessageJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramOptions _telegramOptions = telegramOptions.Value;

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

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

        try
        {
            await botClient.DeleteMessage(
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
