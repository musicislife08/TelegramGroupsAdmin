using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Context for ImpersonationAlert reviews stored in JSONB.
/// Contains all impersonation-specific fields not in the base review.
/// Format matches plan: { "targetUserId": 123, "nameMatch": true, "photoSimilarity": 0.85, "riskLevel": "high" }
/// </summary>
public record ImpersonationAlertContext
{
    /// <summary>
    /// The user suspected of impersonation (the potential scammer).
    /// </summary>
    [JsonPropertyName("suspectedUserId")]
    public long SuspectedUserId { get; init; }

    /// <summary>
    /// The admin/user being impersonated (the victim).
    /// </summary>
    [JsonPropertyName("targetUserId")]
    public long TargetUserId { get; init; }

    [JsonPropertyName("totalScore")]
    public int TotalScore { get; init; }

    /// <summary>
    /// Risk level stored as string for JSON readability ("low", "medium", "high", "critical").
    /// </summary>
    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "medium";

    [JsonPropertyName("nameMatch")]
    public bool NameMatch { get; init; }

    [JsonPropertyName("photoMatch")]
    public bool PhotoMatch { get; init; }

    [JsonPropertyName("photoSimilarity")]
    public double? PhotoSimilarity { get; init; }

    [JsonPropertyName("autoBanned")]
    public bool AutoBanned { get; init; }

    /// <summary>
    /// Verdict stored for historical record after review.
    /// </summary>
    [JsonPropertyName("verdict")]
    public string? Verdict { get; init; }
}