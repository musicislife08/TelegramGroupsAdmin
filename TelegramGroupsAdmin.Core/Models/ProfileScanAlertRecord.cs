namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// UI model for profile scan alerts.
/// Created when a profile scan score exceeds the notify threshold.
/// </summary>
public record ProfileScanAlertRecord
{
    public long Id { get; init; }
    public required UserIdentity User { get; init; }
    public required ChatIdentity Chat { get; init; }

    /// <summary>Computed risk score (0.0-5.0)</summary>
    public decimal Score { get; init; }

    /// <summary>Outcome of the scan (Clean, HeldForReview, Banned)</summary>
    public ProfileScanOutcome Outcome { get; init; }

    /// <summary>AI-provided reason for the score</summary>
    public string? AiReason { get; init; }

    /// <summary>AI-detected signals (e.g., "explicit profile photo", "spam bio")</summary>
    public string[]? AiSignalsDetected { get; init; }

    /// <summary>User's bio text at scan time</summary>
    public string? Bio { get; init; }

    /// <summary>Personal channel title (if any)</summary>
    public string? PersonalChannelTitle { get; init; }

    /// <summary>Whether user has pinned stories</summary>
    public bool HasPinnedStories { get; init; }

    /// <summary>Telegram-flagged scam account</summary>
    public bool IsScam { get; init; }

    /// <summary>Telegram-flagged fake account</summary>
    public bool IsFake { get; init; }

    // Review workflow
    public DateTimeOffset DetectedAt { get; init; }
    public string? ReviewedByUserId { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewedByEmail { get; init; }
    public string? ActionTaken { get; init; }
}
