namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Centralized utility for formatting entity identifiers in log messages.
/// Provides consistent, readable log output across the application.
/// </summary>
/// <remarks>
/// <para>
/// Pattern by log level:
/// - Info logs: Name only (clean, readable) - use *Info methods
/// - Debug/Warning/Error logs: Name + ID (for investigation) - use *Debug methods
/// </para>
/// <para>
/// When name is unavailable, falls back to ID-only format (e.g., "Chat 123", "User abc-def").
/// </para>
/// </remarks>
public static class LogDisplayName
{
    #region Chats

    /// <summary>
    /// Formats chat identifier for Info-level logs. Returns name only.
    /// </summary>
    /// <param name="chatName">Chat title/name (from API or database)</param>
    /// <param name="chatId">Telegram chat ID</param>
    /// <returns>"ChatName" or "Chat {chatId}" if name unavailable</returns>
    public static string ChatInfo(string? chatName, long chatId)
        => !string.IsNullOrWhiteSpace(chatName) ? chatName : $"Chat {chatId}";

    /// <summary>
    /// Formats chat identifier for Debug/Warning/Error logs. Returns name + ID.
    /// </summary>
    /// <param name="chatName">Chat title/name (from API or database)</param>
    /// <param name="chatId">Telegram chat ID</param>
    /// <returns>"ChatName (chatId)" or "Unknown Chat (chatId)" if name unavailable</returns>
    public static string ChatDebug(string? chatName, long chatId)
        => $"{(string.IsNullOrWhiteSpace(chatName) ? "Unknown Chat" : chatName)} ({chatId})";

    #endregion

    #region Telegram Users

    /// <summary>
    /// Formats Telegram user identifier for Info-level logs. Returns name only.
    /// </summary>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="username">User's username (without @ prefix)</param>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>Display name or "User {userId}" if name unavailable</returns>
    public static string UserInfo(string? firstName, string? lastName, string? username, long userId)
        => TelegramDisplayName.Format(firstName, lastName, username, userId);

    /// <summary>
    /// Formats Telegram user identifier for Debug/Warning/Error logs. Returns name + ID.
    /// </summary>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="username">User's username (without @ prefix)</param>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>"DisplayName (userId)"</returns>
    public static string UserDebug(string? firstName, string? lastName, string? username, long userId)
        => $"{TelegramDisplayName.Format(firstName, lastName, username, userId)} ({userId})";

    /// <summary>
    /// Formats Telegram user identifier for Debug/Warning/Error logs using pre-formatted display name.
    /// Use when display name is already formatted (e.g., passed through ContentCheckRequest.UserName).
    /// </summary>
    /// <param name="displayName">Pre-formatted display name from TelegramDisplayName.Format()</param>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>"DisplayName (userId)"</returns>
    public static string UserDebug(string? displayName, long userId)
        => $"{displayName ?? $"User {userId}"} ({userId})";

    #endregion

    #region Web Users

    /// <summary>
    /// Formats web user identifier for Info-level logs. Returns email only.
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="userId">Web user GUID (as string)</param>
    /// <returns>"email@example.com" or "User {userId}" if email unavailable</returns>
    public static string WebUserInfo(string? email, string? userId)
        => !string.IsNullOrWhiteSpace(email) ? email : $"User {userId}";

    /// <summary>
    /// Formats web user identifier for Debug/Warning/Error logs. Returns email + ID.
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="userId">Web user GUID (as string)</param>
    /// <returns>"email@example.com (userId)" or "Unknown User (userId)" if email unavailable</returns>
    public static string WebUserDebug(string? email, string userId)
        => $"{(string.IsNullOrWhiteSpace(email) ? "Unknown User" : email)} ({userId})";

    #endregion
}
