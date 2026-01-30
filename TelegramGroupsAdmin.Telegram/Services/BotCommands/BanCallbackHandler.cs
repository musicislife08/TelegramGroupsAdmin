using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for ban user selection buttons.
/// Callback formats:
/// - ban_select:{userId}:{commandMessageId} - Execute ban for selected user
/// - ban_cancel:{commandMessageId} - Cancel selection, cleanup messages
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
/// </remarks>
public class BanCallbackHandler : IBanCallbackHandler
{
    private readonly ILogger<BanCallbackHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public BanCallbackHandler(
        ILogger<BanCallbackHandler> logger,
        IServiceScopeFactory scopeFactory,
        ITelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _botClientFactory = botClientFactory;
    }

    public bool CanHandle(string callbackData)
    {
        return callbackData.StartsWith(CallbackConstants.BanSelectPrefix) ||
               callbackData.StartsWith(CallbackConstants.BanCancelPrefix);
    }

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Ban callback received with null/empty data");
            return;
        }

        var chatId = callbackQuery.Message?.Chat.Id;
        var selectionMessageId = callbackQuery.Message?.MessageId;

        if (chatId == null || selectionMessageId == null)
        {
            _logger.LogWarning("Ban callback missing chat or message ID");
            return;
        }

        var operations = await _botClientFactory.GetOperationsAsync();

        if (data.StartsWith(CallbackConstants.BanSelectPrefix))
        {
            await HandleSelectAsync(callbackQuery, data, chatId.Value, selectionMessageId.Value, operations, cancellationToken);
        }
        else if (data.StartsWith(CallbackConstants.BanCancelPrefix))
        {
            await HandleCancelAsync(data, chatId.Value, selectionMessageId.Value, operations, cancellationToken);
        }
    }

    private async Task HandleSelectAsync(
        CallbackQuery callbackQuery,
        string data,
        long chatId,
        int selectionMessageId,
        ITelegramOperations operations,
        CancellationToken cancellationToken)
    {
        // Parse: ban_select:{userId}:{commandMessageId}
        var parts = data[CallbackConstants.BanSelectPrefix.Length..].Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var targetUserId) ||
            !int.TryParse(parts[1], out var commandMessageId))
        {
            _logger.LogWarning("Invalid ban_select callback format: {Data}", data);
            return;
        }

        var executorUser = callbackQuery.From;

        // Create scope for scoped services (ModerationOrchestrator, repositories, etc.)
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var targetUser = await userRepo.GetByTelegramIdAsync(targetUserId, cancellationToken);

        if (targetUser == null)
        {
            _logger.LogWarning("Ban target user {User} not found", LogDisplayName.UserDebug(null, null, null, targetUserId));
            await CleanupMessagesAsync(operations, chatId, selectionMessageId, commandMessageId, cancellationToken);
            return;
        }

        // Check if target is admin
        var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var isAdmin = await chatAdminsRepo.IsAdminAsync(chatId, targetUserId, cancellationToken);
        if (isAdmin)
        {
            _logger.LogWarning("Attempted to ban admin user {User}", targetUser.ToLogDebug(targetUserId));
            await CleanupMessagesAsync(operations, chatId, selectionMessageId, commandMessageId, cancellationToken);
            return;
        }

        try
        {
            // Create executor actor
            var executor = Core.Models.Actor.FromTelegramUser(
                executorUser.Id,
                executorUser.Username,
                executorUser.FirstName,
                executorUser.LastName);

            // Execute ban (resolve from scope since ModerationOrchestrator is Scoped)
            var moderationService = scope.ServiceProvider.GetRequiredService<IBotModerationService>();
            var result = await moderationService.BanUserAsync(
                userId: targetUserId,
                messageId: null, // No trigger message for fuzzy search bans
                executor: executor,
                reason: ModerationConstants.DefaultBanReason,
                cancellationToken: cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "{TargetUser} banned by {Executor} via selection button",
                    targetUser.ToLogInfo(targetUser.TelegramUserId),
                    executorUser.ToLogInfo());

                // Send ban notification to user (resolve from scope since IUserMessagingService is Scoped)
                var messagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();
                var chatName = callbackQuery.Message?.Chat.Title ?? "this chat";
                var banNotification = $"🚫 **You have been banned**\n\n" +
                                     $"**Chat:** {chatName}\n" +
                                     $"**Reason:** {ModerationConstants.DefaultBanReason}\n" +
                                     $"**Chats affected:** {result.ChatsAffected}\n\n" +
                                     $"If you believe this was a mistake, you may appeal by contacting the chat administrators.";

                await messagingService.SendToUserAsync(
                    userId: targetUserId,
                    chat: callbackQuery.Message!.Chat,
                    messageText: banNotification,
                    replyToMessageId: null,
                    cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogWarning("Ban failed for user {User}: {Error}", targetUser.ToLogDebug(targetUserId), result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ban for user {User}", targetUser.ToLogDebug(targetUserId));
        }

        // Cleanup messages
        await CleanupMessagesAsync(operations, chatId, selectionMessageId, commandMessageId, cancellationToken);
    }

    private async Task HandleCancelAsync(
        string data,
        long chatId,
        int selectionMessageId,
        ITelegramOperations operations,
        CancellationToken cancellationToken)
    {
        // Parse: ban_cancel:{commandMessageId}
        var commandMessageIdStr = data[CallbackConstants.BanCancelPrefix.Length..];
        if (!int.TryParse(commandMessageIdStr, out var commandMessageId))
        {
            _logger.LogWarning("Invalid ban_cancel callback format: {Data}", data);
            return;
        }

        _logger.LogDebug("Ban selection cancelled, cleaning up messages");

        // Cleanup messages
        await CleanupMessagesAsync(operations, chatId, selectionMessageId, commandMessageId, cancellationToken);
    }

    private async Task CleanupMessagesAsync(
        ITelegramOperations operations,
        long chatId,
        int selectionMessageId,
        int commandMessageId,
        CancellationToken cancellationToken)
    {
        // Delete the selection message (with buttons)
        try
        {
            await operations.DeleteMessageAsync(chatId, selectionMessageId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete selection message {MessageId}", selectionMessageId);
        }

        // Delete the original /ban command message
        try
        {
            await operations.DeleteMessageAsync(chatId, commandMessageId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete command message {MessageId}", commandMessageId);
        }
    }
}
