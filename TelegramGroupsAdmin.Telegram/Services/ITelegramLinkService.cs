using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing Telegram account linking operations.
/// Handles token generation and account unlinking with proper authorization.
/// </summary>
public interface ITelegramLinkService
{
    /// <summary>
    /// Generate a new link token for a user.
    /// Revokes any existing unused tokens before generating a new one.
    /// </summary>
    /// <param name="userId">The web app user ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The generated token record with token string and expiry</returns>
    Task<TelegramLinkTokenRecord> GenerateLinkTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlink a Telegram account from a user.
    /// Verifies the mapping belongs to the specified user before deactivating.
    /// </summary>
    /// <param name="mappingId">The mapping ID to unlink</param>
    /// <param name="userId">The user ID (for authorization check)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if unlinked successfully, false if not found or unauthorized</returns>
    Task<bool> UnlinkAccountAsync(long mappingId, string userId, CancellationToken cancellationToken = default);
}
