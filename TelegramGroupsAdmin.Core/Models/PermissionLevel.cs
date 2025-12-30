using System.ComponentModel.DataAnnotations;
using NetEscapades.EnumGenerators;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// User permission level hierarchy (core domain concept for application authorization)
/// </summary>
[EnumExtensions]
public enum PermissionLevel
{
    /// <summary>Chat-scoped moderation - Can view/moderate only chats they're Telegram admin in, read-only global settings, edit per-chat overrides</summary>
    [Display(Name = "Admin")]
    Admin = 0,

    /// <summary>Global moderation - Can view/moderate all chats, edit content settings, read-only infrastructure settings, manage Admin/GlobalAdmin users</summary>
    [Display(Name = "GlobalAdmin")]
    GlobalAdmin = 1,

    /// <summary>Full system access - Complete control over all settings, infrastructure, and user management</summary>
    [Display(Name = "Owner")]
    Owner = 2
}
