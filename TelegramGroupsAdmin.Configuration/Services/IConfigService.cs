using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Service for managing unified configuration storage
/// Supports global and per-chat configuration with automatic merging
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Save a configuration value for a specific config type and chat
    /// </summary>
    /// <typeparam name="T">Configuration type (must be serializable to JSON)</typeparam>
    /// <param name="configType">Type of config (enum for type safety)</param>
    /// <param name="chatId">Chat ID (0 for global config)</param>
    /// <param name="config">Configuration object</param>
    /// <param name="displayName">Optional display name for logging (e.g., chat name)</param>
    Task SaveAsync<T>(ConfigType configType, long chatId, T config, string? displayName = null) where T : class;

    /// <summary>
    /// Get a configuration value for a specific config type and chat
    /// Returns the raw value without merging (null if not set)
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configType">Type of config (enum for type safety)</param>
    /// <param name="chatId">Chat ID (0 for global config)</param>
    /// <returns>Configuration object or null if not found</returns>
    ValueTask<T?> GetAsync<T>(ConfigType configType, long chatId) where T : class;

    /// <summary>
    /// Get effective configuration for a chat by merging global and chat-specific settings
    /// Chat-specific values override global values
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configType">Type of config (enum for type safety)</param>
    /// <param name="chatId">Chat ID (returns global config if 0)</param>
    /// <returns>Merged configuration object or null if no config exists</returns>
    ValueTask<T?> GetEffectiveAsync<T>(ConfigType configType, long chatId) where T : class;

    /// <summary>
    /// Delete a configuration value for a specific config type and chat
    /// </summary>
    /// <param name="configType">Type of config (enum for type safety)</param>
    /// <param name="chatId">Chat ID (0 for global config)</param>
    Task DeleteAsync(ConfigType configType, long chatId);

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
