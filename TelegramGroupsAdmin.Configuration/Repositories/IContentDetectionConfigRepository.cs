using TelegramGroupsAdmin.Configuration.Models.ContentDetection;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing content detection configurations
/// </summary>
public interface IContentDetectionConfigRepository
{
    /// <summary>
    /// Get the global spam detection configuration
    /// </summary>
    Task<ContentDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the global spam detection configuration
    /// </summary>
    Task<bool> UpdateGlobalConfigAsync(ContentDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the raw chat-specific configuration (without merging with global)
    /// Returns null if no chat-specific config exists
    /// </summary>
    Task<ContentDetectionConfig?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get effective configuration for a specific chat (chat-specific overrides, falls back to global defaults)
    /// </summary>
    Task<ContentDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update configuration for a specific chat
    /// </summary>
    Task<bool> UpdateChatConfigAsync(long chatId, ContentDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all configured chats
    /// </summary>
    Task<IEnumerable<ChatConfigInfo>> GetAllChatConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete configuration for a specific chat (falls back to global)
    /// </summary>
    Task<bool> DeleteChatConfigAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of checks that have AlwaysRun=true for the given chat.
    /// Uses optimized JSONB query to efficiently extract only the needed data.
    /// Returns check names (e.g., "StopWords", "Cas") that are both Enabled and AlwaysRun.
    /// </summary>
    Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default);
}
