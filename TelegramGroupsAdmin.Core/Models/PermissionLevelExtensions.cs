using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Single source of truth for the human-readable name of a <see cref="PermissionLevel"/>.
/// Replaces the previously duplicated GetPermissionName / GetRoleName mappers.
/// </summary>
public static class PermissionLevelExtensions
{
    /// <summary>
    /// Returns the <see cref="DisplayAttribute.Name"/> for the given tier, falling back to the enum name.
    /// An undefined value fails closed to <see cref="PermissionLevel.Member"/> (the no-privilege floor)
    /// instead of emitting a raw int, so the role-claim path can never mint a privileged role for an
    /// unrecognized tier (e.g. a future higher value).
    /// </summary>
    public static string GetDisplayName(this PermissionLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            level = PermissionLevel.Member;
        }

        var member = typeof(PermissionLevel).GetMember(level.ToString()).FirstOrDefault();
        var display = member?.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? level.ToString();
    }
}
