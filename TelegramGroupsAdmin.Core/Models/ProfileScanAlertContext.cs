using System.Text.Json.Serialization;
using TelegramGroupsAdmin.Core.Utilities;

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
    public int Outcome { get; init; }

    [JsonPropertyName("aiReason")]
    public string? AiReason { get; init; }

    /// <summary>
    /// Rows written before the context carried an array store this as one comma-separated string;
    /// both shapes read, the array shape is written.
    /// </summary>
    [JsonPropertyName("aiSignals")]
    [JsonConverter(typeof(CommaSeparatedOrArrayJsonConverter))]
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
