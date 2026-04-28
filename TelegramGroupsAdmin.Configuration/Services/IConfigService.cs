using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Configuration.Services;

// BanCelebrationConfig, ServiceMessageDeletionConfig, WarningSystemConfig live in
// TelegramGroupsAdmin.Configuration root namespace (a parent of this one), so are accessible without an extra using.

/// <summary>
/// Typed configuration service. Reads use long chatId; mutations require ChatIdentity
/// for log context plus an Actor for audit attribution.
/// </summary>
public interface IConfigService
{
    // --- Reads ---
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

    /// <summary>Get the per-chat or global ContentDetection config. chatId == 0 returns the global config (always present); chatId > 0 returns the chat-specific override or null if none configured.</summary>
    ValueTask<ContentDetectionConfig?> GetContentDetectionAsync(long chatId, CancellationToken ct = default);

    /// <summary>Get the effective ContentDetection config for a chat, with global fallback. Always returns a config — the global root is treated as a bootstrap invariant.</summary>
    ValueTask<ContentDetectionConfig> GetEffectiveContentDetectionAsync(long chatId, CancellationToken ct = default);

    // --- Mutations ---
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveLogAsync(ChatIdentity chat, LogConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteLogAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteBotProtectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteTelegramBotAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteWarningSystemAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteInviteCommandAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteBanCelebrationAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    /// <summary>Save ContentDetection config (chat or global), emit audit, and invalidate cache.</summary>
    Task SaveContentDetectionAsync(ChatIdentity chat, ContentDetectionConfig config, Actor initiator, CancellationToken ct = default);

    /// <summary>Delete the per-chat ContentDetection config, emit audit, and invalidate cache.</summary>
    Task DeleteContentDetectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    // --- Bot token ---
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, Actor initiator, CancellationToken ct = default);

    // --- ContentDetection helpers (delegate to IContentDetectionConfigRepository, retained) ---
    Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default);
}
