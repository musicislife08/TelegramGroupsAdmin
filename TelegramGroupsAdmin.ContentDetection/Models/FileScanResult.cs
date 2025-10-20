namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// File scan result domain model (public API)
/// Represents a cached scan result for a specific file hash
/// </summary>
public record FileScanResultModel(
    long Id,
    string FileHash,
    string Scanner,
    string Result,
    string? ThreatName,
    int? ScanDurationMs,
    DateTimeOffset ScannedAt,
    string? MetadataJson
);
