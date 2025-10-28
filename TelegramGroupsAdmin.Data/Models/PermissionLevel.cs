namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User permission level hierarchy (stored as INT in database)
/// </summary>
public enum PermissionLevel
{
    /// <summary>Chat-scoped moderation - Can view/moderate only chats they're Telegram admin in, read-only global settings, edit per-chat overrides</summary>
    Admin = 0,

    /// <summary>Global moderation - Can view/moderate all chats, edit content settings, read-only infrastructure settings, manage Admin/GlobalAdmin users</summary>
    GlobalAdmin = 1,

    /// <summary>Full system access - Complete control over all settings, infrastructure, and user management</summary>
    Owner = 2
}
