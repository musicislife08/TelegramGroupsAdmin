using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Builds the user-facing ban notification as an entity-based <see cref="TelegramMessage"/>
/// (no parse_mode). Shared by the /ban command and the ban-selection callback so the two paths
/// can never drift apart.
/// </summary>
public static class BanNotificationMessage
{
    public static TelegramMessage Build(string chatName, string reason, int chatsAffected) =>
        new TelegramMessageBuilder()
            .Text("🚫 ").Bold("You have been banned").LineBreak().LineBreak()
            .Bold("Chat: ").Text(chatName).LineBreak()
            .Bold("Reason: ").Text(reason).LineBreak()
            .Bold("Chats affected: ").Text(chatsAffected.ToString()).LineBreak().LineBreak()
            .Text("If you believe this was a mistake, you may appeal by contacting the chat administrators.")
            .Build();
}
