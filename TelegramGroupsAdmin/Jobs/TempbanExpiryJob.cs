using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to handle tempban expiry - completely removes user from "Removed users" list
/// Scheduled when tempban is issued, runs at expiry time
/// Calls UnbanChatMember() across all managed chats to allow invite link rejoining
/// Phase 4.6: Tempban with auto-unrestrict
/// </summary>
public class TempbanExpiryJob(
    ILogger<TempbanExpiryJob> logger,
    IDbContextFactory<AppDbContext> contextFactory,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader)
{
    private readonly ILogger<TempbanExpiryJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Execute tempban expiry - unban user from all managed chats
    /// Completely removes user from Telegram's "Removed users" list
    /// Allows user to use invite links to rejoin
    /// </summary>
    [TickerFunction(functionName: "TempbanExpiry")]
    public async Task ExecuteAsync(TickerFunctionContext<TempbanExpiryJobPayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("TempbanExpiryJob received null payload");
            return;
        }

        _logger.LogInformation(
            "Processing tempban expiry for user {UserId}. Reason: {Reason}, Expired at: {ExpiresAt}",
            payload.UserId,
            payload.Reason,
            payload.ExpiresAt);

        // Load bot config from database
        var botToken = await _configLoader.LoadConfigAsync();

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(botToken);

        try
        {
            // Get all managed chats to unban user from
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var managedChats = await dbContext.ManagedChats
                .Where(c => c.IsActive)
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "Found {ChatCount} active managed chats for tempban expiry",
                managedChats.Count);

            int successCount = 0;
            int failureCount = 0;

            // Unban user from all managed chats
            foreach (var chat in managedChats)
            {
                try
                {
                    await botClient.UnbanChatMember(
                        chatId: chat.ChatId,
                        userId: payload.UserId,
                        onlyIfBanned: true,
                        cancellationToken: cancellationToken);

                    successCount++;
                    _logger.LogInformation(
                        "Successfully unbanned user {UserId} from chat {ChatId} ({ChatName})",
                        payload.UserId,
                        chat.ChatId,
                        chat.ChatName);
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogWarning(
                        ex,
                        "Failed to unban user {UserId} from chat {ChatId} ({ChatName})",
                        payload.UserId,
                        chat.ChatId,
                        chat.ChatName);
                    // Continue processing other chats even if one fails
                }
            }

            _logger.LogInformation(
                "Completed tempban expiry for user {UserId}. Success: {SuccessCount}/{TotalCount} chats",
                payload.UserId,
                successCount,
                managedChats.Count);

            if (failureCount > 0)
            {
                _logger.LogWarning(
                    "Tempban expiry partially failed for user {UserId}. {FailureCount}/{TotalCount} chats failed to unban",
                    payload.UserId,
                    failureCount,
                    managedChats.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process tempban expiry for user {UserId}",
                payload.UserId);
            throw; // Re-throw to let TickerQ handle retry logic
        }
    }
}
