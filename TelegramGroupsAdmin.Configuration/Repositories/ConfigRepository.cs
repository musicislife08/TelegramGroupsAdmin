using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Repositories;

public class ConfigRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigRepository> logger) : IConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async ValueTask<string?> GetInviteLinkAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.InviteLink)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, ChatConfigPresenceFlags>> GetMultiplexedConfigPresenceAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId != 0)
            .Select(c => new
            {
                c.ChatId,
                HasWelcome = c.WelcomeConfig != null,
                HasServiceMessageDeletion = c.ServiceMessageDeletionConfig != null,
                HasBanCelebration = c.BanCelebrationConfig != null
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.ChatId,
            r => new ChatConfigPresenceFlags(r.HasWelcome, r.HasServiceMessageDeletion, r.HasBanCelebration));
    }

    public async Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (existing != null)
        {
            existing.InviteLink = inviteLink;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            await context.Configs.AddAsync(new ConfigRecordDto
            {
                ChatId = chatId,
                InviteLink = inviteLink,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (existing != null)
        {
            existing.InviteLink = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var configsWithLinks = await context.Configs
            .Where(c => c.InviteLink != null)
            .ToListAsync(cancellationToken);

        foreach (var config in configsWithLinks)
        {
            config.InviteLink = null;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (configsWithLinks.Any())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    // ============================================================================
    // Welcome
    // ============================================================================

    public async Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.WelcomeConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved Welcome config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.WelcomeConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeWelcome(json, scope: $"chat {chatId}");
    }

    public async Task DeleteWelcomeAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.WelcomeConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted Welcome config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.WelcomeConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.WelcomeConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.WelcomeConfig;

        var globalModel = DeserializeWelcome(globalJson, scope: "global");
        var chatModel = DeserializeWelcome(chatJson, scope: $"chat {chatId}");

        return MergeWelcome(globalModel, chatModel);
    }

    private WelcomeConfig? DeserializeWelcome(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<WelcomeConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize Welcome config for {Scope}", scope);
            return null;
        }
    }

    // Merge contract: when the chat row exists, every non-nullable scalar field on the chat
    // row wins outright (a chat-explicit `false` or `0` is an *override*, not "no opinion").
    // Strings fall back to the global only when the chat field is empty, since the UI uses
    // empty-string to express "inherit." Nullable refs fall back when the chat field is null.
    // Non-nullable nested refs (JoinSecurity, TrustedBypass) wholesale-replace.
    internal static WelcomeConfig? MergeWelcome(WelcomeConfig? global, WelcomeConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new WelcomeConfig
        {
            Enabled = chat.Enabled,
            Mode = chat.Mode,
            TimeoutSeconds = chat.TimeoutSeconds,
            MaxKicksBeforeBan = chat.MaxKicksBeforeBan,
            MainWelcomeMessage = !string.IsNullOrEmpty(chat.MainWelcomeMessage) ? chat.MainWelcomeMessage : global.MainWelcomeMessage,
            DmChatTeaserMessage = !string.IsNullOrEmpty(chat.DmChatTeaserMessage) ? chat.DmChatTeaserMessage : global.DmChatTeaserMessage,
            AcceptButtonText = !string.IsNullOrEmpty(chat.AcceptButtonText) ? chat.AcceptButtonText : global.AcceptButtonText,
            DenyButtonText = !string.IsNullOrEmpty(chat.DenyButtonText) ? chat.DenyButtonText : global.DenyButtonText,
            DmButtonText = !string.IsNullOrEmpty(chat.DmButtonText) ? chat.DmButtonText : global.DmButtonText,
            ExamConfig = chat.ExamConfig ?? global.ExamConfig,
            JoinSecurity = chat.JoinSecurity,
            TrustedBypass = chat.TrustedBypass
        };
    }

    // ============================================================================
    // Log
    // ============================================================================

    public async Task SaveLogAsync(ChatIdentity chat, LogConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.LogConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved Log config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.LogConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeLog(json, scope: $"chat {chatId}");
    }

    public async Task DeleteLogAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.LogConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted Log config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.LogConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.LogConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.LogConfig;

        var globalModel = DeserializeLog(globalJson, scope: "global");
        var chatModel = DeserializeLog(chatJson, scope: $"chat {chatId}");

        return MergeLog(globalModel, chatModel);
    }

    private LogConfig? DeserializeLog(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<LogConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize Log config for {Scope}", scope);
            return null;
        }
    }

    internal static LogConfig? MergeLog(LogConfig? global, LogConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        // Merge per-namespace overrides: start with global, layer chat on top.
        var mergedOverrides = new Dictionary<string, Microsoft.Extensions.Logging.LogLevel>(global.Overrides);
        foreach (var kv in chat.Overrides)
        {
            mergedOverrides[kv.Key] = kv.Value;
        }

        return new LogConfig
        {
            DefaultLevel = chat.DefaultLevel,
            Overrides = mergedOverrides,
            LastModified = chat.LastModified > global.LastModified ? chat.LastModified : global.LastModified
        };
    }

    // ============================================================================
    // BotProtection
    // ============================================================================

    public async Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.BotProtectionConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved BotProtection config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.BotProtectionConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeBotProtection(json, scope: $"chat {chatId}");
    }

    public async Task DeleteBotProtectionAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.BotProtectionConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted BotProtection config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.BotProtectionConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.BotProtectionConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.BotProtectionConfig;

        var globalModel = DeserializeBotProtection(globalJson, scope: "global");
        var chatModel = DeserializeBotProtection(chatJson, scope: $"chat {chatId}");

        return MergeBotProtection(globalModel, chatModel);
    }

    private BotProtectionConfig? DeserializeBotProtection(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<BotProtectionConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize BotProtection config for {Scope}", scope);
            return null;
        }
    }

    internal static BotProtectionConfig? MergeBotProtection(BotProtectionConfig? global, BotProtectionConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new BotProtectionConfig
        {
            Enabled = chat.Enabled,
            AutoBanBots = chat.AutoBanBots,
            AllowAdminInvitedBots = chat.AllowAdminInvitedBots,
            // List override: chat list (if any) wins; otherwise inherit global's list.
            WhitelistedBots = chat.WhitelistedBots is { Count: > 0 } ? chat.WhitelistedBots : global.WhitelistedBots,
            LogBotEvents = chat.LogBotEvents
        };
    }

    // ============================================================================
    // TelegramBot
    // ============================================================================

    public async Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.TelegramBotConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved TelegramBot config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.TelegramBotConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeTelegramBot(json, scope: $"chat {chatId}");
    }

    public async Task DeleteTelegramBotAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.TelegramBotConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted TelegramBot config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.TelegramBotConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.TelegramBotConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.TelegramBotConfig;

        var globalModel = DeserializeTelegramBot(globalJson, scope: "global");
        var chatModel = DeserializeTelegramBot(chatJson, scope: $"chat {chatId}");

        return MergeTelegramBot(globalModel, chatModel);
    }

    private TelegramBotConfig? DeserializeTelegramBot(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TelegramBotConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize TelegramBot config for {Scope}", scope);
            return null;
        }
    }

    internal static TelegramBotConfig? MergeTelegramBot(TelegramBotConfig? global, TelegramBotConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new TelegramBotConfig
        {
            BotEnabled = chat.BotEnabled
        };
    }

    // ============================================================================
    // ServiceMessageDeletion
    // ============================================================================

    public async Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.ServiceMessageDeletionConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved ServiceMessageDeletion config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.ServiceMessageDeletionConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeServiceMessageDeletion(json, scope: $"chat {chatId}");
    }

    public async Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.ServiceMessageDeletionConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted ServiceMessageDeletion config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.ServiceMessageDeletionConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.ServiceMessageDeletionConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.ServiceMessageDeletionConfig;

        var globalModel = DeserializeServiceMessageDeletion(globalJson, scope: "global");
        var chatModel = DeserializeServiceMessageDeletion(chatJson, scope: $"chat {chatId}");

        return MergeServiceMessageDeletion(globalModel, chatModel);
    }

    private ServiceMessageDeletionConfig? DeserializeServiceMessageDeletion(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ServiceMessageDeletionConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize ServiceMessageDeletion config for {Scope}", scope);
            return null;
        }
    }

    internal static ServiceMessageDeletionConfig? MergeServiceMessageDeletion(ServiceMessageDeletionConfig? global, ServiceMessageDeletionConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new ServiceMessageDeletionConfig
        {
            DeleteJoinMessages = chat.DeleteJoinMessages,
            DeleteLeaveMessages = chat.DeleteLeaveMessages,
            DeletePhotoChanges = chat.DeletePhotoChanges,
            DeleteTitleChanges = chat.DeleteTitleChanges,
            DeletePinNotifications = chat.DeletePinNotifications,
            DeleteChatCreationMessages = chat.DeleteChatCreationMessages
        };
    }

    // ============================================================================
    // BanCelebration
    // ============================================================================

    public async Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = config.ToData();
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.BanCelebrationConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved BanCelebration config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.BanCelebrationConfig)
            .FirstOrDefaultAsync(ct);

        return DeserializeBanCelebration(json, scope: $"chat {chatId}");
    }

    public async Task DeleteBanCelebrationAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        record.BanCelebrationConfig = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted BanCelebration config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.BanCelebrationConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.BanCelebrationConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.BanCelebrationConfig;

        var globalModel = DeserializeBanCelebration(globalJson, scope: "global");
        var chatModel = DeserializeBanCelebration(chatJson, scope: $"chat {chatId}");

        return MergeBanCelebration(globalModel, chatModel);
    }

    private BanCelebrationConfig? DeserializeBanCelebration(string? json, string scope)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<BanCelebrationConfigData>(json, JsonOptions)?.ToModel();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize BanCelebration config for {Scope}", scope);
            return null;
        }
    }

    internal static BanCelebrationConfig? MergeBanCelebration(BanCelebrationConfig? global, BanCelebrationConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new BanCelebrationConfig
        {
            Enabled = chat.Enabled,
            TriggerOnAutoBan = chat.TriggerOnAutoBan,
            TriggerOnManualBan = chat.TriggerOnManualBan,
            SendToBannedUser = chat.SendToBannedUser
        };
    }

    // ============================================================================
    // WarningSystem (multiplexed in ModerationConfig column)
    // ============================================================================

    // CONCURRENCY NOTE (issue #196): When this method is wired up to admin UI,
    // the read-modify-write pattern below has a theoretical lost-update window
    // for the multiplexed moderation_config JSONB column. Two concurrent admin
    // saves to the same chat — one updating WarningSystem, the other updating
    // InviteCommand — can race: both read the same baseline wrapper at t=0,
    // each writes back its own slot, and the later commit silently drops the
    // earlier sibling's update. Single-instance bot + per-circuit admin UI
    // makes this rare in practice but not impossible. Mitigations when this
    // matters: add a RowVersion/xmin concurrency token to ConfigRecordDto and
    // retry on DbUpdateConcurrencyException, or wrap the read+write in a
    // serializable transaction.
    public async Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        var existingWrapper = ParseModerationWrapper(record?.ModerationConfig);
        var updated = existingWrapper.WithWarningSystem(config.ToData());
        var json = JsonSerializer.Serialize(updated, JsonOptions);

        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.ModerationConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved WarningSystem config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.ModerationConfig)
            .FirstOrDefaultAsync(ct);

        var wrapper = ParseModerationWrapper(json);
        return wrapper.WarningSystem?.ToModel();
    }

    public async Task DeleteWarningSystemAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        var wrapper = ParseModerationWrapper(record.ModerationConfig);
        var updated = wrapper.WithWarningSystem(null);
        record.ModerationConfig = updated.WarningSystem is null && updated.InviteCommand is null
            ? null
            : JsonSerializer.Serialize(updated, JsonOptions);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted WarningSystem config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.ModerationConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.ModerationConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.ModerationConfig;

        var globalWrapper = ParseModerationWrapper(globalJson);
        var chatWrapper = ParseModerationWrapper(chatJson);
        var globalModel = globalWrapper.WarningSystem?.ToModel();
        var chatModel = chatWrapper.WarningSystem?.ToModel();

        return MergeWarningSystem(globalModel, chatModel);
    }

    internal static WarningSystemConfig? MergeWarningSystem(WarningSystemConfig? global, WarningSystemConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new WarningSystemConfig
        {
            AutoBanEnabled = chat.AutoBanEnabled,
            AutoBanThreshold = chat.AutoBanThreshold,
            AutoBanReason = !string.IsNullOrEmpty(chat.AutoBanReason) ? chat.AutoBanReason : global.AutoBanReason
        };
    }

    // ============================================================================
    // InviteCommand (multiplexed in ModerationConfig column)
    // ============================================================================

    // CONCURRENCY NOTE (issue #196): When this method is wired up to admin UI,
    // the read-modify-write pattern below has a theoretical lost-update window
    // for the multiplexed moderation_config JSONB column. Two concurrent admin
    // saves to the same chat — one updating WarningSystem, the other updating
    // InviteCommand — can race: both read the same baseline wrapper at t=0,
    // each writes back its own slot, and the later commit silently drops the
    // earlier sibling's update. Single-instance bot + per-circuit admin UI
    // makes this rare in practice but not impossible. Mitigations when this
    // matters: add a RowVersion/xmin concurrency token to ConfigRecordDto and
    // retry on DbUpdateConcurrencyException, or wrap the read+write in a
    // serializable transaction.
    public async Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        var existingWrapper = ParseModerationWrapper(record?.ModerationConfig);
        var updated = existingWrapper.WithInviteCommand(config.ToData());
        var json = JsonSerializer.Serialize(updated, JsonOptions);

        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = chat.Id };
            await context.Configs.AddAsync(record, ct);
        }
        record.ModerationConfig = json;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved InviteCommand config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var json = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == chatId)
            .Select(c => c.ModerationConfig)
            .FirstOrDefaultAsync(ct);

        var wrapper = ParseModerationWrapper(json);
        return wrapper.InviteCommand?.ToModel();
    }

    public async Task DeleteInviteCommandAsync(ChatIdentity chat, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
        if (record is null) return;

        var wrapper = ParseModerationWrapper(record.ModerationConfig);
        var updated = wrapper.WithInviteCommand(null);
        record.ModerationConfig = updated.WarningSystem is null && updated.InviteCommand is null
            ? null
            : JsonSerializer.Serialize(updated, JsonOptions);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Deleted InviteCommand config for {Chat}", chat.DisplayName);
    }

    public async ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0 || c.ChatId == chatId)
            .Select(c => new { c.ChatId, c.ModerationConfig })
            .ToListAsync(ct);

        var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.ModerationConfig;
        var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.ModerationConfig;

        var globalWrapper = ParseModerationWrapper(globalJson);
        var chatWrapper = ParseModerationWrapper(chatJson);
        var globalModel = globalWrapper.InviteCommand?.ToModel();
        var chatModel = chatWrapper.InviteCommand?.ToModel();

        return MergeInviteCommand(globalModel, chatModel);
    }

    internal static InviteCommandConfig? MergeInviteCommand(InviteCommandConfig? global, InviteCommandConfig? chat)
    {
        if (chat is null) return global;
        if (global is null) return chat;

        return new InviteCommandConfig
        {
            Enabled = chat.Enabled,
            DeleteCommandMessage = chat.DeleteCommandMessage,
            DeleteResponseAfterSeconds = chat.DeleteResponseAfterSeconds
        };
    }

    private ModerationConfigData ParseModerationWrapper(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new ModerationConfigData();
        try
        {
            return JsonSerializer.Deserialize<ModerationConfigData>(json, JsonOptions) ?? new ModerationConfigData();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize moderation_config wrapper; treating as empty");
            return new ModerationConfigData();
        }
    }

    // ============================================================================
    // Bot token (encrypted, no chat scope)
    // ============================================================================

    public async ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var encrypted = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0)
            .Select(c => c.TelegramBotTokenEncrypted)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(encrypted)) return null;

        try
        {
            var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
            return protector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt Telegram bot token");
            return null;
        }
    }

    public async Task SaveBotTokenAsync(string botToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
        var encrypted = protector.Protect(botToken);

        var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0, ct);
        if (record is null)
        {
            record = new ConfigRecordDto { ChatId = 0 };
            await context.Configs.AddAsync(record, ct);
        }
        record.TelegramBotTokenEncrypted = encrypted;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Saved Telegram bot token (encrypted)");
    }
}
