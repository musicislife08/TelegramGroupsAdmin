namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// UI/Domain model for file scan quota tracking
/// Maps to file_scan_quota table
/// </summary>
public record FileScanQuotaModel(
    long Id,
    string Service,
    string QuotaType,
    DateTimeOffset QuotaWindowStart,
    DateTimeOffset QuotaWindowEnd,
    int Count,
    int LimitValue,
    DateTimeOffset LastUpdated
)
{
    /// <summary>
    /// Percentage of the quota consumed; can exceed 100 when the count overshoots the limit
    /// </summary>
    public double PercentageUsed => LimitValue > 0 ? (double)Count / LimitValue * 100 : 0;
}
