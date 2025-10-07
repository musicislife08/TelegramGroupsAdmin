namespace TelegramGroupsAdmin.Configuration;

public class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 500;
}
