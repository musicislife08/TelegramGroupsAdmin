using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for profile scan result history.
/// </summary>
public interface IProfileScanResultsRepository
{
    /// <summary>
    /// Insert a new scan result record.
    /// </summary>
    Task<long> InsertAsync(ProfileScanResultRecord record, CancellationToken ct);

    /// <summary>
    /// Get all scan results for a user, most recent first.
    /// </summary>
    Task<List<ProfileScanResultRecord>> GetByUserIdAsync(long userId, CancellationToken ct);

    /// <summary>
    /// Get the most recent scan result for a user, or null if never scanned.
    /// </summary>
    Task<ProfileScanResultRecord?> GetLatestByUserIdAsync(long userId, CancellationToken ct);
}
