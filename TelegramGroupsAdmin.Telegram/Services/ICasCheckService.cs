namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for checking users against CAS (Combot Anti-Spam) database.
/// Used during user join to auto-ban known spammers.
/// </summary>
public interface ICasCheckService
{
    /// <summary>
    /// Check if user is banned in CAS database.
    /// </summary>
    /// <param name="userId">Telegram user ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Check result with banned status and reason</returns>
    Task<CasCheckResult> CheckUserAsync(long userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of CAS check
/// </summary>
/// <param name="IsBanned">Whether user is banned in CAS database</param>
/// <param name="Reason">Reason for ban (if banned)</param>
public record CasCheckResult(bool IsBanned, string? Reason);
