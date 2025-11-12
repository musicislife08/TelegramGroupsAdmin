using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.Extensions;

/// <summary>
/// Extension methods for working with enums
/// </summary>
public static class EnumExtensions
{
    extension(Enum enumValue)
    {
        /// <summary>
        /// Gets the Display(Name) attribute value for an enum value.
        /// Falls back to ToString() if no Display attribute is found.
        /// </summary>
        /// <returns>Display name or enum value as string</returns>
        public string GetDisplayName()
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
}
