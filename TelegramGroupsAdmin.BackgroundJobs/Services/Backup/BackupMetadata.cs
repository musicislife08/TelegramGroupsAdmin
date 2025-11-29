using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Backup metadata for version checking and compatibility
/// </summary>
public class BackupMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0";

    /// <summary>
    /// Timestamp when the backup was created. Stored as ISO 8601 string in JSON.
    /// Backward compatible with legacy Unix timestamp format (long) via BackupMetadataDateTimeConverter.
    /// </summary>
    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(BackupMetadataDateTimeConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("table_count")]
    public int TableCount { get; set; }

    [JsonPropertyName("tables")]
    public List<string> Tables { get; set; } = [];
}
