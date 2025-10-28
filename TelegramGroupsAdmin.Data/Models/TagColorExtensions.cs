namespace TelegramGroupsAdmin.Data.Models;

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
