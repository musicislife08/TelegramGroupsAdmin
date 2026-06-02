using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Resolves a user's effective permission tier in a specific chat from the two sources
/// of authority: their stored web tier (if any) and their Telegram admin status in that chat.
///
/// GlobalAdmin/Owner apply globally (any chat). Admin is chat-scoped — it applies only
/// where the user is a Telegram admin/creator. Everyone else is Member.
/// This naturally yields the MAX of the two sources.
/// </summary>
public static class PermissionResolver
{
    public static PermissionLevel Resolve(PermissionLevel? webTier, bool isChatAdminOrCreator)
        => webTier is PermissionLevel.GlobalAdmin or PermissionLevel.Owner
            ? webTier.Value
            : isChatAdminOrCreator
                ? PermissionLevel.Admin
                : PermissionLevel.Member;
}
