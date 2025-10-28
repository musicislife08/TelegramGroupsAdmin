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
    Task<ImpersonationCheckResult?> CheckUserAsync(
        long userId,
        long chatId,
        string? firstName,
        string? lastName,
        string? photoPath);

    /// <summary>
    /// Executes action based on check result (auto-ban, log alert)
    /// </summary>
    Task ExecuteActionAsync(ImpersonationCheckResult result);
}
