namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Result of prompt building - contains system and user prompts for AI chat completion.
/// </summary>
public record AIPromptResult(string SystemPrompt, string UserPrompt);
