using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for verification token management operations
/// </summary>
public interface IVerificationTokenRepository
{
    /// <summary>
    /// Create a new verification token and return its ID
    /// </summary>
    Task<long> CreateAsync(DataModels.VerificationTokenDto verificationToken, CancellationToken ct = default);

    /// <summary>
    /// Get a verification token by its token string
    /// </summary>
    Task<UiModels.VerificationToken?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Get a valid (unused and not expired) verification token by token string and type
    /// </summary>
    Task<UiModels.VerificationToken?> GetValidTokenAsync(string token, DataModels.TokenType tokenType, CancellationToken ct = default);

    /// <summary>
    /// Mark a verification token as used
    /// </summary>
    Task MarkAsUsedAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Clean up expired verification tokens and return count of deleted records
    /// </summary>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Delete all verification tokens for a specific user and return count of deleted records
    /// </summary>
    Task<int> DeleteByUserIdAsync(string userId, CancellationToken ct = default);
}
