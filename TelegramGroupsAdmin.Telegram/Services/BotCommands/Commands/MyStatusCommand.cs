using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /mystatus - Show user's warning count, trust status, and other personal information
/// DM-only command for privacy
/// </summary>
public class MyStatusCommand : IBotCommand
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly IBotUserService _userService;

    public MyStatusCommand(
        ITelegramUserRepository telegramUserRepository,
        IUserActionsRepository userActionsRepository,
        INotificationOrchestrator notificationOrchestrator,
        IBotUserService userService)
    {
        _telegramUserRepository = telegramUserRepository;
        _userActionsRepository = userActionsRepository;
        _notificationOrchestrator = notificationOrchestrator;
        _userService = userService;
    }

    public string Name => "mystatus";
    public string Description => "Check your warning count, trust status, and other info (DM only)";
    public string Usage => "/mystatus";
    public int MinPermissionLevel => 0; // Everyone can use
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => true; // Delete command for privacy
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.From == null)
        {
            return new CommandResult(string.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var telegramUserId = message.From.Id;

        // If command was sent in a group chat, send DM instead (privacy-first)
        if (message.Chat.Type != ChatType.Private)
        {
            // Send status via DM notification system
            var statusMessage = await BuildStatusMessageAsync(telegramUserId, cancellationToken);
            var notification = new Notification("mystatus", statusMessage);

            var result = await _notificationOrchestrator.SendTelegramDmAsync(
                telegramUserId,
                notification,
                cancellationToken);

            if (result.Success)
            {
                // Silently delete command message, DM sent successfully
                return new CommandResult(string.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
            }
            else
            {
                // DM failed (queued), inform user in chat
                var botInfo = await _userService.GetMeAsync(cancellationToken);
                var deepLink = $"https://t.me/{botInfo.Username}?start=mystatus";

                return new CommandResult(
                    $"📬 I've queued your status information for you. Please start a conversation with me to receive it privately: {deepLink}",
                    DeleteCommandMessage,
                    30);
            }
        }

        // Command was sent in private DM - respond directly
        var statusText = await BuildStatusMessageAsync(telegramUserId, cancellationToken);
        return new CommandResult(statusText, DeleteCommandMessage, DeleteResponseAfterSeconds, ParseMode.Html);
    }

    private async Task<string> BuildStatusMessageAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        // Get user info
        var telegramUser = await _telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        if (telegramUser == null)
        {
            return "❌ User record not found. You may need to interact with the bot in a managed chat first.";
        }

        // Get active warnings (not expired)
        var activeWarnings = await _userActionsRepository.GetActiveActionsAsync(
            telegramUserId,
            UserActionType.Warn,
            cancellationToken);

        var warningCount = activeWarnings.Count;

        // Build status message (HTML format for Telegram Html parse mode)
        var statusLines = new List<string>
        {
            "📊 <b>Your Status</b>",
            ""
        };

        // Trust status
        if (telegramUser.IsTrusted)
        {
            statusLines.Add("✅ <b>Trusted User</b> - Your messages skip spam detection");
        }
        else
        {
            statusLines.Add("👤 <b>Regular User</b> - Your messages are checked for spam");
        }

        statusLines.Add("");

        // Warning status
        if (warningCount == 0)
        {
            statusLines.Add("🎉 <b>No Active Warnings</b> - You're in good standing!");
        }
        else
        {
            statusLines.Add($"⚠️ <b>Active Warnings:</b> {warningCount}");
            statusLines.Add("");
            statusLines.Add("<b>Recent Warnings:</b>");

            foreach (var warning in activeWarnings.Take(5).OrderByDescending(w => w.IssuedAt))
            {
                var daysAgo = (DateTimeOffset.UtcNow - warning.IssuedAt).Days;
                var timeAgo = daysAgo == 0 ? "today" : $"{daysAgo} day{(daysAgo > 1 ? "s" : "")} ago";
                statusLines.Add($"  • {System.Net.WebUtility.HtmlEncode(warning.Reason)} ({timeAgo})");
            }

            if (activeWarnings.Count > 5)
            {
                statusLines.Add($"  ... and {activeWarnings.Count - 5} more");
            }
        }

        statusLines.Add("");
        statusLines.Add($"<b>Account Created:</b> {telegramUser.FirstSeenAt:MMM d, yyyy}");
        statusLines.Add($"<b>Last Active:</b> {telegramUser.LastSeenAt:MMM d, yyyy}");

        if (telegramUser.BotDmEnabled)
        {
            statusLines.Add("");
            statusLines.Add("✉️ DM notifications are <b>enabled</b>");
        }

        return string.Join("\n", statusLines);
    }
}
