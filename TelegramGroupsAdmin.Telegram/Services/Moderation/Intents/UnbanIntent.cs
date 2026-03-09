namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record UnbanIntent : ModerationIntent
{
    public bool RestoreTrust { get; init; }
}
