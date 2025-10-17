using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository interface for managing user tags
/// </summary>
public interface IUserTagsRepository
{
    /// <summary>
    /// Get all tags for a specific Telegram user
    /// </summary>
    Task<List<UserTag>> GetTagsByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific tag by ID
    /// </summary>
    Task<UserTag?> GetTagByIdAsync(long tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new tag to a user
    /// </summary>
    Task<long> AddTagAsync(UserTag tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a tag
    /// </summary>
    Task<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all users with a specific tag type
    /// </summary>
    Task<List<long>> GetUserIdsByTagTypeAsync(TagType tagType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user has a specific tag type
    /// </summary>
    Task<bool> UserHasTagAsync(long telegramUserId, TagType tagType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the confidence modifier for a user based on their tags
    /// </summary>
    Task<int> GetTotalConfidenceModifierAsync(long telegramUserId, CancellationToken cancellationToken = default);
}
