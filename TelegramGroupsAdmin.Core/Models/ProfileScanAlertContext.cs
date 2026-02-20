using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// JSONB context stored in reports.context for ProfileScanAlert reports.
/// </summary>
public record ProfileScanAlertContext
{
    [JsonPropertyName("userId")]
    public long UserId { get; init; }

    [JsonPropertyName("score")]
    public decimal Score { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("aiReason")]
    public string? AiReason { get; init; }

    [JsonPropertyName("aiSignals")]
    public string[]? AiSignals { get; init; }

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }

    [JsonPropertyName("personalChannelTitle")]
    public string? PersonalChannelTitle { get; init; }

    [JsonPropertyName("hasPinnedStories")]
    public bool HasPinnedStories { get; init; }

    [JsonPropertyName("isScam")]
    public bool IsScam { get; init; }

    [JsonPropertyName("isFake")]
    public bool IsFake { get; init; }
}
