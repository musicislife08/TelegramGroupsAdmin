using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// Supports: reply to message, @username, user ID, or fuzzy name search
/// Notifies user via DM if available, falls back to chat mention
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;
    private readonly IUserMessagingService _messagingService;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply) | /ban @username | /ban <user_id> | /ban <name>";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => false; // Now supports multiple input methods
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public BanCommand(
        ILogger<BanCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService,
        IUserMessagingService messagingService,
        ITelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _messagingService = messagingService;
        _botClientFactory = botClientFactory;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        User? targetUser = null;
        long? triggerMessageId = null;

        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Option 1: Reply to message (existing behavior)
        if (message.ReplyToMessage != null)
        {
            targetUser = message.ReplyToMessage.From;
            triggerMessageId = message.ReplyToMessage.MessageId;

            if (targetUser == null)
            {
                return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }
        }
        // Option 2: Arguments provided
        else if (args.Length > 0)
        {
            var firstArg = args[0];

            // Check if numeric user ID (e.g., /ban 123456789)
            if (long.TryParse(firstArg, out var userId))
            {
                var user = await userRepository.GetByTelegramIdAsync(userId, cancellationToken);
                if (user == null)
                {
                    return new CommandResult($"❌ User ID {userId} not found.", DeleteCommandMessage, DeleteResponseAfterSeconds);
                }
                targetUser = CreateSyntheticUser(user);
            }
            // Check if @username (e.g., /ban @johndoe)
            else if (firstArg.StartsWith('@'))
            {
                var username = firstArg.TrimStart('@');
                var user = await userRepository.GetByUsernameAsync(username, cancellationToken);
                if (user == null)
                {
                    return new CommandResult($"❌ User @{username} not found.", DeleteCommandMessage, DeleteResponseAfterSeconds);
                }
                targetUser = CreateSyntheticUser(user);
            }
            // Otherwise: fuzzy name search (e.g., /ban john smith)
            else
            {
                var searchText = string.Join(" ", args);
                var matches = await userRepository.SearchByNameAsync(searchText, 5, cancellationToken);

                if (matches.Count == 0)
                {
                    return new CommandResult($"❌ No users found matching '{searchText}'.", DeleteCommandMessage, DeleteResponseAfterSeconds);
                }

                if (matches.Count == 1)
                {
                    // Single match - proceed with ban directly
                    targetUser = CreateSyntheticUser(matches[0]);
                }
                else
                {
                    // Multiple matches - show selection buttons
                    return await ShowUserSelectionAsync(message, matches, cancellationToken);
                }
            }
        }
        else
        {
            return new CommandResult(
                "❌ Reply to a message OR use: /ban @username | /ban <id> | /ban <name>",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser!.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot ban chat admins.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // Execute ban
        return await ExecuteBanAsync(message, targetUser, triggerMessageId, cancellationToken);
    }

    /// <summary>
    /// Shows inline keyboard with user options for fuzzy match results.
    /// </summary>
    private async Task<CommandResult> ShowUserSelectionAsync(
        Message commandMessage,
        List<Models.TelegramUser> matches,
        CancellationToken cancellationToken)
    {
        // Build inline keyboard with user options
        var buttons = matches.Select(u => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                FormatUserButton(u),
                $"{CallbackConstants.BanSelectPrefix}{u.TelegramUserId}:{commandMessage.MessageId}")
        }).ToList();

        // Add cancel button
        buttons.Add([InlineKeyboardButton.WithCallbackData("❌ Cancel", $"{CallbackConstants.BanCancelPrefix}{commandMessage.MessageId}")]);

        var keyboard = new InlineKeyboardMarkup(buttons);

        var operations = await _botClientFactory.GetOperationsAsync();
        await operations.SendMessageAsync(
            commandMessage.Chat.Id,
            "Multiple users found. Select one to ban:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        // Return null message - selection will be handled by callback handler
        // Don't delete command message yet (callback handler will delete both)
        return new CommandResult(null, false, null);
    }

    /// <summary>
    /// Executes the actual ban operation.
    /// </summary>
    private async Task<CommandResult> ExecuteBanAsync(
        Message message,
        User targetUser,
        long? triggerMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create executor actor from Telegram user
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.From.LastName);

            // Execute ban via ModerationOrchestrator
            var result = await _moderationService.BanUserAsync(
                userId: targetUser.Id,
                messageId: triggerMessageId,
                executor: executor,
                reason: ModerationConstants.DefaultBanReason,
                cancellationToken: cancellationToken);

            if (!result.Success)
            {
                return new CommandResult($"❌ Failed to ban user: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Notify user of ban via DM (preferred) or chat mention (fallback)
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var banNotification = $"🚫 **You have been banned**\n\n" +
                                 $"**Chat:** {chatName}\n" +
                                 $"**Reason:** {ModerationConstants.DefaultBanReason}\n" +
                                 $"**Chats affected:** {result.ChatsAffected}\n\n" +
                                 $"If you believe this was a mistake, you may appeal by contacting the chat administrators.";

            var messageResult = await _messagingService.SendToUserAsync(
                userId: targetUser.Id,
                chat: message.Chat,
                messageText: banNotification,
                replyToMessageId: null, // Don't reply to trigger message for bans
                cancellationToken: cancellationToken);

            var deliveryMethod = messageResult.DeliveryMethod == MessageDeliveryMethod.PrivateDm
                ? "DM"
                : "chat mention";

            _logger.LogInformation(
                "{TargetUser} banned by {Executor} from {ChatsAffected} chats. " +
                "Reason: {Reason}. User notified via {DeliveryMethod}. Trust removed: {TrustRemoved}",
                LogDisplayName.UserInfo(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                result.ChatsAffected, ModerationConstants.DefaultBanReason, deliveryMethod, result.TrustRemoved);

            // Silent mode: No chat feedback, command message simply disappears
            return new CommandResult(null, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban {User}",
                LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id));
            return new CommandResult($"❌ Failed to ban user: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }

    /// <summary>
    /// Creates a Telegram.Bot.Types.User from our domain model.
    /// </summary>
    private static User CreateSyntheticUser(Models.TelegramUser user) => new()
    {
        Id = user.TelegramUserId,
        Username = user.Username,
        FirstName = user.FirstName ?? "Unknown",
        LastName = user.LastName,
        IsBot = user.IsBot
    };

    /// <summary>
    /// Formats user info for inline keyboard button text.
    /// </summary>
    private static string FormatUserButton(Models.TelegramUser user)
    {
        var name = $"{user.FirstName ?? ""} {user.LastName ?? ""}".Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = user.Username ?? $"User {user.TelegramUserId}";
        }

        var username = user.Username != null ? $" (@{user.Username})" : "";
        return $"{name}{username}";
    }
}
