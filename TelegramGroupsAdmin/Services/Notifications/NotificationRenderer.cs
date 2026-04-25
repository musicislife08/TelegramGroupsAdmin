using System.Text;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
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
        var sb = new StringBuilder();
        var entities = new List<MessageEntity>();

        AppendBold(sb, entities, payload.Subject);
        sb.AppendLine();
        sb.AppendLine();

        RenderBlocksTelegram(sb, entities, payload.Blocks);

        return new TelegramMessage(sb.ToString().TrimEnd(), entities);
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
        sb.AppendLine($"        <h2>{TelegramHtmlEncoder.Encode(payload.Subject)}</h2>");
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

    private static void RenderBlocksTelegram(
        StringBuilder sb, List<MessageEntity> entities, IReadOnlyList<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.AppendLine(text.Text);
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        AppendBold(sb, entities, $"{field.Label}:");
                        sb.Append(' ');
                        if (field.User is { } u)
                            AppendUserMention(sb, entities, field.Value, u);
                        else
                            sb.Append(field.Value);
                        sb.AppendLine();
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine();
                    AppendBold(sb, entities, section.Header);
                    sb.AppendLine();
                    RenderBlocksTelegram(sb, entities, section.Content);
                    break;
            }
        }
    }

    private static void AppendBold(StringBuilder sb, List<MessageEntity> entities, string text)
    {
        var offset = sb.Length;
        sb.Append(text);
        entities.Add(new MessageEntity
        {
            Type = MessageEntityType.Bold,
            Offset = offset,
            Length = text.Length
        });
    }

    private static void AppendUserMention(
        StringBuilder sb, List<MessageEntity> entities, string displayText, UserIdentity user)
    {
        var offset = sb.Length;
        sb.Append(displayText);
        entities.Add(new MessageEntity
        {
            Type = MessageEntityType.TextMention,
            Offset = offset,
            Length = displayText.Length,
            User = new User
            {
                Id = user.Id,
                IsBot = false,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName,
                Username = user.Username
            }
        });
    }

    // ── Email HTML rendering ──

    private static void RenderBlocksEmail(StringBuilder sb, IReadOnlyList<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.AppendLine($"        <p>{TelegramHtmlEncoder.Encode(text.Text)}</p>");
                    break;

                case FieldList fieldList:
                    foreach (var field in fieldList.Fields)
                    {
                        // User mentions aren't clickable in email — render as plain text
                        sb.AppendLine($"        <div class=\"field\"><span class=\"field-label\">{TelegramHtmlEncoder.Encode(field.Label)}:</span> {TelegramHtmlEncoder.Encode(field.Value)}</div>");
                    }
                    break;

                case SectionBlock section:
                    sb.AppendLine($"        <h3>{TelegramHtmlEncoder.Encode(section.Header)}</h3>");
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

}
