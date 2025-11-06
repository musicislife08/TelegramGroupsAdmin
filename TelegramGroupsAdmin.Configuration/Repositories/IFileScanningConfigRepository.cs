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

    /// <summary>
    /// Get API keys for external services (global only - chat_id = NULL)
    /// Keys are stored encrypted in configs.api_keys JSONB column using Data Protection
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>API keys or null if not configured</returns>
    Task<ApiKeysConfig?> GetApiKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save API keys for external services (global only - chat_id = NULL)
    /// Keys are automatically encrypted using Data Protection before storing
    /// </summary>
    /// <param name="apiKeys">API keys to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveApiKeysAsync(ApiKeysConfig apiKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get OpenAI service configuration (global only - chat_id = NULL)
    /// Configuration stored in configs.openai_config JSONB column
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>OpenAI config or default if not configured</returns>
    Task<OpenAIConfig?> GetOpenAIConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save OpenAI service configuration (global only - chat_id = NULL)
    /// </summary>
    /// <param name="config">OpenAI configuration to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveOpenAIConfigAsync(OpenAIConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get SendGrid email service configuration (global only - chat_id = NULL)
    /// Configuration stored in configs.sendgrid_config JSONB column
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SendGrid config or default if not configured</returns>
    Task<SendGridConfig?> GetSendGridConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save SendGrid email service configuration (global only - chat_id = NULL)
    /// </summary>
    /// <param name="config">SendGrid configuration to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveSendGridConfigAsync(SendGridConfig config, CancellationToken cancellationToken = default);
}
