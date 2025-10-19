using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing content check configurations
/// Supports per-chat configuration and "always-run" critical checks
/// </summary>
public interface IContentCheckConfigRepository
{
    /// <summary>
    /// Get all checks marked as always_run=true for a specific chat (or global if chat not found)
    /// Critical checks run for ALL users regardless of trust/admin status
    /// </summary>
    Task<IEnumerable<ContentCheckConfig>> GetCriticalChecksAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get configuration for a specific check in a chat
    /// Returns null if not found
    /// </summary>
    Task<ContentCheckConfig?> GetCheckConfigAsync(long chatId, string checkName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all check configurations for a chat (both chat-specific and global)
    /// Chat-specific configs override global configs
    /// </summary>
    Task<IEnumerable<ContentCheckConfig>> GetAllCheckConfigsAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update or insert a check configuration
    /// </summary>
    Task<ContentCheckConfig> UpsertCheckConfigAsync(ContentCheckConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggle the always_run flag for a specific check
    /// Only allows updating global config (chatId=0)
    /// </summary>
    Task<bool> SetAlwaysRunAsync(string checkName, bool alwaysRun, string modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all global check configurations (chatId=0)
    /// Used by Settings UI to show which checks are critical
    /// </summary>
    Task<IEnumerable<ContentCheckConfig>> GetGlobalConfigsAsync(CancellationToken cancellationToken = default);
}
