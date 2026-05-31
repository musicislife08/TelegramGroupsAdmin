using System.Text;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Builds a <see cref="TelegramMessage"/> (text + entities) with UTF-16 offset tracking.
/// Entities are Telegram's parse-mode-free formatting model: each records a type over an
/// offset/length range of the text. Offsets are UTF-16 code units (StringBuilder.Length),
/// matching Telegram's offset rule, so non-BMP characters (emoji) count as length 2.
/// </summary>
public sealed class TelegramMessageBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly List<MessageEntity> _entities = [];

    public TelegramMessageBuilder Text(string text) { _sb.Append(text); return this; }
    public TelegramMessageBuilder LineBreak() { _sb.Append('\n'); return this; }

    public TelegramMessageBuilder Bold(string text) => Styled(text, MessageEntityType.Bold);
    public TelegramMessageBuilder Italic(string text) => Styled(text, MessageEntityType.Italic);
    public TelegramMessageBuilder Code(string text) => Styled(text, MessageEntityType.Code);
    public TelegramMessageBuilder Pre(string text) => Styled(text, MessageEntityType.Pre);

    public TelegramMessageBuilder Link(string text, string url)
    {
        var offset = _sb.Length;
        _sb.Append(text);
        _entities.Add(new MessageEntity { Type = MessageEntityType.TextLink, Offset = offset, Length = text.Length, Url = url });
        return this;
    }

    /// <summary>
    /// Append a clickable mention of <paramref name="user"/>. Always emits a TextMention entity
    /// carrying the real User id, so it is clickable even for users without a username.
    /// Display text is the user's name (no @).
    /// </summary>
    public TelegramMessageBuilder Mention(UserIdentity user)
    {
        var displayText = TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, user.Id);
        var offset = _sb.Length;
        _sb.Append(displayText);
        _entities.Add(new MessageEntity
        {
            Type = MessageEntityType.TextMention,
            Offset = offset,
            Length = displayText.Length,
            User = new User { Id = user.Id, IsBot = false, FirstName = user.FirstName ?? string.Empty, LastName = user.LastName, Username = user.Username }
        });
        return this;
    }

    public TelegramMessage Build() => new(_sb.ToString(), [.. _entities]);

    private TelegramMessageBuilder Styled(string text, MessageEntityType type)
    {
        var offset = _sb.Length;
        _sb.Append(text);
        _entities.Add(new MessageEntity { Type = type, Offset = offset, Length = text.Length });
        return this;
    }
}
