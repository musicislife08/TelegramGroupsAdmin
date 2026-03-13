namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Base type for structured content blocks in notifications.
/// Each block type renders differently per channel (Telegram HTML, Email HTML, plain text).
/// </summary>
internal abstract record ContentBlock;
