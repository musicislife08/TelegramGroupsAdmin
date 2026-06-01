using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
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
            return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var telegramUserId = message.From.Id;

        // If command was sent in a group chat, send DM instead (privacy-first)
        if (message.Chat.Type != ChatType.Private)
        {
            // Send status via DM notification system with the full entity payload
            // so bold formatting renders in the DM.
            var statusMessage = await BuildStatusMessageAsync(telegramUserId, cancellationToken);
            var notification = new Notification("mystatus", statusMessage.Text, statusMessage);

            var result = await _notificationOrchestrator.SendTelegramDmAsync(
                telegramUserId,
                notification,
                cancellationToken);

            if (result.Success)
            {
                // Silently delete command message, DM sent successfully
                return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
            }
            else
            {
                // DM failed (queued), inform user in chat
                var botInfo = await _userService.GetMeAsync(cancellationToken);
                var deepLink = $"https://t.me/{botInfo.Username}?start=mystatus";

                return new CommandResult(
                    TelegramMessage.Plain(
                        $"📬 I've queued your status information for you. Please start a conversation with me to receive it privately: {deepLink}"),
                    DeleteCommandMessage,
                    30);
            }
        }

        // Command was sent in private DM - respond directly
        var statusText = await BuildStatusMessageAsync(telegramUserId, cancellationToken);
        return new CommandResult(statusText, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    private async Task<TelegramMessage> BuildStatusMessageAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        // Get user info
        var telegramUser = await _telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        if (telegramUser == null)
        {
            return TelegramMessage.Plain("❌ User record not found. You may need to interact with the bot in a managed chat first.");
        }

        // Get active warnings (not expired)
        var activeWarnings = await _userActionsRepository.GetActiveActionsAsync(
            telegramUserId,
            UserActionType.Warn,
            cancellationToken);

        var warningCount = activeWarnings.Count;

        // Build status message with explicit entities (bold via builder, no parse mode)
        var builder = new TelegramMessageBuilder()
            .Text("📊 ").Bold("Your Status").LineBreak()
            .LineBreak();

        // Trust status
        if (telegramUser.IsTrusted)
        {
            builder.Text("✅ ").Bold("Trusted User").Text(" - Your messages skip spam detection").LineBreak();
        }
        else
        {
            builder.Text("👤 ").Bold("Regular User").Text(" - Your messages are checked for spam").LineBreak();
        }

        builder.LineBreak();

        // Warning status
        if (warningCount == 0)
        {
            builder.Text("🎉 ").Bold("No Active Warnings").Text(" - You're in good standing!").LineBreak();
        }
        else
        {
            builder.Text("⚠️ ").Bold("Active Warnings:").Text($" {warningCount}").LineBreak()
                .LineBreak()
                .Bold("Recent Warnings:").LineBreak();

            foreach (var warning in activeWarnings.Take(5).OrderByDescending(w => w.IssuedAt))
            {
                var daysAgo = (DateTimeOffset.UtcNow - warning.IssuedAt).Days;
                var timeAgo = daysAgo == 0 ? "today" : $"{daysAgo} day{(daysAgo > 1 ? "s" : "")} ago";
                builder.Text($"  • {warning.Reason} ({timeAgo})").LineBreak();
            }

            if (activeWarnings.Count > 5)
            {
                builder.Text($"  ... and {activeWarnings.Count - 5} more").LineBreak();
            }
        }

        builder.LineBreak()
            .Bold("Account Created:").Text($" {telegramUser.FirstSeenAt:MMM d, yyyy}").LineBreak()
            .Bold("Last Active:").Text($" {telegramUser.LastSeenAt:MMM d, yyyy}").LineBreak();

        if (telegramUser.BotDmEnabled)
        {
            builder.LineBreak()
                .Text("✉️ DM notifications are ").Bold("enabled").LineBreak();
        }

        return builder.Build();
    }
}
