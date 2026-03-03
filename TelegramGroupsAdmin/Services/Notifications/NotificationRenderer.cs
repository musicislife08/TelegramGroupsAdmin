using System.Text;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Centralized multi-channel renderer for notification payloads.
/// Renders the same content blocks differently per delivery channel:
/// - Telegram HTML: bold headers, tg://user deep links, HTML entities
/// - Email HTML: full CSS-styled layout with container and footer
/// - Plain text: for web push notifications (no formatting)
/// </summary>
internal static class NotificationRenderer
{
    /// <summary>
    /// Render payload as Telegram HTML (ParseMode.Html).
    /// Fields with TelegramUserId get clickable tg://user deep links.
    /// </summary>
    public static string ToTelegramHtml(NotificationPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>{EscapeHtml(payload.Subject)}</b>");
        sb.AppendLine();
        RenderBlocksTelegram(sb, payload.Blocks, indent: false);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Render payload as full HTML email with CSS styling.
    /// tg://user links render as plain text (not actionable in email clients).
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
        sb.AppendLine($"        <h2>{EscapeHtml(payload.Subject)}</h2>");
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
    /// No formatting, TelegramUserId ignored.
    /// </summary>
    public static string ToPlainText(NotificationPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine(payload.Subject);
        sb.AppendLine();
        RenderBlocksPlainText(sb, payload.Blocks, indent: "");
        return sb.ToString().TrimEnd();
    }

    // ── Telegram HTML rendering ──

    private static void RenderBlocksTelegram(StringBuilder sb, IReadOnlyList<ContentBlock> blocks, bool indent)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.AppendLine(EscapeHtml(text.Text));
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        var value = field.TelegramUserId.HasValue
                            ? $"<a href=\"tg://user?id={field.TelegramUserId.Value}\">{EscapeHtml(field.Value)}</a>"
                            : EscapeHtml(field.Value);
                        sb.AppendLine($"<b>{EscapeHtml(field.Label)}:</b> {value}");
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine();
                    sb.AppendLine($"<b>{EscapeHtml(section.Header)}</b>");
                    RenderBlocksTelegram(sb, section.Content, indent: true);
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
                    sb.AppendLine($"        <p>{EscapeHtml(text.Text)}</p>");
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        // tg://user links aren't clickable in email — render as plain text
                        sb.AppendLine($"        <div class=\"field\"><span class=\"field-label\">{EscapeHtml(field.Label)}:</span> {EscapeHtml(field.Value)}</div>");
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine($"        <h3>{EscapeHtml(section.Header)}</h3>");
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

    // ── Utilities ──

    internal static string EscapeHtml(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : System.Net.WebUtility.HtmlEncode(text);
}
