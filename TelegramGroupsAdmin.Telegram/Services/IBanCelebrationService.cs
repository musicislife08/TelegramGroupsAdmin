namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for posting celebratory GIFs when users are banned.
/// Configurable per-chat with optional DM to banned user.
/// </summary>
public interface IBanCelebrationService
{
    /// <summary>
    /// Send a ban celebration GIF and caption to the chat (and optionally to the banned user).
    /// Respects per-chat configuration for enabled state and triggers.
    /// </summary>
    /// <param name="chatId">The chat where the ban occurred</param>
    /// <param name="chatName">Display name of the chat (for caption placeholders)</param>
    /// <param name="bannedUserId">Telegram user ID of the banned user</param>
    /// <param name="bannedUserName">Display name of the banned user (for caption placeholders)</param>
    /// <param name="isAutoBan">True if this was an automatic spam detection ban, false if manual</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if celebration was sent, false if skipped (disabled/no content/error)</returns>
    Task<bool> SendBanCelebrationAsync(
        long chatId,
        string chatName,
        long bannedUserId,
        string bannedUserName,
        bool isAutoBan,
        CancellationToken cancellationToken = default);
}
