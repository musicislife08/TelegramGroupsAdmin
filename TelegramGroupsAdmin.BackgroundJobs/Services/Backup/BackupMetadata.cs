using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Backup metadata for version checking and compatibility
/// </summary>
public class BackupMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("table_count")]
    public int TableCount { get; set; }

    [JsonPropertyName("tables")]
    public List<string> Tables { get; set; } = [];
}
