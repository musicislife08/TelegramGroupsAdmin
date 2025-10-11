using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Services.Backup;

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

/// <summary>
/// Root backup container - uses reflection to discover and serialize DTOs
/// Dictionary key = table name, value = list of DTO objects
/// </summary>
public class SystemBackup
{
    [JsonPropertyName("metadata")]
    public BackupMetadata Metadata { get; set; } = new();

    [JsonPropertyName("data")]
    public Dictionary<string, List<object>> Data { get; set; } = new();
}
