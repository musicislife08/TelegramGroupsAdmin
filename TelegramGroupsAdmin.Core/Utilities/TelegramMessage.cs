using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Rendered Telegram message — plain text plus explicit entities.
/// Sent with no parse_mode; Telegram renders exactly what the entities specify.
/// </summary>
public sealed record TelegramMessage(
    string Text,
    IReadOnlyList<MessageEntity> Entities)
{
    /// <summary>A message with no formatting and no entities.</summary>
    public static TelegramMessage Plain(string text) =>
        new(text, []);

    /// <summary>An empty message — no text, no entities. Use as an empty/sentinel result.</summary>
    public static TelegramMessage Empty { get; } = new(string.Empty, []);
}
