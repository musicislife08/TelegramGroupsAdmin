using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

public class ConfigRepository(IDbContextFactory<AppDbContext> contextFactory) : IConfigRepository
{
    public async Task<ConfigRecordDto?> GetAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
    }

    public async Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == config.ChatId, cancellationToken);

        if (existing != null)
        {
            // Update existing record - manually copy properties to avoid Id modification error
            // DO NOT use SetValues() - it tries to copy Id which is a key property
            // NOTE: ChatId is NOT copied - we queried by ChatId, so it's already the same value (immutable natural key)
            // NOTE: ContentDetection config is in separate table (content_detection_configs), not here
            existing.WelcomeConfig = config.WelcomeConfig;
            existing.LogConfig = config.LogConfig;
            existing.ModerationConfig = config.ModerationConfig;
            existing.BotProtectionConfig = config.BotProtectionConfig;
            existing.TelegramBotConfig = config.TelegramBotConfig;
            existing.FileScanningConfig = config.FileScanningConfig;
            existing.BackgroundJobsConfig = config.BackgroundJobsConfig;
            existing.ApiKeys = config.ApiKeys;
            existing.BackupEncryptionConfig = config.BackupEncryptionConfig;
            existing.PassphraseEncrypted = config.PassphraseEncrypted;
            existing.InviteLink = config.InviteLink;
            existing.TelegramBotTokenEncrypted = config.TelegramBotTokenEncrypted;
            // OpenAIConfig removed - superseded by AIProviderConfig
            existing.SendGridConfig = config.SendGridConfig;
            existing.ServiceMessageDeletionConfig = config.ServiceMessageDeletionConfig;
            existing.BanCelebrationConfig = config.BanCelebrationConfig;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            // Immutable properties NOT copied: Id (primary key), ChatId (natural key used for query), CreatedAt (database default)
        }
        else
        {
            // Insert new record (CreatedAt will be set by database default)
            config.UpdatedAt = null;
            await context.Configs.AddAsync(config, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (config != null)
        {
            context.Configs.Remove(config);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        return await GetAsync(chatId, cancellationToken);
    }

    public async Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (existing != null)
        {
            // Update existing config
            existing.InviteLink = inviteLink;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Create new config row just for invite link
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
}
