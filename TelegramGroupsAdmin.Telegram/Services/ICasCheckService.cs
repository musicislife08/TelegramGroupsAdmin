using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for checking users against CAS (Combot Anti-Spam) database.
/// Used during user join to auto-ban known spammers before they can post.
/// </summary>
public interface ICasCheckService
{
    /// <summary>
    /// Check if a user is banned in the CAS (Combot Anti-Spam) database.
    /// </summary>
    /// <remarks>
    /// <para>Results are cached for 1 hour to reduce API calls.</para>
    /// <para>Fails open on API errors (returns not-banned) to avoid blocking legitimate users.</para>
    /// <para>Caller is responsible for checking if CAS is enabled before calling this method.</para>
    /// </remarks>
    /// <param name="user">The user identity to check.</param>
    /// <param name="casConfig">The CAS configuration containing API URL, timeout, and other settings.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="CasCheckResult"/> indicating whether the user is banned and the reason if applicable.
    /// Returns <c>IsBanned=false</c> if the API fails or the user is not in the database.
    /// </returns>
    Task<CasCheckResult> CheckUserAsync(UserIdentity user, CasConfig casConfig, CancellationToken cancellationToken = default);
}
