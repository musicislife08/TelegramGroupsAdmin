namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Business model for unified configuration
/// Maps from ConfigRecordDto in Data layer
/// </summary>
public class ConfigRecord
{
    public long Id { get; set; }
    public long? ChatId { get; set; }
    public string? SpamDetectionConfig { get; set; }
    public string? WelcomeConfig { get; set; }
    public string? LogConfig { get; set; }
    public string? ModerationConfig { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
