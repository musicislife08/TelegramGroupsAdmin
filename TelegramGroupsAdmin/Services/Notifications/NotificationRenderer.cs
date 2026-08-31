using System.Text;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Centralized multi-channel renderer for notification payloads.
/// Renders the same content blocks differently per delivery channel:
/// - Telegram: bold/mention entities (parse_mode-free)
/// - Email HTML: full CSS-styled layout with container and footer
/// - Plain text: for web push notifications (no formatting)
/// </summary>
internal static class NotificationRenderer
{
    /// <summary>
    /// Render payload as entity-based Telegram message.
    /// Emits Bold entities for subject, field labels, and section headers;
    /// TextMention entities with full User object for clickable user mentions.
    /// No HTML — uses the entities parameter which is mutually exclusive with parse_mode.
    /// </summary>
    public static TelegramMessage ToTelegramMessage(NotificationPayload payload)
    {
        var builder = new TelegramMessageBuilder();
        builder.Bold(payload.Subject).LineBreak().LineBreak();
        RenderBlocksTelegram(builder, payload.Blocks);
        var msg = builder.Build();
        return new TelegramMessage(msg.Text.TrimEnd(), msg.Entities);
    }

    /// <summary>
    /// Render payload as full HTML email with CSS styling.
    /// Email clients don't resolve tg://user links, so user fields render as plain text.
    /// </summary>
    public static string ToEmailHtml(NotificationPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; }
        .container { max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 5px; }
        h2 { color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px; }
        h3 { color: #34495e; margin-top: 16px; margin-bottom: 8px; }
        .field { margin: 4px 0; }
        .field-label { font-weight: bold; }
        .footer { margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }
    </style>
</head>
<body>
    <div class=""container"">");
        sb.AppendLine($"        <h2>{EncodeHtml(payload.Subject)}</h2>");
        RenderBlocksEmail(sb, payload.Blocks);
        sb.AppendLine(@"        <div class=""footer"">
            <p>This is an automated notification from TelegramGroupsAdmin.</p>
            <p>To manage your notification preferences, visit your Profile Settings.</p>
        </div>
    </div>
</body>
</html>");
        return sb.ToString();
    }

    /// <summary>
    /// Render payload as plain text for web push notifications.
    /// No formatting, user mentions ignored.
    /// </summary>
    public static string ToPlainText(NotificationPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine(payload.Subject);
        sb.AppendLine();
        RenderBlocksPlainText(sb, payload.Blocks, indent: "");
        return sb.ToString().TrimEnd();
    }

    // ── Telegram entity-based rendering ──

    private static void RenderBlocksTelegram(TelegramMessageBuilder builder, IReadOnlyList<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    builder.Text(text.Text).LineBreak();
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        builder.Bold($"{field.Label}:").Text(" ");
                        if (field.User is { } u)
                            builder.Mention(u);
                        else
                            builder.Text(field.Value);
                        builder.LineBreak();
                    }
                    break;

                case SectionBlock section:
                    builder.LineBreak().Bold(section.Header).LineBreak();
                    RenderBlocksTelegram(builder, section.Content);
                    break;
            }
        }
    }

    // ── Email HTML rendering ──

    private static void RenderBlocksEmail(StringBuilder sb, IReadOnlyList<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.AppendLine($"        <p>{EncodeHtml(text.Text)}</p>");
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        // User mentions aren't clickable in email — render as plain text
                        sb.AppendLine($"        <div class=\"field\"><span class=\"field-label\">{EncodeHtml(field.Label)}:</span> {EncodeHtml(field.Value)}</div>");
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine($"        <h3>{EncodeHtml(section.Header)}</h3>");
                    RenderBlocksEmail(sb, section.Content);
                    break;
            }
        }
    }

    // ── Plain text rendering ──

    private static void RenderBlocksPlainText(StringBuilder sb, IReadOnlyList<ContentBlock> blocks, string indent)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.AppendLine($"{indent}{text.Text}");
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        sb.AppendLine($"{indent}{field.Label}: {field.Value}");
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine();
                    sb.AppendLine($"{indent}{section.Header}");
                    RenderBlocksPlainText(sb, section.Content, indent: indent + "  ");
                    break;
            }
        }
    }

    // ── Helpers ──

    private static string EncodeHtml(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
}
