namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// MudBlazor color options for tag display
/// </summary>
public enum TagColor
{
    Primary = 0,      // Blue
    Secondary = 1,    // Purple
    Info = 2,         // Light Blue
    Success = 3,      // Green
    Warning = 4,      // Orange
    Error = 5,        // Red
    Dark = 6          // Dark Gray
}

/// <summary>
/// Extension methods for TagColor enum
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
