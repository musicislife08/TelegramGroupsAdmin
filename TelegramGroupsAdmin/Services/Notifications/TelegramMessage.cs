using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Rendered Telegram message — plain text plus explicit entities.
/// Sent with no parse_mode; Telegram renders exactly what the entities specify.
/// </summary>
internal sealed record TelegramMessage(
    string Text,
    IReadOnlyList<MessageEntity> Entities);
