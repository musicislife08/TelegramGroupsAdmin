namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Identifies which media-style DM send method is running so the shared helper can preserve
/// the original per-method error log template (structured log consumers see the same template
/// string they did before the helper was extracted).
/// </summary>
internal enum DmMediaLogVariant
{
    EntitiesMediaKeyboard
}
