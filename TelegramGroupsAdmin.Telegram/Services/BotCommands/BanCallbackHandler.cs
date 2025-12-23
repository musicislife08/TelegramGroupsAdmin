using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for ban user selection buttons.
/// Callback formats:
/// - ban_select:{userId}:{commandMessageId} - Execute ban for selected user
/// - ban_cancel:{commandMessageId} - Cancel selection, cleanup messages
/// </summary>
public class BanCallbackHandler : IBanCallbackHandler
{
    private readonly ILogger<BanCallbackHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ModerationOrchestrator _moderationService;
    private readonly IUserMessagingService _messagingService;

    private const string DefaultReason = "Banned by admin";

    public BanCallbackHandler(
        ILogger<BanCallbackHandler> logger,
        IServiceProvider serviceProvider,
        ITelegramBotClientFactory botClientFactory,
        ModerationOrchestrator moderationService,
        IUserMessagingService messagingService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClientFactory = botClientFactory;
        _moderationService = moderationService;
        _messagingService = messagingService;
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

        // Get target user details
        using var scope = _serviceProvider.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var targetUser = await userRepo.GetByTelegramIdAsync(targetUserId, cancellationToken);

        if (targetUser == null)
        {
            _logger.LogWarning("Ban target user {UserId} not found", targetUserId);
            await CleanupMessagesAsync(operations, chatId, selectionMessageId, commandMessageId, cancellationToken);
            return;
        }

        // Check if target is admin
        var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var isAdmin = await chatAdminsRepo.IsAdminAsync(chatId, targetUserId, cancellationToken);
        if (isAdmin)
        {
            _logger.LogWarning("Attempted to ban admin user {UserId}", targetUserId);
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

            // Execute ban
            var result = await _moderationService.BanUserAsync(
                userId: targetUserId,
                messageId: null, // No trigger message for fuzzy search bans
                executor: executor,
                reason: DefaultReason,
                cancellationToken: cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "{TargetUser} banned by {Executor} via selection button",
                    LogDisplayName.UserInfo(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.TelegramUserId),
                    LogDisplayName.UserInfo(executorUser.FirstName, executorUser.LastName, executorUser.Username, executorUser.Id));

                // Send ban notification to user
                var chatName = callbackQuery.Message?.Chat.Title ?? "this chat";
                var banNotification = $"🚫 **You have been banned**\n\n" +
                                     $"**Chat:** {chatName}\n" +
                                     $"**Reason:** {DefaultReason}\n" +
                                     $"**Chats affected:** {result.ChatsAffected}\n\n" +
                                     $"If you believe this was a mistake, you may appeal by contacting the chat administrators.";

                await _messagingService.SendToUserAsync(
                    userId: targetUserId,
                    chatId: chatId,
                    messageText: banNotification,
                    replyToMessageId: null,
                    cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogWarning("Ban failed for user {UserId}: {Error}", targetUserId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ban for user {UserId}", targetUserId);
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
