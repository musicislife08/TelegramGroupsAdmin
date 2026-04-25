using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing configs table (unified configuration storage).
/// Owns mapping → JSON serialization → per-config typed merge → encryption end-to-end.
/// </summary>
public interface IConfigRepository
{
    // ---- Existing anemic methods (REMOVED in commit 7, kept for ConfigService compat) ----
    Task<ConfigRecordDto?> GetAsync(long chatId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default);
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
    Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);
    Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default);
    Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default);
    Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default);

    // ---- New typed reads (no audit, no info logs) ----
    ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default);
    ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default);

    ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default);
    ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default);

    ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default);
    ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default);

    ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default);
    ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default);

    ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);
    ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);

    ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default);
    ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default);

    ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default);
    ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default);

    ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default);
    ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default);

    // ---- New typed mutations (ChatIdentity for log context) ----
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveLogAsync(ChatIdentity chat, LogConfig config, CancellationToken ct = default);
    Task DeleteLogAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, CancellationToken ct = default);
    Task DeleteBotProtectionAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, CancellationToken ct = default);
    Task DeleteTelegramBotAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, CancellationToken ct = default);
    Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, CancellationToken ct = default);
    Task DeleteWarningSystemAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, CancellationToken ct = default);
    Task DeleteInviteCommandAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, CancellationToken ct = default);
    Task DeleteBanCelebrationAsync(ChatIdentity chat, CancellationToken ct = default);

    // ---- Bot token (encrypted, no chat scope) ----
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, CancellationToken ct = default);
}
