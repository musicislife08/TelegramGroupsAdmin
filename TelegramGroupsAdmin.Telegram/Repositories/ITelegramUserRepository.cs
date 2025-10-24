using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for Telegram user operations
/// </summary>
public interface ITelegramUserRepository
{
    Task<UiModels.TelegramUser?> GetByTelegramIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<UiModels.TelegramUser?> GetByIdAsync(long telegramUserId, CancellationToken ct = default); // Alias for GetByTelegramIdAsync
    Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken ct = default);
    Task UpsertAsync(UiModels.TelegramUser user, CancellationToken ct = default);
    Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken ct = default);
    Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken ct = default);
    Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken ct = default);
    Task UpdateTrustStatusAsync(long telegramUserId, bool isTrusted, CancellationToken ct = default);
    Task SetBotDmEnabledAsync(long telegramUserId, bool enabled, CancellationToken ct = default);
    Task<List<long>> GetTrustedUserIdsAsync(CancellationToken ct = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken ct = default);
    Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(List<long> chatIds, CancellationToken ct = default);
    Task<List<UiModels.TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken ct = default);
    Task<List<UiModels.TelegramUserListItem>> GetBannedUsersAsync(CancellationToken ct = default);
    Task<List<UiModels.BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken ct = default);
    Task<List<UiModels.TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken ct = default);
    Task<List<UiModels.TopActiveUser>> GetTopActiveUsersAsync(int limit = 3, CancellationToken ct = default);
    Task<UiModels.ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken ct = default);
    Task<UiModels.TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken ct = default);
}
