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
    /// Gets a random caption from the library
    /// </summary>
    Task<BanCelebrationCaption?> GetRandomAsync(CancellationToken ct = default);

    /// <summary>
    /// Claims the next caption in the current rotation cycle: picks a random caption not yet
    /// dispensed, marks it dispensed, and returns it. When the cycle is exhausted, starts a fresh
    /// cycle — holding back the caption dispensed last so it cannot repeat immediately — and
    /// claims from it. Returns null only when nothing is claimable — an empty library, or
    /// (vanishingly rarely) every pending row locked by concurrent claims.
    /// </summary>
    Task<BanCelebrationCaption?> ClaimNextForCycleAsync(CancellationToken ct = default);

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
