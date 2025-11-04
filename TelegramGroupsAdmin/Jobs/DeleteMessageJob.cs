using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader)
{
    private readonly ILogger<DeleteMessageJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Execute delayed message deletion
    /// Scheduled via TickerQ with configurable delay
    /// </summary>
    [TickerFunction(functionName: "DeleteMessage")]
    public async Task ExecuteAsync(TickerFunctionContext<DeleteMessagePayload> context, CancellationToken cancellationToken)
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

        // Load bot config from database
        var (botToken, _, apiServerUrl) = await _configLoader.LoadConfigAsync();

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(botToken, apiServerUrl);

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
            _logger.LogError(
                ex,
                "Failed to delete message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);
            throw; // Re-throw to let TickerQ handle retry logic and record exception
        }
    }
}
