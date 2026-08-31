using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// Supports: reply to message, @username, user ID, or fuzzy name search
/// Notifies user via DM only - a banned user cannot read a chat mention
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;
    private readonly IUserMessagingService _messagingService;
    private readonly IBotMessageService _messageService;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply) | /ban @username | /ban <user_id> | /ban <name>";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => false; // Now supports multiple input methods
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public BanCommand(
        ILogger<BanCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService,
        IUserMessagingService messagingService,
        IBotMessageService messageService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _messagingService = messagingService;
        _messageService = messageService;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        UserIdentity? targetIdentity = null;
        int? triggerMessageId = null;

        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Option 1: Reply to message (existing behavior)
        if (message.ReplyToMessage != null)
        {
            if (message.ReplyToMessage.From == null)
            {
                return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            targetIdentity = UserIdentity.From(message.ReplyToMessage.From);
            triggerMessageId = message.ReplyToMessage.MessageId;
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
                    return new CommandResult(TelegramMessage.Plain($"❌ User ID {userId} not found."), DeleteCommandMessage, DeleteResponseAfterSeconds);
                }
                targetIdentity = UserIdentity.From(user);
            }
            // Check if @username (e.g., /ban @johndoe)
            else if (firstArg.StartsWith('@'))
            {
                var username = firstArg.TrimStart('@');
                var user = await userRepository.GetByUsernameAsync(username, cancellationToken);
                if (user == null)
                {
                    return new CommandResult(TelegramMessage.Plain($"❌ User @{username} not found."), DeleteCommandMessage, DeleteResponseAfterSeconds);
                }
                targetIdentity = UserIdentity.From(user);
            }
            // Otherwise: fuzzy name search (e.g., /ban john smith)
            else
            {
                var searchText = string.Join(" ", args);
                var matches = await userRepository.SearchByNameAsync(searchText, 5, cancellationToken);

                if (matches.Count == 0)
                {
                    return new CommandResult(TelegramMessage.Plain($"❌ No users found matching '{searchText}'."), DeleteCommandMessage, DeleteResponseAfterSeconds);
                }

                if (matches.Count == 1)
                {
                    // Single match - proceed with ban directly
                    targetIdentity = UserIdentity.From(matches[0]);
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
                TelegramMessage.Plain("❌ Reply to a message OR use: /ban @username | /ban <id> | /ban <name>"),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetIdentity!.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Cannot ban chat admins."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // Execute ban
        return await ExecuteBanAsync(message, targetIdentity, triggerMessageId, cancellationToken);
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

        await _messageService.SendAndSaveMessageAsync(
            commandMessage.Chat.Id,
            "Multiple users found. Select one to ban:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        // Return null message - selection will be handled by callback handler
        // Don't delete command message yet (callback handler will delete both)
        return new CommandResult(TelegramMessage.Empty, false, null);
    }

    /// <summary>
    /// Executes the actual ban operation.
    /// </summary>
    private async Task<CommandResult> ExecuteBanAsync(
        Message message,
        UserIdentity targetIdentity,
        int? triggerMessageId,
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

            // Execute ban via BotModerationService
            var result = await _moderationService.BanUserAsync(
                new BanIntent
                {
                    User = targetIdentity,
                    Executor = executor,
                    Reason = ModerationConstants.DefaultBanReason,
                    MessageId = triggerMessageId,
                    Chat = ChatIdentity.From(message.Chat)
                },
                cancellationToken);

            if (!result.Success)
            {
                return new CommandResult(TelegramMessage.Plain($"❌ Failed to ban user: {result.ErrorMessage}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Notify user of ban via DM only - they are out of the chat, so a mention is just noise
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var banNotification = BanNotificationMessage.Build(
                chatName, ModerationConstants.DefaultBanReason, result.ChatsAffected);

            var messageResult = await _messagingService.SendDmOnlyAsync(
                userId: targetIdentity.Id,
                message: banNotification,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "{TargetUser} banned by {Executor} from {ChatsAffected} chats. " +
                "Reason: {Reason}. Ban DM delivered: {DmDelivered}. Trust removed: {TrustRemoved}",
                targetIdentity.ToLogInfo(),
                message.From.ToLogInfo(),
                result.ChatsAffected, ModerationConstants.DefaultBanReason, messageResult.Success, result.TrustRemoved);

            // Silent mode: No chat feedback, command message simply disappears
            return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban {User}", targetIdentity.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain($"❌ Failed to ban user: {ex.Message}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }

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
