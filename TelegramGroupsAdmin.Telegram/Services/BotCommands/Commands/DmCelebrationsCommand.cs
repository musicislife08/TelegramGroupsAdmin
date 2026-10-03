using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /dmcelebrations on|off — opt in or out of receiving this chat's ban celebrations by DM.
/// Group-only: posting in the group is what proves membership. Replies go to the user's DMs;
/// the group only sees a post (mentioning them) when they can't be DMed.
/// </summary>
public sealed class DmCelebrationsCommand(
    IBanCelebrationSubscriptionService subscriptionService,
    IBotDmService dmService) : IBotCommand
{
    public string Name => CommandNames.DmCelebrations;
    public string Description => "Get this chat's ban celebrations in your DMs (on/off)";
    public string Usage => "/dmcelebrations on|off";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member;
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => true;
    public int? DeleteResponseAfterSeconds => 30;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        if (message.From is null)
        {
            return Reply(TelegramMessage.Empty);
        }

        // Anonymous admins and channels posting as the chat carry a placeholder From, not a person.
        if (message.SenderChat is not null)
        {
            return Reply(TelegramMessage.Plain("Send this from your own account, not as the group or a channel."));
        }

        if (message.Chat.Type == ChatType.Private)
        {
            return Reply(TelegramMessage.Plain("Run /dmcelebrations on in the group you want celebrations from."));
        }

        var chat = ChatIdentity.From(message.Chat);
        var user = UserIdentity.From(message.From);
        var chatName = chat.ChatName ?? "this chat";

        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case "on":
                // The service DMs the confirmation, or posts the start prompt when it can't;
                // a banned user gets nothing. Either way the group sees no reply from here.
                await subscriptionService.SubscribeAsync(chat, user, cancellationToken);
                return Reply(TelegramMessage.Empty);

            case "off":
                await subscriptionService.UnsubscribeAsync(chat, user, cancellationToken);
                return await ReplyByDmAsync(user, chat,
                    $"🔕 You won't get {chatName}'s ban celebrations in your DMs anymore.", cancellationToken);

            default:
                var subscribed = await subscriptionService.IsSubscribedAsync(chat.Id, user.Id, cancellationToken);
                return await ReplyByDmAsync(user, chat, subscribed
                    ? $"✅ You're getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations off to stop."
                    : $"You're not getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations on to start.",
                    cancellationToken);
        }
    }

    /// <summary>DMs the reply; only if the DM fails does it post in the group, self-deleting like a normal reply.</summary>
    private async Task<CommandResult> ReplyByDmAsync(UserIdentity user, ChatIdentity chat, string text, CancellationToken ct)
    {
        await dmService.SendDmAsync(user, TelegramMessage.Plain(text), chat.Id, DeleteResponseAfterSeconds, ct);
        return Reply(TelegramMessage.Empty);
    }

    private CommandResult Reply(TelegramMessage message) =>
        new(message, DeleteCommandMessage, DeleteResponseAfterSeconds);
}
