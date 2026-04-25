namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Log-message style for the shared DM send helper in <see cref="BotDmService"/>.
/// Queue-style matches the parse-mode/entities text methods (info success + warning on 403 with
/// "queueing" verbiage + network-aware error). Media-style matches the media/keyboard variants
/// (success logged internally by the sendAction + info on 403 with "blocked bot DMs" verbiage
/// + variant-specific error wording).
/// </summary>
internal enum DmLogStyle
{
    Queue,
    Media
}
