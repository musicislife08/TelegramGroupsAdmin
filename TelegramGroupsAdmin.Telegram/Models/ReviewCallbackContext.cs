using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for review moderation callback button context.
/// Action is passed in callback data, not stored here.
/// ReviewType determines which action handler to use.
/// </summary>
public record ReviewCallbackContext(
    long Id,
    long ReviewId,
    ReviewType ReviewType,
    long ChatId,
    long UserId,
    DateTimeOffset CreatedAt);
