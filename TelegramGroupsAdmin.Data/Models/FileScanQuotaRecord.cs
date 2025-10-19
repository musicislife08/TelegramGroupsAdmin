using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for file_scan_quota table - tracks quota usage for cloud scanning services
/// Supports both calendar-based resets (midnight UTC) and rolling 24-hour windows
/// </summary>
[Table("file_scan_quota")]
public class FileScanQuotaRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("service")]
    [Required]
    [MaxLength(50)]  // 'VirusTotal', 'MetaDefender', 'HybridAnalysis', 'Intezer'
    public string Service { get; set; } = string.Empty;

    [Column("quota_type")]
    [Required]
    [MaxLength(10)]  // 'daily' or 'monthly'
    public string QuotaType { get; set; } = string.Empty;

    [Column("quota_window_start")]
    public DateTimeOffset QuotaWindowStart { get; set; }  // When this quota window started

    [Column("quota_window_end")]
    public DateTimeOffset QuotaWindowEnd { get; set; }  // When this quota window expires

    [Column("count")]
    public int Count { get; set; } = 0;

    [Column("limit_value")]
    public int LimitValue { get; set; }  // Quota limit (500 for VT daily, 40 for MetaDefender, etc.)

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }
}
