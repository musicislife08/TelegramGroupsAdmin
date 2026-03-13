namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record TempBanIntent : ModerationIntent
{
    public int? MessageId { get; init; }
    public required TimeSpan Duration { get; init; }
}
