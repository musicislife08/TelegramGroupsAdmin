using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /dmcelebrations on|off — opt in or out of receiving this chat's ban celebrations by DM.
/// Group-only: posting in the group is what proves membership.
/// </summary>
public sealed class DmCelebrationsCommand(IBanCelebrationSubscriptionService subscriptionService) : IBotCommand
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
                var result = await subscriptionService.SubscribeAsync(chat, user, cancellationToken);
                // AwaitingStart: the start prompt (with its button) is already posted and self-cleans.
                return Reply(result == DmCelebrationSubscribeResult.Subscribed
                    ? TelegramMessage.Plain($"✅ You'll get {chatName}'s ban celebrations in your DMs.")
                    : TelegramMessage.Empty);

            case "off":
                await subscriptionService.UnsubscribeAsync(chat, user, cancellationToken);
                return Reply(TelegramMessage.Plain($"🔕 You won't get {chatName}'s ban celebrations in your DMs anymore."));

            default:
                var subscribed = await subscriptionService.IsSubscribedAsync(chat.Id, user.Id, cancellationToken);
                return Reply(TelegramMessage.Plain(subscribed
                    ? $"✅ You're getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations off to stop."
                    : $"You're not getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations on to start."));
        }
    }

    private CommandResult Reply(TelegramMessage message) =>
        new(message, DeleteCommandMessage, DeleteResponseAfterSeconds);
}
