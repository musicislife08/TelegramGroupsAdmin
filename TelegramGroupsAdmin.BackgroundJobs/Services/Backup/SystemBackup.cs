using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

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
