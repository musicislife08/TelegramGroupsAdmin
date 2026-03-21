using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IUsernameBlacklistRepository
{
    Task<IReadOnlyList<UsernameBlacklistEntry>> GetEnabledEntriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsernameBlacklistEntry>> GetAllEntriesAsync(CancellationToken cancellationToken = default);
    Task<long> AddEntryAsync(UsernameBlacklistEntry entry, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string pattern, CancellationToken cancellationToken = default);
    Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);
    Task<bool> DeleteEntryAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> UpdateNotesAsync(long id, string? notes, CancellationToken cancellationToken = default);
}
