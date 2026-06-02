using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Single source of truth for the human-readable name of a <see cref="PermissionLevel"/>.
/// Replaces the previously duplicated GetPermissionName / GetRoleName mappers.
/// </summary>
public static class PermissionLevelExtensions
{
    public static string GetDisplayName(this PermissionLevel level)
    {
        var member = typeof(PermissionLevel).GetMember(level.ToString()).FirstOrDefault();
        var display = member?.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? level.ToString();
    }
}
