namespace TelegramGroupsAdmin.Services;

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
    /// <param name="configType">Type of config (e.g., "spam_detection", "welcome", "log")</param>
    /// <param name="chatId">Chat ID (null for global config)</param>
    /// <param name="config">Configuration object</param>
    Task SaveAsync<T>(string configType, long? chatId, T config) where T : class;

    /// <summary>
    /// Get a configuration value for a specific config type and chat
    /// Returns the raw value without merging (null if not set)
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configType">Type of config</param>
    /// <param name="chatId">Chat ID (null for global config)</param>
    /// <returns>Configuration object or null if not found</returns>
    Task<T?> GetAsync<T>(string configType, long? chatId) where T : class;

    /// <summary>
    /// Get effective configuration for a chat by merging global and chat-specific settings
    /// Chat-specific values override global values
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configType">Type of config</param>
    /// <param name="chatId">Chat ID (returns global config if null)</param>
    /// <returns>Merged configuration object or null if no config exists</returns>
    Task<T?> GetEffectiveAsync<T>(string configType, long? chatId) where T : class;

    /// <summary>
    /// Delete a configuration value for a specific config type and chat
    /// </summary>
    /// <param name="configType">Type of config</param>
    /// <param name="chatId">Chat ID (null for global config)</param>
    Task DeleteAsync(string configType, long? chatId);
}
