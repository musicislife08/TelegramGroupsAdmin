namespace TelegramGroupsAdmin.Configuration;

public sealed record TelegramOptions
{
    public required string BotToken { get; init; }
    public required string ChatId { get; init; }
}
