using TelegramGroupsAdmin.Core.Models;
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
    Task<long> InsertAsync(ProfileScanResultRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Get all scan results for a user, most recent first.
    /// </summary>
    Task<List<ProfileScanResultRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// Source of the user's latest scan (scanned_at DESC, id DESC), or null when the user has none.
    /// </summary>
    Task<ProfileScanSource?> GetLatestSourceAsync(long userId, CancellationToken cancellationToken);
}
