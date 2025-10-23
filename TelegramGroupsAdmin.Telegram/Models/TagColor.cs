namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// MudBlazor color options for tag display in UI
/// Duplicated from Data.Models.TagColor to maintain architectural boundary.
/// </summary>
public enum TagColor
{
    /// <summary>Blue color for primary tags</summary>
    Primary = 0,
    /// <summary>Purple color for secondary tags</summary>
    Secondary = 1,
    /// <summary>Light blue color for informational tags</summary>
    Info = 2,
    /// <summary>Green color for success/positive tags</summary>
    Success = 3,
    /// <summary>Orange color for warning tags</summary>
    Warning = 4,
    /// <summary>Red color for error/critical tags</summary>
    Error = 5,
    /// <summary>Dark gray color for neutral tags</summary>
    Dark = 6
}

/// <summary>
/// Extension methods for TagColor enum
/// Note: ToMudColor() extension is in TelegramGroupsAdmin/Helpers/TagColorExtensions.cs
/// (can't be here since this project doesn't reference MudBlazor)
/// </summary>
public static class TagColorExtensions
{
    /// <summary>
    /// Get user-friendly display name for the color
    /// </summary>
    public static string GetDisplayName(this TagColor color) => color switch
    {
        TagColor.Primary => "Blue",
        TagColor.Secondary => "Purple",
        TagColor.Info => "Light Blue",
        TagColor.Success => "Green",
        TagColor.Warning => "Orange",
        TagColor.Error => "Red",
        TagColor.Dark => "Dark Gray",
        _ => "Blue"
    };
}
