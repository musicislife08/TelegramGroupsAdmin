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
    /// Check if this quota window is still valid (not expired)
    /// </summary>
    public bool IsValid => QuotaWindowEnd > DateTimeOffset.UtcNow;

    /// <summary>
    /// Check if quota is available (count &lt; limit)
    /// </summary>
    public bool IsAvailable => Count < LimitValue;

    /// <summary>
    /// Percentage of quota consumed (0-100)
    /// </summary>
    public int PercentageUsed => LimitValue > 0 ? (Count * 100) / LimitValue : 0;

    /// <summary>
    /// Remaining quota
    /// </summary>
    public int Remaining => Math.Max(0, LimitValue - Count);
}
