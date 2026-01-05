using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for detecting impersonation attempts (name + photo matching)
/// </summary>
public interface IImpersonationDetectionService
{
    /// <summary>
    /// Checks if a user should be checked for impersonation
    /// based on message count and trusted status
    /// </summary>
    Task<bool> ShouldCheckUserAsync(long userId, long chatId);

    /// <summary>
    /// Checks a user for impersonation against all chat admins
    /// Returns null if no matches found (score = 0)
    /// </summary>
    /// <param name="user">SDK User object from message</param>
    /// <param name="chat">SDK Chat object from message</param>
    /// <param name="photoPath">User's photo path from database (if available)</param>
    Task<ImpersonationCheckResult?> CheckUserAsync(
        User user,
        Chat chat,
        string? photoPath);

    /// <summary>
    /// Executes action based on check result (auto-ban, log alert)
    /// </summary>
    Task ExecuteActionAsync(ImpersonationCheckResult result);
}
