namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building and parsing Telegram deep links.
/// No side effects - 100% testable.
/// </summary>
public static class WelcomeDeepLinkBuilder
{
    private const string WelcomePrefix = "welcome_";
    private const string ExamPrefix = "exam_start_";

    /// <summary>
    /// Builds a /start deep link for DM welcome flow.
    /// When clicked, opens DM with bot and triggers /start command with payload.
    /// </summary>
    /// <param name="botUsername">Bot username (without @)</param>
    /// <param name="chatId">Group chat ID</param>
    /// <param name="userId">Target user ID</param>
    /// <returns>Full deep link URL</returns>
    public static string BuildStartDeepLink(string botUsername, long chatId, long userId)
    {
        return $"https://t.me/{botUsername}?start={WelcomePrefix}{chatId}_{userId}";
    }

    /// <summary>
    /// Builds a public chat link from the chat's username.
    /// Returns null for private chats (which don't have usernames).
    /// </summary>
    /// <param name="chatUsername">Chat username (without @), or null for private chats</param>
    /// <returns>Public chat link, or null if chat is private</returns>
    public static string? BuildPublicChatLink(string? chatUsername)
    {
        if (string.IsNullOrWhiteSpace(chatUsername))
            return null;

        return $"https://t.me/{chatUsername}";
    }

    /// <summary>
    /// Parses a /start payload from a welcome deep link.
    /// </summary>
    /// <param name="payload">Start command payload (after ?start=)</param>
    /// <returns>Parsed payload data, or null if invalid format</returns>
    public static WelcomeStartPayload? ParseStartPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return null;

        if (!payload.StartsWith(WelcomePrefix))
            return null;

        // Format: welcome_chatId_userId
        var parts = payload.Split('_');
        if (parts.Length != 3)
            return null;

        // parts[0] = "welcome", parts[1] = chatId, parts[2] = userId
        if (!long.TryParse(parts[1], out var chatId))
            return null;

        if (!long.TryParse(parts[2], out var userId))
            return null;

        return new WelcomeStartPayload(chatId, userId);
    }

    /// <summary>
    /// Builds a /start deep link for entrance exam flow.
    /// When clicked, opens DM with bot and triggers /start command with exam payload.
    /// </summary>
    /// <param name="botUsername">Bot username (without @)</param>
    /// <param name="chatId">Group chat ID</param>
    /// <param name="userId">Target user ID</param>
    /// <returns>Full deep link URL</returns>
    public static string BuildExamStartDeepLink(string botUsername, long chatId, long userId)
    {
        return $"https://t.me/{botUsername}?start={ExamPrefix}{chatId}_{userId}";
    }

    /// <summary>
    /// Parses a /start payload from an exam deep link.
    /// </summary>
    /// <param name="payload">Start command payload (after ?start=)</param>
    /// <returns>Parsed payload data, or null if invalid format</returns>
    public static ExamStartPayload? ParseExamStartPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return null;

        if (!payload.StartsWith(ExamPrefix))
            return null;

        // Format: exam_start_chatId_userId
        var remainder = payload[ExamPrefix.Length..];
        var parts = remainder.Split('_');
        if (parts.Length != 2)
            return null;

        if (!long.TryParse(parts[0], out var chatId))
            return null;

        if (!long.TryParse(parts[1], out var userId))
            return null;

        return new ExamStartPayload(chatId, userId);
    }

    /// <summary>
    /// Checks if a /start payload is an exam deep link.
    /// </summary>
    public static bool IsExamPayload(string? payload)
        => !string.IsNullOrEmpty(payload) && payload.StartsWith(ExamPrefix);
}

/// <summary>
/// Parsed /start payload from a welcome deep link.
/// </summary>
/// <param name="ChatId">Group chat ID</param>
/// <param name="UserId">Target user ID</param>
public record WelcomeStartPayload(long ChatId, long UserId);

/// <summary>
/// Parsed /start payload from an exam deep link.
/// </summary>
/// <param name="ChatId">Group chat ID where user joined</param>
/// <param name="UserId">Target user ID taking the exam</param>
public record ExamStartPayload(long ChatId, long UserId);
