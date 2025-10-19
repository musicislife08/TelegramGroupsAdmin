using TelegramGroupsAdmin.Core.Models;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for normalized cached blocked domains
/// Phase 4.13: URL Filtering
/// </summary>
public class CachedBlockedDomainsRepository : ICachedBlockedDomainsRepository
{
    private readonly AppDbContext _context;

    public CachedBlockedDomainsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CachedBlockedDomain>> GetAllAsync(long? chatId = null, BlockMode? blockMode = null, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains
            .AsQueryable();

        if (chatId.HasValue)
        {
            query = query.Where(cbd => cbd.ChatId == null || cbd.ChatId == chatId.Value);
        }

        if (blockMode.HasValue)
        {
            query = query.Where(cbd => cbd.BlockMode == (int)blockMode.Value);
        }

        var dtos = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<CachedBlockedDomain?> GetByDomainAsync(string domain, long chatId, BlockMode blockMode, CancellationToken cancellationToken = default)
    {
        // Normalize domain before lookup (matches parser normalization)
        domain = NormalizeDomain(domain);

        var dto = await _context.CachedBlockedDomains
            .FirstOrDefaultAsync(cbd =>
                cbd.Domain == domain &&
                (cbd.ChatId == null || cbd.ChatId == chatId) &&
                cbd.BlockMode == (int)blockMode,
                cancellationToken).ConfigureAwait(false);

        return dto == null ? null : ToModel(dto);
    }

    public async Task<CachedBlockedDomain?> FindHardBlockAsync(string domain, long chatId, CancellationToken cancellationToken = default)
    {
        // Normalize domain before lookup
        domain = NormalizeDomain(domain);

        // Check global + chat-specific hard blocks (BlockMode = Hard)
        var dto = await _context.CachedBlockedDomains
            .FirstOrDefaultAsync(cbd =>
                cbd.Domain == domain &&
                (cbd.ChatId == null || cbd.ChatId == chatId) &&
                cbd.BlockMode == (int)BlockMode.Hard,
                cancellationToken).ConfigureAwait(false);

        return dto == null ? null : ToModel(dto);
    }

    public async Task BulkInsertAsync(List<CachedBlockedDomain> domains, CancellationToken cancellationToken = default)
    {
        var dtos = domains.Select(d => new CachedBlockedDomainDto
        {
            ChatId = d.ChatId,
            Domain = NormalizeDomain(d.Domain),
            BlockMode = (int)d.BlockMode,
            SourceSubscriptionId = d.SourceSubscriptionId,
            FirstSeen = d.FirstSeen,
            LastVerified = d.LastVerified,
            Notes = d.Notes
        }).ToList();

        await _context.CachedBlockedDomains.AddRangeAsync(dtos, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBySourceAsync(string sourceType, long sourceId, CancellationToken cancellationToken = default)
    {
        // Source type is currently only "subscription", sourceId is blocklist_subscriptions.id
        // Use ExecuteDeleteAsync to avoid loading entities into memory and prevent concurrency issues
        await _context.CachedBlockedDomains
            .Where(cbd => cbd.SourceSubscriptionId == sourceId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains.AsQueryable();

        if (chatId.HasValue)
        {
            query = query.Where(cbd => cbd.ChatId == chatId.Value);
        }

        // Use ExecuteDeleteAsync to avoid loading entities into memory and prevent concurrency issues
        await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UrlFilterStats> GetStatsAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains.AsQueryable();

        if (chatId.HasValue)
        {
            query = query.Where(cbd => cbd.ChatId == null || cbd.ChatId == chatId.Value);
        }

        var totalCachedDomains = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var hardBlockDomains = await query.CountAsync(cbd => cbd.BlockMode == (int)BlockMode.Hard, cancellationToken).ConfigureAwait(false);
        var softBlockDomains = await query.CountAsync(cbd => cbd.BlockMode == (int)BlockMode.Soft, cancellationToken).ConfigureAwait(false);

        // Count subscriptions
        var totalSubscriptions = await _context.BlocklistSubscriptions.CountAsync(cancellationToken).ConfigureAwait(false);
        var enabledSubscriptions = await _context.BlocklistSubscriptions
            .CountAsync(bs => bs.Enabled, cancellationToken).ConfigureAwait(false);
        var hardBlockSubscriptions = await _context.BlocklistSubscriptions
            .CountAsync(bs => bs.Enabled && bs.BlockMode == (int)BlockMode.Hard, cancellationToken).ConfigureAwait(false);
        var softBlockSubscriptions = await _context.BlocklistSubscriptions
            .CountAsync(bs => bs.Enabled && bs.BlockMode == (int)BlockMode.Soft, cancellationToken).ConfigureAwait(false);

        // Count whitelisted domains (domain_filters with FilterType=Whitelist)
        var whitelistedDomains = await _context.DomainFilters
            .CountAsync(df => df.Enabled && df.FilterType == 1, cancellationToken).ConfigureAwait(false);  // 1 = Whitelist

        return new UrlFilterStats(
            TotalSubscriptions: totalSubscriptions,
            EnabledSubscriptions: enabledSubscriptions,
            HardBlockSubscriptions: hardBlockSubscriptions,
            SoftBlockSubscriptions: softBlockSubscriptions,
            TotalCachedDomains: totalCachedDomains,
            HardBlockDomains: hardBlockDomains,
            SoftBlockDomains: softBlockDomains,
            WhitelistedDomains: whitelistedDomains,
            LastSync: null  // Will be populated by sync service from configs
        );
    }

    /// <summary>
    /// Convert DTO to UI model
    /// </summary>
    private static CachedBlockedDomain ToModel(CachedBlockedDomainDto dto)
    {
        return new CachedBlockedDomain(
            Id: dto.Id,
            Domain: dto.Domain,
            BlockMode: (BlockMode)dto.BlockMode,
            ChatId: dto.ChatId,
            SourceSubscriptionId: dto.SourceSubscriptionId,
            FirstSeen: dto.FirstSeen,
            LastVerified: dto.LastVerified,
            Notes: dto.Notes
        );
    }

    /// <summary>
    /// Normalize domain: lowercase, trim, remove www prefix
    /// Must match parser normalization logic
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();

        // Remove www prefix if present
        if (domain.StartsWith("www."))
        {
            domain = domain.Substring(4);
        }

        return domain;
    }
}
