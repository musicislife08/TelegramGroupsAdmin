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

    /// <summary>
    /// Append an admin-authored template, substituting placeholder tokens via builder actions.
    /// Each entry in <paramref name="substitutions"/> maps a literal token (e.g. "{username}")
    /// to an action that appends its replacement (e.g. <c>b =&gt; b.Mention(user)</c>). Literal
    /// text between tokens is appended verbatim, and — critically — any token NOT present in the
    /// map is passed through as literal text (never dropped), so a mistyped placeholder renders
    /// visibly rather than vanishing. Tokens are matched left-to-right by earliest occurrence.
    /// </summary>
    public TelegramMessageBuilder AppendTemplate(
        string template,
        IReadOnlyDictionary<string, Action<TelegramMessageBuilder>> substitutions)
    {
        var remaining = template.AsSpan();
        while (!remaining.IsEmpty)
        {
            var bestIdx = -1;
            string? bestKey = null;
            foreach (var key in substitutions.Keys)
            {
                var idx = remaining.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) continue;
                if (bestIdx == -1 || idx < bestIdx || (idx == bestIdx && key.Length > bestKey!.Length))
                {
                    bestIdx = idx;
                    bestKey = key;
                }
            }

            // No known token remains — emit the rest verbatim (unknown tokens fall through here).
            if (bestIdx < 0)
            {
                Text(remaining.ToString());
                break;
            }

            if (bestIdx > 0)
                Text(remaining[..bestIdx].ToString());

            substitutions[bestKey!](this);
            remaining = remaining[(bestIdx + bestKey!.Length)..];
        }

        return this;
    }

    /// <summary>
    /// Append a pre-rendered <see cref="TelegramMessage"/>: its text is appended verbatim and each
    /// of its entities is re-anchored by shifting its offset by the current builder length (the
    /// length before the text was appended). <see cref="MessageEntity"/> is a mutable class, so the
    /// originals are never mutated — fresh instances are constructed copying every property.
    /// </summary>
    public TelegramMessageBuilder Append(TelegramMessage message)
    {
        var baseOffset = _sb.Length;
        _sb.Append(message.Text);
        foreach (var entity in message.Entities)
        {
            _entities.Add(new MessageEntity
            {
                Type = entity.Type,
                Offset = baseOffset + entity.Offset,
                Length = entity.Length,
                Url = entity.Url,
                User = entity.User,
                Language = entity.Language,
                CustomEmojiId = entity.CustomEmojiId
            });
        }
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
