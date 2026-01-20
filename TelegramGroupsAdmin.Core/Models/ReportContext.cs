using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Core.Models;

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
