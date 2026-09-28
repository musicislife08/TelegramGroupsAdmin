namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// A single ContentScan detection_results row, projected for stop word removal analysis.
/// </summary>
public sealed record ScanCheckResults(long Id, bool IsSpam, string CheckResultsJson, DateTimeOffset DetectedAt);
