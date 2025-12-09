using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing system-wide configuration
/// Handles global configs (API keys, service settings) and per-chat config overrides
/// </summary>
public interface ISystemConfigRepository
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

    /// <summary>
    /// Get Web Push notification configuration (global only - chat_id = NULL)
    /// Configuration stored in configs.web_push_config JSONB column
    /// VAPID private key stored separately in configs.vapid_private_key_encrypted
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Web Push config (never null - returns default if not configured)</returns>
    Task<WebPushConfig> GetWebPushConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save Web Push notification configuration (global only - chat_id = NULL)
    /// </summary>
    /// <param name="config">Web Push configuration to save (non-secret settings only)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveWebPushConfigAsync(WebPushConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get VAPID private key (global only - chat_id = NULL)
    /// Decrypted from configs.vapid_private_key_encrypted column
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted VAPID private key or null if not configured</returns>
    Task<string?> GetVapidPrivateKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save VAPID private key (global only - chat_id = NULL)
    /// Encrypted and stored in configs.vapid_private_key_encrypted column
    /// </summary>
    /// <param name="privateKey">VAPID private key (base64 URL-safe encoded)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveVapidPrivateKeyAsync(string privateKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if VAPID keys are fully configured (both public key in config and private key encrypted)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if both public and private keys are configured</returns>
    Task<bool> HasVapidKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get AI provider configuration (global only - chat_id = NULL)
    /// Multi-provider support: OpenAI, Azure OpenAI, local/OpenAI-compatible endpoints
    /// Configuration stored in configs.ai_provider_config JSONB column
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AI provider config or null if not configured (migration not run yet)</returns>
    Task<AIProviderConfig?> GetAIProviderConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save AI provider configuration (global only - chat_id = NULL)
    /// </summary>
    /// <param name="config">AI provider configuration to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveAIProviderConfigAsync(AIProviderConfig config, CancellationToken cancellationToken = default);
}
