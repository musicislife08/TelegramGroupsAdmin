using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for manual domain filters (blacklist/whitelist)
/// Phase 4.13: URL Filtering
/// </summary>
public interface IDomainFiltersRepository
{
    /// <summary>
    /// Get all domain filters
    /// If chatId = 0: Returns global only
    /// If chatId > 0: Returns global + chat-specific (for display/merging in UI)
    /// </summary>
    Task<List<DomainFilter>> GetAllAsync(long chatId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get effective domain filters for a chat (merges global + chat-specific)
    /// </summary>
    Task<List<DomainFilter>> GetEffectiveAsync(long chatId, DomainFilterType? filterType = null, BlockMode? blockMode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get domain filter by ID
    /// </summary>
    Task<DomainFilter?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert new domain filter
    /// </summary>
    Task<long> InsertAsync(DomainFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing domain filter
    /// </summary>
    Task UpdateAsync(DomainFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete domain filter
    /// </summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
