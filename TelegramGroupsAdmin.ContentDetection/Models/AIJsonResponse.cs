namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Expected JSON response structure from AI for spam detection.
/// This is the format we request in our prompts.
/// </summary>
public record AIJsonResponse
{
    public string? Result { get; init; } // "spam", "clean", or "review"
    public string? Reason { get; init; }
    public double? Score { get; init; }
}
