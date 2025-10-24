using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.Extensions;

/// <summary>
/// Extension methods for working with enums
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Gets the Display(Name) attribute value for an enum value.
    /// Falls back to ToString() if no Display attribute is found.
    /// </summary>
    /// <param name="enumValue">The enum value</param>
    /// <returns>Display name or enum value as string</returns>
    public static string GetDisplayName(this Enum enumValue)
    {
        var field = enumValue.GetType().GetField(enumValue.ToString());
        if (field == null)
        {
            return enumValue.ToString();
        }

        var displayAttribute = field.GetCustomAttribute<DisplayAttribute>();
        return displayAttribute?.Name ?? enumValue.ToString();
    }
}
