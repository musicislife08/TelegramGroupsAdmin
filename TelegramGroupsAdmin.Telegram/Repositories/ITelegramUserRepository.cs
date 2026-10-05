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
    /// Returns one identity per id found (names plus the verdict from the latest profile scan).
    /// Ids with no row are absent from the result.
    /// </summary>
    Task<IReadOnlyList<UserIdentity>> GetIdentitiesAsync(
        IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an observation of a user's names. Creates a minimal inactive row for an unknown user.
    /// For a known user, the names change only when the observation is not older than the stored one
    /// (newest observation wins) and differs from it; a change writes a username_history row and a
    /// ProfileChange audit row in the same transaction. A row lock serializes concurrent observers so
    /// the same rename is recorded once. <see cref="UiModels.ObservedNamesResult.Renamed"/> holds the
    /// previous names only for the call that changed them.
    /// </summary>
    Task<UiModels.ObservedNamesResult> GetOrUpdateAsync(
        UiModels.ObservedUser observed, UiModels.ProfileChangeContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the user active and moves last_seen_at forward to <paramref name="seenAt"/> (an older
    /// update processed late never moves it back). Names are untouched; they change only via
    /// <see cref="GetOrUpdateAsync"/>.
    /// </summary>
    Task MarkActiveAsync(long telegramUserId, DateTimeOffset seenAt, CancellationToken cancellationToken = default);

    Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken cancellationToken = default);
    Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken cancellationToken = default);
    Task TrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UntrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task EnableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task DisableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<List<long>> GetTrustedUserIdsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken cancellationToken = default);
    Task<UiModels.ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default);

    // ============================================================================
    // Paginated Methods (server-side pagination for Users page)
    // ============================================================================

    /// <summary>
    /// Get a page of users filtered by tab, search text, and accessible chats.
    /// Stats (ChatCount, WarningCount, etc.) are enriched only for the returned page.
    /// </summary>
    Task<(List<UiModels.TelegramUserListItem> Items, int TotalCount)> GetPagedUsersAsync(
        UiModels.UserListFilter filter, int skip, int take,
        string? searchText, List<long>? chatIds,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a page of banned users with full ban details (date, issuer, reason, expiry).
    /// </summary>
    Task<(List<UiModels.BannedUserListItem> Items, int TotalCount)> GetPagedBannedUsersWithDetailsAsync(
        int skip, int take, string? searchText,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get counts for all user tabs in a single round-trip (5 parallel COUNT queries).
    /// </summary>
    Task<UiModels.UserTabCounts> GetUserTabCountsAsync(
        List<long>? chatIds, string? searchText,
        CancellationToken cancellationToken = default);
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
    /// Increment user's all-time kick count by 1. Returns rows affected (0 = unknown user, 1 = success).
    /// Source of truth for kick escalation logic (not derived from audit log).
    /// </summary>
    Task<int> IncrementKickCountAsync(UserIdentity user, CancellationToken cancellationToken = default);

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
    /// Exclude a user from automatic profile scans (renames still scan).
    /// The admin's switch (UserDetailDialog); scans never set it.
    /// </summary>
    Task ExcludeFromProfileScanAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Include a user in automatic profile scans again.
    /// The admin's switch (UserDetailDialog); scans never clear it.
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

    /// <summary>
    /// Record a name-only scan: set ProfileScanScore and bump ProfileScannedAt + UpdatedAt.
    /// Stored bio, channel, story and photo fields are left as they are.
    /// </summary>
    Task UpdateProfileScanScoreAsync(long telegramUserId, decimal score, CancellationToken cancellationToken = default);
}
