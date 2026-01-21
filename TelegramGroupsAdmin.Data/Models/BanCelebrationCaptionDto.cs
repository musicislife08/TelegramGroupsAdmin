using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for ban_celebration_captions table
/// Stores caption templates used for ban celebration messages
/// Supports placeholders: {username}, {chatname}, {bancount}
/// </summary>
[Table("ban_celebration_captions")]
public class BanCelebrationCaptionDto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Caption text for chat messages (uses {username} placeholder)
    /// Example: "FATALITY! {username} has been finished!"
    /// </summary>
    [Column("text")]
    [Required]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Caption text for DM messages to banned user (uses "You" grammar)
    /// Example: "You have been finished!"
    /// </summary>
    [Column("dm_text")]
    [Required]
    public string DmText { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the caption in the UI
    /// Example: "Mortal Kombat - Fatality"
    /// </summary>
    [Column("name")]
    [MaxLength(100)]
    public string? Name { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
