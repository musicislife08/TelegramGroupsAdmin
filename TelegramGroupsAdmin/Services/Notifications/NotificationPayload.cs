using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Immutable notification payload produced by the builder.
/// Contains structured content blocks that render differently per channel.
/// </summary>
internal sealed record NotificationPayload
{
    public required string Subject { get; init; }
    public IReadOnlyList<ContentBlock> Blocks { get; init; } = [];
    public string? PhotoPath { get; init; }
    public string? VideoPath { get; init; }
    public ActionKeyboardContext? Keyboard { get; init; }
}

/// <summary>
/// Base type for structured content blocks in notifications.
/// Each block type renders differently per channel (Telegram HTML, Email HTML, plain text).
/// </summary>
internal abstract record ContentBlock;

/// <summary>
/// Free-form text paragraph.
/// </summary>
internal sealed record TextBlock(string Text) : ContentBlock;

/// <summary>
/// List of labeled fields (key-value pairs with optional Telegram user ID for deep links).
/// </summary>
internal sealed record FieldList(IReadOnlyList<Field> Fields) : ContentBlock;

/// <summary>
/// Named section containing nested content blocks.
/// Renders as a bold header followed by indented content.
/// </summary>
internal sealed record SectionBlock(string Header, IReadOnlyList<ContentBlock> Content) : ContentBlock;

/// <summary>
/// A single labeled field. When TelegramUserId is set, the value renders as a
/// clickable tg://user deep link in Telegram DMs.
/// </summary>
internal sealed record Field(string Label, string Value, long? TelegramUserId = null);

/// <summary>
/// Context for building inline keyboard action buttons on notifications.
/// Reuses the existing ReportCallbackContext infrastructure.
/// </summary>
internal sealed record ActionKeyboardContext(
    long EntityId,
    long ChatId,
    long UserId,
    ReportType KeyboardType);
