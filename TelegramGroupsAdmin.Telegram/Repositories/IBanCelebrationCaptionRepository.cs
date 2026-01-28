using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ban celebration captions
/// </summary>
public interface IBanCelebrationCaptionRepository
{
    /// <summary>
    /// Gets all ban celebration captions ordered by creation date
    /// </summary>
    Task<List<BanCelebrationCaption>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all caption IDs (lightweight query for shuffle-bag algorithm)
    /// </summary>
    Task<List<int>> GetAllIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a random caption from the library
    /// </summary>
    Task<BanCelebrationCaption?> GetRandomAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a specific caption by ID
    /// </summary>
    Task<BanCelebrationCaption?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new caption to the library
    /// </summary>
    Task<BanCelebrationCaption> AddAsync(string text, string dmText, string? name, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing caption
    /// </summary>
    Task<BanCelebrationCaption> UpdateAsync(int id, string text, string dmText, string? name, CancellationToken ct = default);

    /// <summary>
    /// Deletes a caption from the library
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Gets the count of captions in the library
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Seeds the default captions if the library is empty
    /// Called on startup to populate initial captions
    /// </summary>
    Task SeedDefaultsIfEmptyAsync(CancellationToken ct = default);
}
