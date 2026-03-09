using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for Telegram user operations
/// </summary>
public interface ITelegramUserRepository
{
    Task<UiModels.TelegramUser?> GetByTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<UiModels.TelegramUser?> GetByIdAsync(long telegramUserId, CancellationToken cancellationToken = default); // Alias for GetByTelegramIdAsync

    /// <summary>
    /// Returns the existing user if found, or creates a minimal inactive record.
    /// The returned object reflects current DB state (including IsBanned, IsTrusted, etc.).
    /// </summary>
    Task<UiModels.TelegramUser> GetOrCreateAsync(
        long telegramUserId, string? username, string? firstName, string? lastName, bool isBot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple Telegram users by their Telegram IDs in a single query.
    /// Used for batch hydration to avoid N+1 query patterns.
    /// </summary>
    Task<List<UiModels.TelegramUser>> GetByTelegramIdsAsync(IEnumerable<long> telegramIds, CancellationToken cancellationToken = default);
    Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UpsertAsync(UiModels.TelegramUser user, CancellationToken cancellationToken = default);
    Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken cancellationToken = default);
    Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken cancellationToken = default);
    Task TrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UntrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task EnableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task DisableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<List<long>> GetTrustedUserIdsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(List<long> chatIds, CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default);
    Task<UiModels.ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default);
    Task<UiModels.TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken cancellationToken = default);

    // ============================================================================
    // Moderation State Methods (REFACTOR-5: Source of truth on telegram_users)
    // ============================================================================

    /// <summary>
    /// Set user's ban status. Source of truth for "is user banned?".
    /// </summary>
    /// <param name="telegramUserId">User to update</param>
    /// <param name="isBanned">Whether user is banned</param>
    /// <param name="expiresAt">When ban expires (null = permanent)</param>
    Task SetBanStatusAsync(long telegramUserId, bool isBanned, DateTimeOffset? expiresAt = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a warning to user's JSONB warnings collection.
    /// Returns the count of active (non-expired) warnings after insert.
    /// </summary>
    Task<int> AddWarningAsync(long telegramUserId, WarningEntry warning, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of active (non-expired) warnings for a user.
    /// </summary>
    Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is currently banned (source of truth).
    /// </summary>
    Task<bool> IsBannedAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is trusted (source of truth: telegram_users.is_trusted).
    /// </summary>
    Task<bool> IsTrustedAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment user's all-time kick count by 1. Returns the updated count.
    /// Source of truth for kick escalation logic (not derived from audit log).
    /// </summary>
    Task<int> IncrementKickCountAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's all-time kick count for escalation decisions.
    /// </summary>
    Task<int> GetKickCountAsync(long telegramUserId, CancellationToken cancellationToken = default);

    // ============================================================================
    // IsActive Methods (Phase: /ban @username support)
    // ============================================================================

    /// <summary>
    /// Get user by username (case-insensitive, without @ prefix).
    /// Returns only active users by default.
    /// </summary>
    Task<UiModels.TelegramUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark user as active (completed welcome flow or sent a message).
    /// </summary>
    Task ActivateAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search users by name (fuzzy contains match on combined "first last" and username).
    /// Searches ALL users (active and inactive) - used by ban command to find timeout users.
    /// </summary>
    Task<List<UiModels.TelegramUser>> SearchByNameAsync(string searchText, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get inactive users (users who joined but never engaged).
    /// Used by the Inactive Users UI tab.
    /// </summary>
    Task<List<UiModels.TelegramUserListItem>> GetInactiveUsersAsync(CancellationToken cancellationToken = default);

    // ============================================================================
    // Profile Scan Methods
    // ============================================================================

    /// <summary>
    /// Get the most recently active chat for a user (by message activity).
    /// Returns null if the user has no message history in any managed chat.
    /// Used by the profile rescan job to associate alerts with a real chat.
    /// </summary>
    Task<ChatIdentity?> GetFirstChatForUserAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exclude a user from automatic profile re-scans.
    /// Set when the user cannot be resolved via Telegram API (likely deleted account).
    /// </summary>
    Task ExcludeFromProfileScanAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Include a user in automatic profile re-scans.
    /// Cleared when a manual rescan successfully resolves the user.
    /// </summary>
    Task IncludeInProfileScanAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user IDs eligible for periodic profile re-scanning.
    /// Filters out banned/bot/trusted/excluded users and returns those with stale or missing scans.
    /// Ordered by ProfileScannedAt ASC (NULLS FIRST = never-scanned users prioritized).
    /// </summary>
    Task<List<long>> GetEligibleUsersForRescanAsync(int batchSize, DateTimeOffset rescanCutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically update all profile scan columns for a user.
    /// Called after a User API profile scan completes.
    /// </summary>
    Task UpdateProfileScanDataAsync(
        long telegramUserId,
        string? bio,
        long? personalChannelId,
        string? personalChannelTitle,
        string? personalChannelAbout,
        bool hasPinnedStories,
        string? pinnedStoryCaptions,
        bool isScam,
        bool isFake,
        bool isVerified,
        decimal profileScanScore,
        long? profilePhotoId,
        long? personalChannelPhotoId,
        string? pinnedStoryIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bump ProfileScannedAt + UpdatedAt without changing any other fields.
    /// Used when diff detection finds no profile changes — marks the user as freshly scanned.
    /// </summary>
    Task UpdateProfileScannedAtAsync(long telegramUserId, CancellationToken cancellationToken = default);
}
