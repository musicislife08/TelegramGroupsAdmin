namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Centralized utility for formatting Telegram user display names consistently across the application.
/// Eliminates inconsistent patterns like Username ?? FirstName vs FirstName ?? Username.
/// </summary>
public static class TelegramDisplayName
{
    /// <summary>
    /// Gets display name for UI/logs. No @ prefix.
    /// Priority: Service Account (chatName) → FullName (First + Last) → Username → User {id}
    /// </summary>
    /// <param name="firstName">User's first name from Telegram profile</param>
    /// <param name="lastName">User's last name from Telegram profile</param>
    /// <param name="username">User's username (without @ prefix)</param>
    /// <param name="userId">User's Telegram ID for fallback display</param>
    /// <param name="chatName">Optional chat name - used for service account (777000) to show channel/group name</param>
    /// <returns>Formatted display name suitable for UI display</returns>
    public static string Format(string? firstName, string? lastName, string? username, long? userId = null, string? chatName = null)
    {
        // Special handling for Telegram service account (channel posts, anonymous admin posts)
        // In Telegram clients, these show as the channel/group name, not as a user
        if (userId == TelegramConstants.ServiceAccountUserId)
            return !string.IsNullOrWhiteSpace(chatName) ? chatName : "Telegram Service Account";

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        if (!string.IsNullOrWhiteSpace(username))
            return username;

        return userId.HasValue ? $"User {userId}" : "Unknown User";
    }

    /// <summary>
    /// Gets mention-style name for Telegram bot messages. Uses @username when available.
    /// Priority: @Username → FullName (First + Last) → User {id}
    /// </summary>
    /// <param name="firstName">User's first name from Telegram profile</param>
    /// <param name="lastName">User's last name from Telegram profile</param>
    /// <param name="username">User's username (without @ prefix)</param>
    /// <param name="userId">User's Telegram ID for fallback display</param>
    /// <returns>Formatted name suitable for Telegram bot messages (with @ prefix when username available)</returns>
    /// <remarks>
    /// Use this for bot command responses where @username creates a clickable mention.
    /// For guaranteed clickable mentions (even without username), use HTML text mentions:
    /// &lt;a href="tg://user?id=123"&gt;DisplayName&lt;/a&gt;
    /// </remarks>
    public static string FormatMention(string? firstName, string? lastName, string? username, long? userId = null)
    {
        // @username creates clickable mention in Telegram
        if (!string.IsNullOrWhiteSpace(username))
            return $"@{username}";

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return userId.HasValue ? $"User {userId}" : "Unknown User";
    }
}
