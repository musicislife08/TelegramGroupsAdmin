using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class LinkedChannelsRepository : ILinkedChannelsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<LinkedChannelsRepository> _logger;

    public LinkedChannelsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<LinkedChannelsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task UpsertAsync(LinkedChannelRecord record, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.LinkedChannels
            .FirstOrDefaultAsync(lc => lc.ManagedChatId == record.ManagedChatId, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.ChannelId = record.ChannelId;
            existing.ChannelName = record.ChannelName;
            existing.ChannelIconPath = record.ChannelIconPath;
            existing.PhotoHash = record.PhotoHash;
            existing.LastSynced = record.LastSynced;

            _logger.LogDebug(
                "Updated linked channel {ChannelId} ({ChannelName}) for chat {ChatId}",
                record.ChannelId,
                record.ChannelName,
                record.ManagedChatId);
        }
        else
        {
            // Insert new record
            var entity = record.ToDto();
            entity.Id = 0; // Let DB generate ID
            context.LinkedChannels.Add(entity);

            _logger.LogInformation(
                "Created linked channel {ChannelId} ({ChannelName}) for chat {ChatId}",
                record.ChannelId,
                record.ChannelName,
                record.ManagedChatId);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LinkedChannelRecord?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LinkedChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(lc => lc.ManagedChatId == chatId, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<LinkedChannelRecord?> GetByChannelIdAsync(long channelId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LinkedChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(lc => lc.ChannelId == channelId, cancellationToken);

        return entity?.ToModel();
    }

    public async Task DeleteByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LinkedChannels
            .FirstOrDefaultAsync(lc => lc.ManagedChatId == chatId, cancellationToken);

        if (entity != null)
        {
            context.LinkedChannels.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Deleted linked channel {ChannelId} ({ChannelName}) for chat {ChatId}",
                entity.ChannelId,
                entity.ChannelName,
                chatId);
        }
    }

    public async Task<List<LinkedChannelRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.LinkedChannels
            .AsNoTracking()
            .OrderBy(lc => lc.ChannelName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<HashSet<long>> GetAllManagedChatIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chatIds = await context.LinkedChannels
            .AsNoTracking()
            .Select(lc => lc.ManagedChatId)
            .ToListAsync(cancellationToken);

        return chatIds.ToHashSet();
    }
}
