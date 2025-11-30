using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for notification_preferences table
/// Simplified schema: stores channel×event matrix as single JSONB config
/// </summary>
[Table("notification_preferences")]
public record NotificationPreferencesDto
{
    [Key]
    [Column("id")]
    public long Id { get; init; }

    /// <summary>
    /// User ID (references users table)
    /// </summary>
    [Column("user_id")]
    [MaxLength(450)]
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Channel×Event configuration as JSONB
    /// Stores raw integers for enums (NotificationChannel, NotificationEventType)
    /// Structure: { "channels": [{ "channel": 0, "enabledEvents": [0, 3], "digestMinutes": 0 }] }
    /// </summary>
    [Column("config", TypeName = "jsonb")]
    public string Config { get; init; } = """{"channels":[]}""";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
