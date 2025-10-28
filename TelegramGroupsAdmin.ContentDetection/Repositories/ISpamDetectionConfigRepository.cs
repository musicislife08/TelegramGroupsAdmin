using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Configuration;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing spam detection configurations
/// </summary>
public interface ISpamDetectionConfigRepository
{
    /// <summary>
    /// Get the global spam detection configuration
    /// </summary>
    Task<SpamDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the global spam detection configuration
    /// </summary>
    Task<bool> UpdateGlobalConfigAsync(SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the raw chat-specific configuration (without merging with global)
    /// Returns null if no chat-specific config exists
    /// </summary>
    Task<SpamDetectionConfig?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get effective configuration for a specific chat (chat-specific overrides, falls back to global defaults)
    /// </summary>
    Task<SpamDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update configuration for a specific chat
    /// </summary>
    Task<bool> UpdateChatConfigAsync(long chatId, SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all configured chats
    /// </summary>
    Task<IEnumerable<ChatConfigInfo>> GetAllChatConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete configuration for a specific chat (falls back to global)
    /// </summary>
    Task<bool> DeleteChatConfigAsync(long chatId, CancellationToken cancellationToken = default);
}