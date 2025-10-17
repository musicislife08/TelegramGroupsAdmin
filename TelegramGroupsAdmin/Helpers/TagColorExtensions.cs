using MudBlazor;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Helpers;

/// <summary>
/// Extension methods for TagColor enum to convert to MudBlazor Color
/// Centralizes the mapping logic for consistent color usage across the UI
/// </summary>
public static class TagColorExtensions
{
    /// <summary>
    /// Convert TagColor (database enum) to MudBlazor Color (UI enum)
    /// </summary>
    public static Color ToMudColor(this TagColor tagColor) => tagColor switch
    {
        TagColor.Primary => Color.Primary,
        TagColor.Secondary => Color.Secondary,
        TagColor.Info => Color.Info,
        TagColor.Success => Color.Success,
        TagColor.Warning => Color.Warning,
        TagColor.Error => Color.Error,
        TagColor.Dark => Color.Dark,
        _ => Color.Primary
    };
}
