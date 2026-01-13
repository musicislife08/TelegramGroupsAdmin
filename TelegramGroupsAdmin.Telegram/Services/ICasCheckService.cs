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
    /// <para>Respects the CAS enabled/disabled setting in ContentDetectionConfig.</para>
    /// </remarks>
    /// <param name="userId">The Telegram user ID to check.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="CasCheckResult"/> indicating whether the user is banned and the reason if applicable.
    /// Returns <c>IsBanned=false</c> if CAS is disabled, the API fails, or the user is not in the database.
    /// </returns>
    Task<CasCheckResult> CheckUserAsync(long userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a CAS (Combot Anti-Spam) database check.
/// </summary>
/// <param name="IsBanned">Whether the user is banned in the CAS database.</param>
/// <param name="Reason">
/// The reason for the ban if <paramref name="IsBanned"/> is <c>true</c>;
/// otherwise <c>null</c>.
/// </param>
public record CasCheckResult(bool IsBanned, string? Reason);
