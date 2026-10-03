namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for one (user, chat) DM ban celebration opt-in, including any open start prompt.
/// </summary>
public sealed record BanCelebrationSubscriber(
    long TelegramUserId,
    long ChatId,
    DateTimeOffset SubscribedAt,
    int? PromptMessageId,
    string? PromptDeleteJobId);
