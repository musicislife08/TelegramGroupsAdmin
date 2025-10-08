namespace TelegramGroupsAdmin.Configuration;

public sealed record TelegramOptions
{
    public required string HistoryBotToken { get; init; }
    public required string ChatId { get; init; }
}
