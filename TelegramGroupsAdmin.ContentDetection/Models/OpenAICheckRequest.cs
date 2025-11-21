namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for OpenAI spam check
/// </summary>
public sealed class OpenAICheckRequest : ContentCheckRequestBase
{
    public required bool VetoMode { get; init; }
    public required string? SystemPrompt { get; init; }
    public required bool HasSpamFlags { get; init; }
    public required int MinMessageLength { get; init; }
    public required bool CheckShortMessages { get; init; }
    public required int MessageHistoryCount { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
}
