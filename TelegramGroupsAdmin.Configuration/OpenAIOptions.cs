namespace TelegramGroupsAdmin.Configuration;

public sealed class OpenAIOptions
{
    public required string ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 500;
}
