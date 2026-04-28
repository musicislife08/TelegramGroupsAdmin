using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Typed configuration service: caches reads via HybridCache, emits audit events
/// on mutations, delegates all data-layer work (mapping, JSON, encryption, merge)
/// to IConfigRepository.
/// </summary>
public class ConfigService(
    IConfigRepository repository,
    IContentDetectionConfigRepository contentDetectionRepository,
    IAuditService auditService,
    HybridCache cache,
    ILogger<ConfigService> logger) : IConfigService
{
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(15) };

    // ============================================================================
    // Welcome
    // ============================================================================

    public ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_welcome_{chatId}",
            async _ => await repository.GetWelcomeAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_welcome_{chatId}",
            async _ => await repository.GetEffectiveWelcomeAsync(chatId, ct),
            CacheOptions, tags: ["effective_welcome"], cancellationToken: ct);

    public async Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveWelcomeAsync(chat, config, ct);
        await EmitAuditAsync("Welcome", chat, initiator, ct);
        await InvalidateAsync("welcome", chat.Id, ct);
        logger.LogInformation("Welcome config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteWelcomeAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteWelcomeAsync(chat, ct);
        await EmitAuditAsync("Welcome (deleted)", chat, initiator, ct);
        await InvalidateAsync("welcome", chat.Id, ct);
        logger.LogInformation("Welcome config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // Log
    // ============================================================================

    public ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_log_{chatId}",
            async _ => await repository.GetLogAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_log_{chatId}",
            async _ => await repository.GetEffectiveLogAsync(chatId, ct),
            CacheOptions, tags: ["effective_log"], cancellationToken: ct);

    public async Task SaveLogAsync(ChatIdentity chat, LogConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveLogAsync(chat, config, ct);
        await EmitAuditAsync("Log", chat, initiator, ct);
        await InvalidateAsync("log", chat.Id, ct);
        logger.LogInformation("Log config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteLogAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteLogAsync(chat, ct);
        await EmitAuditAsync("Log (deleted)", chat, initiator, ct);
        await InvalidateAsync("log", chat.Id, ct);
        logger.LogInformation("Log config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // BotProtection
    // ============================================================================

    public ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_bot_protection_{chatId}",
            async _ => await repository.GetBotProtectionAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_bot_protection_{chatId}",
            async _ => await repository.GetEffectiveBotProtectionAsync(chatId, ct),
            CacheOptions, tags: ["effective_bot_protection"], cancellationToken: ct);

    public async Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveBotProtectionAsync(chat, config, ct);
        await EmitAuditAsync("BotProtection", chat, initiator, ct);
        await InvalidateAsync("bot_protection", chat.Id, ct);
        logger.LogInformation("BotProtection config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteBotProtectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteBotProtectionAsync(chat, ct);
        await EmitAuditAsync("BotProtection (deleted)", chat, initiator, ct);
        await InvalidateAsync("bot_protection", chat.Id, ct);
        logger.LogInformation("BotProtection config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // TelegramBot
    // ============================================================================

    public ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_telegram_bot_{chatId}",
            async _ => await repository.GetTelegramBotAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_telegram_bot_{chatId}",
            async _ => await repository.GetEffectiveTelegramBotAsync(chatId, ct),
            CacheOptions, tags: ["effective_telegram_bot"], cancellationToken: ct);

    public async Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveTelegramBotAsync(chat, config, ct);
        await EmitAuditAsync("TelegramBot", chat, initiator, ct);
        await InvalidateAsync("telegram_bot", chat.Id, ct);
        logger.LogInformation("TelegramBot config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteTelegramBotAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteTelegramBotAsync(chat, ct);
        await EmitAuditAsync("TelegramBot (deleted)", chat, initiator, ct);
        await InvalidateAsync("telegram_bot", chat.Id, ct);
        logger.LogInformation("TelegramBot config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // ServiceMessageDeletion
    // ============================================================================

    public ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_service_message_deletion_{chatId}",
            async _ => await repository.GetServiceMessageDeletionAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_service_message_deletion_{chatId}",
            async _ => await repository.GetEffectiveServiceMessageDeletionAsync(chatId, ct),
            CacheOptions, tags: ["effective_service_message_deletion"], cancellationToken: ct);

    public async Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveServiceMessageDeletionAsync(chat, config, ct);
        await EmitAuditAsync("ServiceMessageDeletion", chat, initiator, ct);
        await InvalidateAsync("service_message_deletion", chat.Id, ct);
        logger.LogInformation("ServiceMessageDeletion config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteServiceMessageDeletionAsync(chat, ct);
        await EmitAuditAsync("ServiceMessageDeletion (deleted)", chat, initiator, ct);
        await InvalidateAsync("service_message_deletion", chat.Id, ct);
        logger.LogInformation("ServiceMessageDeletion config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // WarningSystem
    // ============================================================================

    public ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_warning_system_{chatId}",
            async _ => await repository.GetWarningSystemAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_warning_system_{chatId}",
            async _ => await repository.GetEffectiveWarningSystemAsync(chatId, ct),
            CacheOptions, tags: ["effective_warning_system"], cancellationToken: ct);

    public async Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveWarningSystemAsync(chat, config, ct);
        await EmitAuditAsync("WarningSystem", chat, initiator, ct);
        await InvalidateAsync("warning_system", chat.Id, ct);
        logger.LogInformation("WarningSystem config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteWarningSystemAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteWarningSystemAsync(chat, ct);
        await EmitAuditAsync("WarningSystem (deleted)", chat, initiator, ct);
        await InvalidateAsync("warning_system", chat.Id, ct);
        logger.LogInformation("WarningSystem config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // InviteCommand
    // ============================================================================

    public ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_invite_command_{chatId}",
            async _ => await repository.GetInviteCommandAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_invite_command_{chatId}",
            async _ => await repository.GetEffectiveInviteCommandAsync(chatId, ct),
            CacheOptions, tags: ["effective_invite_command"], cancellationToken: ct);

    public async Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveInviteCommandAsync(chat, config, ct);
        await EmitAuditAsync("InviteCommand", chat, initiator, ct);
        await InvalidateAsync("invite_command", chat.Id, ct);
        logger.LogInformation("InviteCommand config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteInviteCommandAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteInviteCommandAsync(chat, ct);
        await EmitAuditAsync("InviteCommand (deleted)", chat, initiator, ct);
        await InvalidateAsync("invite_command", chat.Id, ct);
        logger.LogInformation("InviteCommand config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // BanCelebration
    // ============================================================================

    public ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_ban_celebration_{chatId}",
            async _ => await repository.GetBanCelebrationAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_ban_celebration_{chatId}",
            async _ => await repository.GetEffectiveBanCelebrationAsync(chatId, ct),
            CacheOptions, tags: ["effective_ban_celebration"], cancellationToken: ct);

    public async Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveBanCelebrationAsync(chat, config, ct);
        await EmitAuditAsync("BanCelebration", chat, initiator, ct);
        await InvalidateAsync("ban_celebration", chat.Id, ct);
        logger.LogInformation("BanCelebration config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteBanCelebrationAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteBanCelebrationAsync(chat, ct);
        await EmitAuditAsync("BanCelebration (deleted)", chat, initiator, ct);
        await InvalidateAsync("ban_celebration", chat.Id, ct);
        logger.LogInformation("BanCelebration config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // ContentDetection
    // ============================================================================

    public ValueTask<ContentDetectionConfig?> GetContentDetectionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_content_detection_{chatId}",
            async factoryCt => chatId == 0
                ? (ContentDetectionConfig?)await contentDetectionRepository.GetGlobalConfigAsync(factoryCt)
                : await contentDetectionRepository.GetByChatIdAsync(chatId, factoryCt),
            CacheOptions, cancellationToken: ct);

    public ValueTask<ContentDetectionConfig?> GetEffectiveContentDetectionAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_content_detection_{chatId}",
            async factoryCt => (ContentDetectionConfig?)await contentDetectionRepository.GetEffectiveConfigAsync(chatId, factoryCt),
            CacheOptions, tags: ["effective_content_detection"], cancellationToken: ct);

    public async Task SaveContentDetectionAsync(ChatIdentity chat, ContentDetectionConfig config, Actor initiator, CancellationToken ct = default)
    {
        if (chat.Id == 0)
            await contentDetectionRepository.UpdateGlobalConfigAsync(config, cancellationToken: ct);
        else
            await contentDetectionRepository.UpdateChatConfigAsync(chat.Id, config, cancellationToken: ct);
        await EmitAuditAsync("ContentDetection", chat, initiator, ct);
        await InvalidateAsync("content_detection", chat.Id, ct);
        logger.LogInformation("ContentDetection config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    public async Task DeleteContentDetectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await contentDetectionRepository.DeleteChatConfigAsync(chat.Id, ct);
        await EmitAuditAsync("ContentDetection (deleted)", chat, initiator, ct);
        await InvalidateAsync("content_detection", chat.Id, ct);
        logger.LogInformation("ContentDetection config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
    }

    // ============================================================================
    // Bot token
    // ============================================================================

    public ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync("cfg_bot_token",
            async _ => await repository.GetBotTokenAsync(ct),
            CacheOptions, cancellationToken: ct);

    public async Task SaveBotTokenAsync(string botToken, Actor initiator, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);
        await repository.SaveBotTokenAsync(botToken, ct);
        // Audit value MUST NOT contain plaintext token — only the config name.
        await auditService.LogEventAsync(AuditEventType.ConfigurationChanged, initiator, target: null, value: "TelegramBotToken", ct);
        await cache.RemoveAsync("cfg_bot_token", ct);
        logger.LogInformation("Telegram bot token saved by {Actor}", initiator.DisplayName);
    }

    // ============================================================================
    // ContentDetection helper delegates (retained)
    // ============================================================================

    public Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default)
        => contentDetectionRepository.GetAllChatConfigsAsync(cancellationToken);

    public Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default)
        => contentDetectionRepository.GetCriticalCheckNamesAsync(chatId, cancellationToken);

    // ============================================================================
    // Helpers
    // ============================================================================

    private Task EmitAuditAsync(string configName, ChatIdentity chat, Actor initiator, CancellationToken ct)
        => auditService.LogEventAsync(
            AuditEventType.ConfigurationChanged,
            initiator,
            target: null,
            value: $"{configName} ({chat.DisplayName})",
            ct);

    private async Task InvalidateAsync(string keyPrefix, long chatId, CancellationToken ct)
    {
        await cache.RemoveAsync($"cfg_{keyPrefix}_{chatId}", ct);
        if (chatId != 0)
            await cache.RemoveAsync($"cfg_effective_{keyPrefix}_{chatId}", ct);
        else
            await cache.RemoveByTagAsync($"effective_{keyPrefix}", ct);
    }
}
