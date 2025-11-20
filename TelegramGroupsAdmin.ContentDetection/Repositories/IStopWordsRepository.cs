namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing stop words in the spam detection system
/// </summary>
public interface IStopWordsRepository
{
    /// <summary>
    /// Get all enabled stop words
    /// </summary>
    Task<IEnumerable<string>> GetEnabledStopWordsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new stop word
    /// </summary>
    Task<long> AddStopWordAsync(Models.StopWord stopWord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enable or disable a stop word
    /// </summary>
    Task<bool> SetStopWordEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a word exists as a stop word
    /// </summary>
    Task<bool> ExistsAsync(string word, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all stop words (enabled and disabled) with full details
    /// </summary>
    Task<IEnumerable<Models.StopWord>> GetAllStopWordsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update stop word notes
    /// </summary>
    Task<bool> UpdateStopWordNotesAsync(long id, string? notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a stop word by ID
    /// </summary>
    Task<bool> DeleteStopWordAsync(long id, CancellationToken cancellationToken = default);
}