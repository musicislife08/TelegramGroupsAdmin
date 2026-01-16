using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.ContentDetection.Models;

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

/// <summary>
/// Context for ExamFailure reviews stored in JSONB.
/// Contains exam results and configuration details.
/// </summary>
public record ExamFailureContext
{
    [JsonPropertyName("userId")]
    public long UserId { get; init; }

    /// <summary>
    /// User's multiple choice answers.
    /// Format: {"0": "B", "1": "A", "2": "C"} (question index → selected answer)
    /// </summary>
    [JsonPropertyName("mcAnswers")]
    public Dictionary<int, string>? McAnswers { get; init; }

    [JsonPropertyName("openEndedAnswer")]
    public string? OpenEndedAnswer { get; init; }

    /// <summary>
    /// Percentage score achieved (0-100).
    /// </summary>
    [JsonPropertyName("score")]
    public int Score { get; init; }

    /// <summary>
    /// Passing threshold that was configured (0-100).
    /// </summary>
    [JsonPropertyName("passingThreshold")]
    public int PassingThreshold { get; init; }

    /// <summary>
    /// AI evaluation result for open-ended question (if applicable).
    /// </summary>
    [JsonPropertyName("aiEvaluation")]
    public string? AiEvaluation { get; init; }
}

/// <summary>
/// Context for Report reviews stored in JSONB (optional extra metadata).
/// Most report data is in the base columns, this is for extended info.
/// </summary>
public record ReportContext
{
    /// <summary>
    /// Source of the report: telegram, web, or system (OpenAI veto).
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>
    /// Original message text (cached for display even if message deleted).
    /// </summary>
    [JsonPropertyName("messageText")]
    public string? MessageText { get; init; }

    /// <summary>
    /// Media type if the reported message contained media.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; init; }
}
