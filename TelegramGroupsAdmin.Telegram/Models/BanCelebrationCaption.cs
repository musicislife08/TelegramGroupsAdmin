namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for ban celebration caption templates
/// Supports placeholders: {username}, {chatname}, {bancount}
/// </summary>
public class BanCelebrationCaption
{
    public int Id { get; set; }

    /// <summary>
    /// Caption text for chat messages (uses {username} placeholder)
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Caption text for DM messages to banned user (uses "You" grammar)
    /// </summary>
    public string DmText { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the caption
    /// </summary>
    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
