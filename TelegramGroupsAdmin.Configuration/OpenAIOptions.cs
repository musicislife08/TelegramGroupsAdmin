namespace TelegramGroupsAdmin.Configuration;

public sealed record OpenAIOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "gpt-4o-mini";
    public int MaxTokens { get; init; } = 500;
}
