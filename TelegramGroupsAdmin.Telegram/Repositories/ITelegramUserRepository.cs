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
    Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UpsertAsync(UiModels.TelegramUser user, CancellationToken cancellationToken = default);
    Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken cancellationToken = default);
    Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken cancellationToken = default);
    Task UpdateTrustStatusAsync(long telegramUserId, bool isTrusted, CancellationToken cancellationToken = default);
    Task SetBotDmEnabledAsync(long telegramUserId, bool enabled, CancellationToken cancellationToken = default);
    Task<List<long>> GetTrustedUserIdsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(List<long> chatIds, CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.TopActiveUser>> GetTopActiveUsersAsync(
        int limit = 3,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        List<long>? chatIds = null,
        CancellationToken cancellationToken = default);
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
}
