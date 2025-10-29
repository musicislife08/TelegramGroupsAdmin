using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing OpenAI custom prompt versions
/// Phase 4.X: Prompt builder with AI-generated prompts and version control
/// </summary>
public interface IPromptVersionRepository
{
    /// <summary>
    /// Get all prompt versions for a chat, ordered by version desc (newest first)
    /// </summary>
    Task<List<PromptVersion>> GetVersionHistoryByChatIdAsync(
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the currently active prompt version for a chat
    /// </summary>
    Task<PromptVersion?> GetActiveVersionAsync(
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific prompt version by ID
    /// </summary>
    Task<PromptVersion?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new prompt version (auto-increments version number)
    /// Deactivates previous active version
    /// </summary>
    Task<PromptVersion> CreateVersionAsync(
        long chatId,
        string promptText,
        string? createdBy,
        string? generationMetadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore (activate) an older prompt version
    /// Deactivates current active version
    /// </summary>
    Task<PromptVersion> RestoreVersionAsync(
        long versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific prompt version (cannot delete active version)
    /// </summary>
    Task<bool> DeleteVersionAsync(
        long versionId,
        CancellationToken cancellationToken = default);
}
