namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Chat summary for the sidebar (projection of ManagedChat).
/// </summary>
public record ChatSummary(
    long ChatId,
    string ChatName,
    string? ChatIconPath,
    int MessageCount,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview
);
