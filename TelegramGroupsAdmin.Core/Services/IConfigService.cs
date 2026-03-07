using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for managing unified configuration storage
/// Supports global and per-chat configuration with automatic merging
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Save a configuration value for a specific config type and chat.
    /// Pass ChatIdentity.FromId(0) for global config.
    /// </summary>
    Task SaveAsync<T>(ConfigType configType, ChatIdentity chat, T config) where T : class;

    /// <summary>
    /// Get a configuration value for a specific config type and chat.
    /// Returns the raw value without merging (null if not set).
    /// </summary>
    ValueTask<T?> GetAsync<T>(ConfigType configType, long chatId) where T : class;

    /// <summary>
    /// Get effective configuration for a chat by merging global and chat-specific settings.
    /// Chat-specific values override global values.
    /// </summary>
    ValueTask<T?> GetEffectiveAsync<T>(ConfigType configType, long chatId) where T : class;

    /// <summary>
    /// Delete a configuration value for a specific config type and chat.
    /// Pass ChatIdentity.FromId(0) for global config.
    /// </summary>
    Task DeleteAsync(ConfigType configType, ChatIdentity chat);

    /// <summary>
    /// Get the encrypted Telegram bot token from database (global config only, chat_id = 0)
    /// Returns decrypted token or null if not configured
    /// </summary>
    ValueTask<string?> GetTelegramBotTokenAsync();

    /// <summary>
    /// Save the Telegram bot token to database (encrypted, global config only, chat_id = 0)
    /// </summary>
    Task SaveTelegramBotTokenAsync(string botToken);

    /// <summary>
    /// Get all content detection chat configurations (for admin UI listing).
    /// Returns metadata about which chats have custom configs.
    /// </summary>
    Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of content detection checks that have AlwaysRun=true for the given chat.
    /// Uses optimized JSONB query to efficiently extract only critical check names.
    /// Handles UseGlobal merging at the database level.
    /// </summary>
    Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default);
}
