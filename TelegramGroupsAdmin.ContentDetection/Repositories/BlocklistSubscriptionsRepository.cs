using TelegramGroupsAdmin.Core.Models;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for blocklist subscriptions with actor resolution
/// Phase 4.13: URL Filtering
/// </summary>
public class BlocklistSubscriptionsRepository : IBlocklistSubscriptionsRepository
{
    private readonly AppDbContext _context;

    public BlocklistSubscriptionsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<BlocklistSubscription>> GetAllAsync(long chatId = 0, CancellationToken cancellationToken = default)
    {
        var query = _context.BlocklistSubscriptions
            .AsQueryable();

        if (chatId == 0)
        {
            // Global only
            query = query.Where(bs => bs.ChatId == 0);
        }
        else
        {
            // Global + chat-specific (for UI display/merging)
            query = query.Where(bs => bs.ChatId == 0 || bs.ChatId == chatId);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<List<BlocklistSubscription>> GetEffectiveAsync(long chatId, BlockMode? blockMode = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BlocklistSubscriptions
            .Where(bs => bs.ChatId == 0 || bs.ChatId == chatId)  // Global (0) OR chat-specific
            .Where(bs => bs.Enabled);

        if (blockMode.HasValue)
        {
            query = query.Where(bs => bs.BlockMode == (int)blockMode.Value);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<BlocklistSubscription?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var dto = await _context.BlocklistSubscriptions
            .FirstOrDefaultAsync(bs => bs.Id == id, cancellationToken);

        return dto == null ? null : ToModel(dto);
    }

    public async Task<long> InsertAsync(BlocklistSubscription subscription, CancellationToken cancellationToken = default)
    {
        var dto = new BlocklistSubscriptionDto
        {
            ChatId = subscription.ChatId,
            Name = subscription.Name,
            Url = subscription.Url,
            Format = (int)subscription.Format,
            BlockMode = (int)subscription.BlockMode,
            IsBuiltIn = subscription.IsBuiltIn,
            Enabled = subscription.Enabled,
            LastFetched = subscription.LastFetched,
            EntryCount = subscription.EntryCount,
            RefreshIntervalHours = subscription.RefreshIntervalHours,
            WebUserId = subscription.AddedBy.WebUserId,
            TelegramUserId = subscription.AddedBy.TelegramUserId,
            SystemIdentifier = subscription.AddedBy.SystemIdentifier,
            AddedDate = subscription.AddedDate,
            Notes = subscription.Notes
        };

        _context.BlocklistSubscriptions.Add(dto);
        await _context.SaveChangesAsync(cancellationToken);

        return dto.Id;
    }

    public async Task UpdateAsync(BlocklistSubscription subscription, CancellationToken cancellationToken = default)
    {
        var dto = await _context.BlocklistSubscriptions
            .FirstOrDefaultAsync(bs => bs.Id == subscription.Id, cancellationToken);

        if (dto == null)
        {
            throw new InvalidOperationException($"Blocklist subscription {subscription.Id} not found");
        }

        // Update mutable fields
        dto.Name = subscription.Name;
        dto.Url = subscription.Url;
        dto.Format = (int)subscription.Format;
        dto.BlockMode = (int)subscription.BlockMode;
        dto.Enabled = subscription.Enabled;
        dto.RefreshIntervalHours = subscription.RefreshIntervalHours;
        dto.Notes = subscription.Notes;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var dto = await _context.BlocklistSubscriptions
            .FirstOrDefaultAsync(bs => bs.Id == id, cancellationToken);

        if (dto != null)
        {
            _context.BlocklistSubscriptions.Remove(dto);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateFetchMetadataAsync(long id, DateTimeOffset lastFetched, int entryCount, CancellationToken cancellationToken = default)
    {
        var dto = await _context.BlocklistSubscriptions
            .FirstOrDefaultAsync(bs => bs.Id == id, cancellationToken);

        if (dto != null)
        {
            dto.LastFetched = lastFetched;
            dto.EntryCount = entryCount;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<BlocklistSubscription>> FindByUrlAsync(string url, long chatId = 0, CancellationToken cancellationToken = default)
    {
        var query = _context.BlocklistSubscriptions
            .Where(bs => bs.Url == url);

        if (chatId == 0)
        {
            // Global only
            query = query.Where(bs => bs.ChatId == 0);
        }
        else
        {
            // Global + chat-specific
            query = query.Where(bs => bs.ChatId == 0 || bs.ChatId == chatId);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    /// <summary>
    /// Convert DTO to UI model with actor resolution
    /// </summary>
    private static BlocklistSubscription ToModel(BlocklistSubscriptionDto dto)
    {
        return new BlocklistSubscription(
            Id: dto.Id,
            ChatId: dto.ChatId,
            Name: dto.Name,
            Url: dto.Url,
            Format: (BlocklistFormat)dto.Format,
            BlockMode: (BlockMode)dto.BlockMode,
            IsBuiltIn: dto.IsBuiltIn,
            Enabled: dto.Enabled,
            LastFetched: dto.LastFetched,
            EntryCount: dto.EntryCount,
            RefreshIntervalHours: dto.RefreshIntervalHours,
            AddedBy: ResolveActor(dto),
            AddedDate: dto.AddedDate,
            Notes: dto.Notes
        );
    }

    /// <summary>
    /// Resolve actor from DTO fields
    /// Note: For blocklist subscriptions, we don't do expensive JOINs for actor names
    /// System actors show as "System: <identifier>", others show as ID
    /// For full resolution with email/username, use separate query
    /// </summary>
    private static Actor ResolveActor(BlocklistSubscriptionDto dto)
    {
        if (!string.IsNullOrEmpty(dto.SystemIdentifier))
        {
            return Actor.FromSystem(dto.SystemIdentifier);
        }

        if (!string.IsNullOrEmpty(dto.WebUserId))
        {
            return Actor.FromWebUser(dto.WebUserId);
        }

        if (dto.TelegramUserId.HasValue)
        {
            return Actor.FromTelegramUser(dto.TelegramUserId.Value);
        }

        return Actor.Unknown;
    }
}
