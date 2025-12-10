using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// Notifies user via DM if available, falls back to chat mention
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;
    private readonly IUserMessagingService _messagingService;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public BanCommand(
        ILogger<BanCommand> logger,
        IServiceProvider serviceProvider,
        ModerationActionService moderationService,
        IUserMessagingService messagingService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _messagingService = messagingService;
    }

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult("❌ Please reply to a message from the user to ban.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot ban chat admins.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "Banned by admin";

        try
        {
            // Create executor actor from Telegram user
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.From.LastName);

            // Execute ban via ModerationActionService
            var result = await _moderationService.BanUserAsync(
                botClient,
                targetUser.Id,
                message.ReplyToMessage.MessageId,
                executor,
                reason,
                cancellationToken);

            if (!result.Success)
            {
                return new CommandResult($"❌ Failed to ban user: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Notify user of ban via DM (preferred) or chat mention (fallback)
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var banNotification = $"🚫 **You have been banned**\n\n" +
                                 $"**Chat:** {chatName}\n" +
                                 $"**Reason:** {reason}\n" +
                                 $"**Chats affected:** {result.ChatsAffected}\n\n" +
                                 $"If you believe this was a mistake, you may appeal by contacting the chat administrators.";

            var messageResult = await _messagingService.SendToUserAsync(
                botClient,
                userId: targetUser.Id,
                chatId: message.Chat.Id,
                messageText: banNotification,
                replyToMessageId: null, // Don't reply to trigger message for bans
                cancellationToken);

            var deliveryMethod = messageResult.DeliveryMethod == MessageDeliveryMethod.PrivateDm
                ? "DM"
                : "chat mention";

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) banned by {ExecutorId} from {ChatsAffected} chats. " +
                "Reason: {Reason}. User notified via {DeliveryMethod}. Trust removed: {TrustRemoved}",
                targetUser.Id, targetUser.Username, message.From?.Id, result.ChatsAffected, reason, deliveryMethod, result.TrustRemoved);

            // Silent mode: No chat feedback, command message simply disappears
            // Admins see action through DM notifications if enabled
            return new CommandResult(null, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban user {UserId}", targetUser.Id);
            return new CommandResult($"❌ Failed to ban user: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}
