using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /warn - Issue warning to user (auto-ban after threshold)
/// Notifies user via DM if available, falls back to chat mention
/// </summary>
public class WarnCommand : IBotCommand
{
    private readonly ILogger<WarnCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationOrchestrator _moderationService;
    private readonly IUserMessagingService _messagingService;

    public string Name => "warn";
    public string Description => "Issue warning to user (auto-ban after threshold)";
    public string Usage => "/warn (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible as public warning
    public int? DeleteResponseAfterSeconds => null;

    public WarnCommand(
        ILogger<WarnCommand> logger,
        IServiceProvider serviceProvider,
        ModerationOrchestrator moderationService,
        IUserMessagingService messagingService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _messagingService = messagingService;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult("❌ Please reply to a message from the user to warn.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't warn admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot warn chat admins.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "No reason provided";

        try
        {
            // Create executor actor from Telegram user
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.From.LastName);

            // Execute warn action using service
            var result = await _moderationService.WarnUserAsync(
                userId: targetUser.Id,
                messageId: message.ReplyToMessage.MessageId,
                executor: executor,
                reason: reason,
                chatId: message.Chat.Id,
                cancellationToken: cancellationToken
            );

            if (!result.Success)
            {
                return new CommandResult($"❌ Failed to issue warning: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Notify user of warning via DM (preferred) or chat mention (fallback)
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var warningNotification = $"⚠️ **Warning Issued**\n\n" +
                                     $"**Chat:** {chatName}\n" +
                                     $"**Reason:** {reason}\n" +
                                     $"**Total Warnings:** {result.WarningCount}\n\n" +
                                     $"Please review the group rules.";

            if (result.AutoBanTriggered)
            {
                warningNotification += $"\n\n🚫 **Auto-ban triggered!** You have been banned from {result.ChatsAffected} chat(s) due to excessive warnings.";
            }

            var messageResult = await _messagingService.SendToUserAsync(
                userId: targetUser.Id,
                chatId: message.Chat.Id,
                messageText: warningNotification,
                replyToMessageId: message.ReplyToMessage.MessageId,
                cancellationToken: cancellationToken);

            // Build admin confirmation response
            var username = targetUser.Username ?? targetUser.FirstName ?? targetUser.Id.ToString();
            var deliveryNote = messageResult.DeliveryMethod == MessageDeliveryMethod.PrivateDm
                ? " (notified via DM)"
                : " (notified in chat)";

            var response = $"⚠️ Warning issued to {(targetUser.Username != null ? "@" + targetUser.Username : username)}{deliveryNote}\n" +
                          $"Reason: {reason}\n" +
                          $"Total warnings: {result.WarningCount}";

            if (result.AutoBanTriggered)
            {
                response += $"\n\n🚫 Auto-ban triggered! User has been banned from {result.ChatsAffected} chat(s).";
            }

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn {User}",
                LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id));
            return new CommandResult($"❌ Failed to issue warning: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}
