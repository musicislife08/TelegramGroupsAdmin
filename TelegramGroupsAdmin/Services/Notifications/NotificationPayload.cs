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
