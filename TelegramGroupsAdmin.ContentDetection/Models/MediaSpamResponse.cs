using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Shared JSON response model for AI-based media spam detection (image and video).
/// </summary>
internal record MediaSpamResponse(
    [property: JsonPropertyName("spam")] bool Spam,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("patterns_detected")] string[]? PatternsDetected
);
