namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for parsing welcome callback data.
/// No side effects - 100% testable.
/// </summary>
public static class WelcomeCallbackParser
{
    /// <summary>
    /// Parses callback data from welcome-related inline button clicks.
    /// </summary>
    /// <param name="data">Raw callback data string from Telegram</param>
    /// <returns>Parsed callback data, or null if format is invalid or not a welcome callback</returns>
    public static WelcomeCallbackData? ParseCallbackData(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        var parts = data.Split(':');
        if (parts.Length < 2)
            return null;

        var action = parts[0];

        return action switch
        {
            "welcome_accept" => ParseTwoPartCallback(parts, WelcomeCallbackType.Accept),
            "welcome_deny" => ParseTwoPartCallback(parts, WelcomeCallbackType.Deny),
            "dm_accept" => ParseThreePartCallback(parts),
            _ => null
        };
    }

    /// <summary>
    /// Validates that the user who clicked a button is the intended target.
    /// </summary>
    /// <param name="callerId">User ID who clicked the button</param>
    /// <param name="targetUserId">User ID the button was intended for</param>
    /// <returns>True if caller is the target user</returns>
    public static bool ValidateCallerIsTarget(long callerId, long targetUserId)
    {
        return callerId == targetUserId;
    }

    private static WelcomeCallbackData? ParseTwoPartCallback(string[] parts, WelcomeCallbackType type)
    {
        // Format: action:userId
        if (parts.Length != 2)
            return null;

        if (!long.TryParse(parts[1], out var userId))
            return null;

        return new WelcomeCallbackData(type, userId);
    }

    private static WelcomeCallbackData? ParseThreePartCallback(string[] parts)
    {
        // Format: dm_accept:chatId:userId
        if (parts.Length != 3)
            return null;

        if (!long.TryParse(parts[1], out var chatId))
            return null;

        if (!long.TryParse(parts[2], out var userId))
            return null;

        return new WelcomeCallbackData(WelcomeCallbackType.DmAccept, userId, chatId);
    }
}

/// <summary>
/// Callback type for welcome system button clicks.
/// </summary>
public enum WelcomeCallbackType
{
    /// <summary>User accepted rules in chat</summary>
    Accept,

    /// <summary>User declined rules in chat</summary>
    Deny,

    /// <summary>User accepted rules via DM</summary>
    DmAccept
}

/// <summary>
/// Parsed welcome callback data.
/// </summary>
/// <param name="Type">Type of callback action</param>
/// <param name="UserId">Target user ID</param>
/// <param name="ChatId">Group chat ID (only for DmAccept)</param>
public record WelcomeCallbackData(
    WelcomeCallbackType Type,
    long UserId,
    long? ChatId = null);
