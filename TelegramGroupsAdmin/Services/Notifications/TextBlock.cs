namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Free-form text paragraph.
/// </summary>
internal sealed record TextBlock(string Text) : ContentBlock;
