namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Data-availability counts for stop word recommendation generation.
/// </summary>
public sealed record StopWordCorpusCounts(int SpamSamples, int LegitMessages, int ScanResults);
