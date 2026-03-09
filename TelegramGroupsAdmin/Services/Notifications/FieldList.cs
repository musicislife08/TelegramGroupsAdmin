namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// List of labeled fields (key-value pairs with optional Telegram user ID for deep links).
/// </summary>
internal sealed record FieldList(IReadOnlyList<Field> Fields) : ContentBlock;
