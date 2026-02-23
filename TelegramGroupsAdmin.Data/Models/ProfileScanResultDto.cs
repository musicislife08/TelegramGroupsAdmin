using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for profile_scan_results table.
/// Each row is one profile scan event with full scoring detail.
/// </summary>
[Table("profile_scan_results")]
public class ProfileScanResultDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("scanned_at")]
    public DateTimeOffset ScannedAt { get; set; }

    /// <summary>Total capped score (0.0-5.0)</summary>
    [Column("score")]
    public decimal Score { get; set; }

    /// <summary>0=Clean, 1=HeldForReview, 2=Banned</summary>
    [Column("outcome")]
    public int Outcome { get; set; }

    /// <summary>Layer 1 rule-based score contribution</summary>
    [Column("rule_score")]
    public decimal RuleScore { get; set; }

    /// <summary>Layer 2 AI score contribution</summary>
    [Column("ai_score")]
    public decimal AiScore { get; set; }

    /// <summary>AI confidence 0-100, null if AI was skipped</summary>
    [Column("ai_confidence")]
    public int? AiConfidence { get; set; }

    /// <summary>AI's explanation of its decision</summary>
    [Column("ai_reason")]
    public string? AiReason { get; set; }

    /// <summary>Comma-separated list of detected signals</summary>
    [Column("ai_signals")]
    public string? AiSignals { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public virtual TelegramUserDto? User { get; set; }
}
