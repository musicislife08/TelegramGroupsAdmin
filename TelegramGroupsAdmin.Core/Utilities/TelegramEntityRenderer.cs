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
                sb.Append(Encode(text[cursor..entity.Offset]));

            sb.Append(Wrap(entity, text[entity.Offset..(entity.Offset + entity.Length)]));
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
        MessageEntityType.TextLink => IsSafeUrl(entity.Url)
            ? $"<a href=\"{Encode(entity.Url)}\" rel=\"noopener noreferrer\">{Encode(inner)}</a>"
            : Encode(inner),
        MessageEntityType.TextMention => $"<span class=\"tg-mention\">{Encode(inner)}</span>",
        _ => Encode(inner),
    };

    // A TextLink href is rendered into the preview verbatim, so a javascript:/data: URL would be a
    // latent XSS vector. Only emit an anchor for an absolute URL whose scheme is on the allowlist;
    // anything else falls back to encoded inner text with no anchor. mailto:/tg: parse as absolute
    // URIs with those exact schemes.
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto", "tg"];

    private static bool IsSafeUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && AllowedSchemes.Contains(uri.Scheme);

    // Encode only the HTML-significant characters. WebUtility.HtmlEncode would also turn every
    // non-ASCII rune (emoji, Cyrillic, CJK) into a numeric entity, bloating the preview markup —
    // Telegram messages are full of such text. Escaping &, <, >, " is sufficient to keep text
    // content and double-quoted attribute values XSS-safe while leaving Unicode readable.
    private static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
