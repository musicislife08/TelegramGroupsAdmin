using System.Net;
using System.Text;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Renders a <see cref="TelegramMessage"/> (text + entities) to an HTML fragment for display in
/// the UI preview — the display-side inverse of <see cref="TelegramMessageBuilder"/>. Composing
/// (builder) and displaying (renderer) share the one entity model, so a preview renders the exact
/// object type that is sent to Telegram.
///
/// Entity offsets are UTF-16 code units, which align with .NET <see cref="string"/> indexing, so
/// the walk indexes the text directly (an emoji is length 2 in both worlds). Assumes flat,
/// in-order, non-overlapping entities — everything <see cref="TelegramMessageBuilder"/> produces.
/// Nested entities (which the Telegram API permits but this codebase never emits) are not supported;
/// an entity that overlaps an already-rendered span is skipped rather than mis-rendered.
/// </summary>
public static class TelegramEntityRenderer
{
    /// <summary>
    /// Render <paramref name="message"/> to an HTML fragment. Literal text is HTML-encoded first,
    /// then entity spans are wrapped — encode-then-wrap, so message text can never inject markup.
    /// </summary>
    public static string ToHtml(TelegramMessage message)
    {
        var text = message.Text;
        if (message.Entities.Count == 0)
            return Encode(text);

        var ordered = message.Entities.OrderBy(e => e.Offset).ToList();
        var sb = new StringBuilder(text.Length + ordered.Count * 16);
        var cursor = 0;

        foreach (var entity in ordered)
        {
            // Overlapping / nested entity — unsupported, skip it rather than corrupt the output.
            if (entity.Offset < cursor)
                continue;

            if (entity.Offset > cursor)
                sb.Append(Encode(text.Substring(cursor, entity.Offset - cursor)));

            sb.Append(Wrap(entity, text.Substring(entity.Offset, entity.Length)));
            cursor = entity.Offset + entity.Length;
        }

        if (cursor < text.Length)
            sb.Append(Encode(text[cursor..]));

        return sb.ToString();
    }

    private static string Wrap(MessageEntity entity, string inner) => entity.Type switch
    {
        MessageEntityType.Bold => $"<b>{Encode(inner)}</b>",
        MessageEntityType.Italic => $"<i>{Encode(inner)}</i>",
        MessageEntityType.Underline => $"<u>{Encode(inner)}</u>",
        MessageEntityType.Strikethrough => $"<s>{Encode(inner)}</s>",
        MessageEntityType.Code => $"<code>{Encode(inner)}</code>",
        MessageEntityType.Pre => $"<pre>{Encode(inner)}</pre>",
        MessageEntityType.TextLink => $"<a href=\"{Encode(entity.Url)}\">{Encode(inner)}</a>",
        MessageEntityType.TextMention => $"<span class=\"tg-mention\">{Encode(inner)}</span>",
        _ => Encode(inner),
    };

    private static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
}
