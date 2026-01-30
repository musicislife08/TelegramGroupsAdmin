using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Core.Models;

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

    /// <summary>
    /// Shuffle state for answer randomization.
    /// Format: {"0": [3,2,1,0], "1": [0,1,2,3]} (question index → shuffled answer indices)
    /// </summary>
    [JsonPropertyName("shuffleState")]
    public Dictionary<int, int[]>? ShuffleState { get; init; }

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