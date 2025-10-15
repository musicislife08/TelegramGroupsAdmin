using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Services.Telegram;

namespace TelegramGroupsAdmin.Telegram.Jobs;

/// <summary>
/// TickerQ job to handle welcome message timeout
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class WelcomeTimeoutJob(
    ILogger<WelcomeTimeoutJob> logger,
    IDbContextFactory<AppDbContext> contextFactory,
    TelegramBotClientFactory botClientFactory,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<WelcomeTimeoutJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramOptions _telegramOptions = telegramOptions.Value;

    /// <summary>
    /// Payload for welcome timeout job
    /// </summary>
    public record TimeoutPayload(
        long ChatId,
        long UserId,
        int WelcomeMessageId
    );

    /// <summary>
    /// Execute welcome timeout - kicks user if they haven't responded
    /// Scheduled via TickerQ with configurable delay (default 60s)
    /// </summary>
    [TickerFunction(functionName: "WelcomeTimeout")]
    public async Task ExecuteAsync(TickerFunctionContext<TimeoutPayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("WelcomeTimeoutJob received null payload");
            return;
        }

        _logger.LogInformation(
            "Processing welcome timeout for user {UserId} in chat {ChatId}",
            payload.UserId,
            payload.ChatId);

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

        try
        {
            // Check if user has responded
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var response = await dbContext.WelcomeResponses
                .Where(r => r.ChatId == payload.ChatId
                    && r.UserId == payload.UserId
                    && r.WelcomeMessageId == payload.WelcomeMessageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (response == null || response.Response != "pending")
            {
                _logger.LogInformation(
                    "User {UserId} already responded to welcome in chat {ChatId}, skipping timeout",
                    payload.UserId,
                    payload.ChatId);
                return;
            }

            _logger.LogInformation(
                "Welcome timeout: User {UserId} did not respond in chat {ChatId}",
                payload.UserId,
                payload.ChatId);

            // Kick user for timeout (ban then immediately unban)
            try
            {
                await botClient.BanChatMember(
                    chatId: payload.ChatId,
                    userId: payload.UserId,
                    cancellationToken: cancellationToken);
                await botClient.UnbanChatMember(
                    chatId: payload.ChatId,
                    userId: payload.UserId,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Kicked user {UserId} from chat {ChatId} due to welcome timeout",
                    payload.UserId,
                    payload.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to kick user {UserId} from chat {ChatId}",
                    payload.UserId,
                    payload.ChatId);
                // Continue to delete message and update response even if kick fails
            }

            // Delete welcome message
            try
            {
                await botClient.DeleteMessage(
                    chatId: payload.ChatId,
                    messageId: payload.WelcomeMessageId,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete welcome message {MessageId} in chat {ChatId}",
                    payload.WelcomeMessageId,
                    payload.ChatId);
            }

            // Update response record
            response.Response = "timeout";
            response.RespondedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recorded welcome timeout for user {UserId} in chat {ChatId}",
                payload.UserId,
                payload.ChatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process welcome timeout for user {UserId} in chat {ChatId}",
                payload.UserId,
                payload.ChatId);
            throw; // Re-throw to let TickerQ handle retry logic
        }
    }
}
