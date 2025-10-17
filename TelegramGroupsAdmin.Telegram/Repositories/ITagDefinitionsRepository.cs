using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface ITagDefinitionsRepository
{
    /// <summary>
    /// Gets all tag definitions ordered by usage count descending
    /// </summary>
    Task<List<TagDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific tag definition by name
    /// </summary>
    Task<TagDefinition?> GetByNameAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new tag definition with color preference
    /// </summary>
    Task<TagDefinition> CreateAsync(string tagName, TagColor color, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the color for an existing tag definition
    /// </summary>
    Task<bool> UpdateColorAsync(string tagName, TagColor color, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tag definition (only if usage_count is 0)
    /// </summary>
    Task<bool> DeleteAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the usage count for a tag (called when tag is added to a user)
    /// </summary>
    Task IncrementUsageAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrements the usage count for a tag (called when tag is removed from a user)
    /// </summary>
    Task DecrementUsageAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tag definition exists
    /// </summary>
    Task<bool> ExistsAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tag names that match a search query
    /// </summary>
    Task<List<string>> SearchTagNamesAsync(string searchTerm, int limit = 50, CancellationToken cancellationToken = default);
}
