using System.Text.Json;

namespace TelegramGroupsAdmin.AI.Services;

/// <summary>
/// Shared System.Text.Json options for parsing AI provider responses
/// (model lists, translation results). Provider payloads use varying casing,
/// so property matching is case-insensitive.
/// </summary>
internal static class AIJsonDefaults
{
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
