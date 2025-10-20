using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing file scanning configuration
/// Handles loading, saving, and merging global + chat-specific configs
/// </summary>
public interface IFileScanningConfigRepository
{
    /// <summary>
    /// Get file scanning config for a specific chat (with global fallback)
    /// Returns merged config: chat-specific overrides applied to global defaults
    /// </summary>
    /// <param name="chatId">Chat ID (null = global only)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged configuration or default if not found</returns>
    Task<FileScanningConfig> GetAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save file scanning config for global or specific chat
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="chatId">Chat ID (null = global)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveAsync(FileScanningConfig config, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete file scanning config for a specific chat (reverts to global)
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
}
