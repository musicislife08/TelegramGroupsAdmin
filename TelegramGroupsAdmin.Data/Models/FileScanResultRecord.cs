using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for file_scan_results table - stores file scanning results for caching and auditing
/// </summary>
[Table("file_scan_results")]
public class FileScanResultRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("file_hash")]
    [Required]
    [MaxLength(64)]  // SHA256 hash is 64 hex characters
    public string FileHash { get; set; } = string.Empty;

    [Column("scanner")]
    [Required]
    [MaxLength(50)]  // 'ClamAV', 'YARA', 'WindowsAMSI', 'VirusTotal', etc.
    public string Scanner { get; set; } = string.Empty;

    [Column("result")]
    [Required]
    [MaxLength(20)]  // 'clean', 'infected', 'suspicious', 'error'
    public string Result { get; set; } = string.Empty;

    [Column("threat_name")]
    [MaxLength(255)]
    public string? ThreatName { get; set; }

    [Column("scan_duration_ms")]
    public int? ScanDurationMs { get; set; }

    [Column("scanned_at")]
    public DateTimeOffset ScannedAt { get; set; }

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }  // YARA rule matched, VT engine breakdown, etc.
}
