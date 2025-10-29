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

    public async Task<List<CachedBlockedDomain>> GetAllAsync(long chatId = 0, BlockMode? blockMode = null, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains
            .AsQueryable();

        if (chatId == 0)
        {
            // Global only
            query = query.Where(cbd => cbd.ChatId == 0);
        }
        else
        {
            // Global + chat-specific
            query = query.Where(cbd => cbd.ChatId == 0 || cbd.ChatId == chatId);
        }

        if (blockMode.HasValue)
        {
            query = query.Where(cbd => cbd.BlockMode == (int)blockMode.Value);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<CachedBlockedDomain?> GetByDomainAsync(string domain, long chatId, BlockMode blockMode, CancellationToken cancellationToken = default)
    {
        // Normalize domain before lookup (matches parser normalization)
        domain = NormalizeDomain(domain);

        var dto = await _context.CachedBlockedDomains
            .FirstOrDefaultAsync(cbd =>
                cbd.Domain == domain &&
                (cbd.ChatId == 0 || cbd.ChatId == chatId) &&
                cbd.BlockMode == (int)blockMode,
                cancellationToken);

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
                (cbd.ChatId == 0 || cbd.ChatId == chatId) &&
                cbd.BlockMode == (int)BlockMode.Hard,
                cancellationToken);

        return dto == null ? null : ToModel(dto);
    }

    public async Task BulkInsertAsync(List<CachedBlockedDomain> domains, CancellationToken cancellationToken = default)
    {
        if (domains.Count == 0)
            return;

        // Use PostgreSQL UPSERT (ON CONFLICT DO UPDATE) to handle duplicates
        // The unique constraint is on (domain, block_mode, chat_id)
        // Strategy: Update last_verified on conflict to keep data fresh

        // IMPORTANT: Deduplicate in-memory first!
        // Blocklists can contain duplicate domains, and PostgreSQL UPSERT
        // cannot handle duplicates within the same INSERT batch
        var dtos = domains
            .Select(d => new CachedBlockedDomainDto
            {
                ChatId = d.ChatId,
                Domain = NormalizeDomain(d.Domain),
                BlockMode = (int)d.BlockMode,
                SourceSubscriptionId = d.SourceSubscriptionId,
                FirstSeen = d.FirstSeen,
                LastVerified = d.LastVerified,
                Notes = d.Notes
            })
            .GroupBy(d => new { d.Domain, d.BlockMode, d.ChatId })
            .Select(g => g.First()) // Keep first occurrence of each (domain, block_mode, chat_id) combination
            .ToList();

        if (dtos.Count == 0)
            return;

        // Use PostgreSQL bulk UPSERT with UNNEST for maximum performance
        // This inserts all domains in a single query with ON CONFLICT handling
        var domainNames = dtos.Select(d => d.Domain).ToArray();
        var blockModes = dtos.Select(d => d.BlockMode).ToArray();
        var chatIds = dtos.Select(d => d.ChatId).ToArray();
        var sourceIds = dtos.Select(d => d.SourceSubscriptionId).ToArray();
        var firstSeens = dtos.Select(d => d.FirstSeen).ToArray();
        var lastVerifieds = dtos.Select(d => d.LastVerified).ToArray();
        var notesArray = dtos.Select(d => d.Notes).ToArray();

        var sql = @"
            INSERT INTO cached_blocked_domains
                (domain, block_mode, chat_id, source_subscription_id, first_seen, last_verified, notes)
            SELECT * FROM UNNEST(
                @p0::text[],
                @p1::integer[],
                @p2::bigint[],
                @p3::bigint[],
                @p4::timestamp with time zone[],
                @p5::timestamp with time zone[],
                @p6::text[]
            )
            ON CONFLICT (domain, block_mode, chat_id)
            DO UPDATE SET
                last_verified = EXCLUDED.last_verified,
                source_subscription_id = EXCLUDED.source_subscription_id";

        // Build parameters array (excluding cancellationToken)
        var parameters = new object[]
        {
            domainNames,
            blockModes,
            chatIds,
            sourceIds,
            firstSeens,
            lastVerifieds,
            notesArray
        };

        await _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    public async Task DeleteBySourceAsync(string sourceType, long sourceId, CancellationToken cancellationToken = default)
    {
        // Source type is currently only "subscription", sourceId is blocklist_subscriptions.id
        // Use ExecuteDeleteAsync to avoid loading entities into memory and prevent concurrency issues
        await _context.CachedBlockedDomains
            .Where(cbd => cbd.SourceSubscriptionId == sourceId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAllAsync(long chatId = 0, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains.AsQueryable();

        if (chatId == 0)
        {
            // Delete global only
            query = query.Where(cbd => cbd.ChatId == 0);
        }
        else
        {
            // Delete chat-specific only (preserve global)
            query = query.Where(cbd => cbd.ChatId == chatId);
        }

        // Use ExecuteDeleteAsync to avoid loading entities into memory and prevent concurrency issues
        await query.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<UrlFilterStats> GetStatsAsync(long chatId = 0, CancellationToken cancellationToken = default)
    {
        var query = _context.CachedBlockedDomains.AsQueryable();

        if (chatId == 0)
        {
            // Global only
            query = query.Where(cbd => cbd.ChatId == 0);
        }
        else
        {
            // Global + chat-specific
            query = query.Where(cbd => cbd.ChatId == 0 || cbd.ChatId == chatId);
        }

        // PERF-CD-3: Optimized - Single GroupBy query for CachedBlockedDomains
        // Benchmark: 2.24× faster (55ms → 25ms) vs 3 separate CountAsync queries
        var domainStats = await query
            .GroupBy(cbd => cbd.BlockMode)
            .Select(g => new { BlockMode = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalCachedDomains = domainStats.Sum(s => s.Count);
        var hardBlockDomains = domainStats.FirstOrDefault(s => s.BlockMode == (int)BlockMode.Hard)?.Count ?? 0;
        var softBlockDomains = domainStats.FirstOrDefault(s => s.BlockMode == (int)BlockMode.Soft)?.Count ?? 0;

        // PERF-CD-3: Optimized - Single GroupBy query for BlocklistSubscriptions
        var subscriptionStats = await _context.BlocklistSubscriptions
            .GroupBy(bs => new { bs.Enabled, bs.BlockMode })
            .Select(g => new { g.Key.Enabled, g.Key.BlockMode, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalSubscriptions = subscriptionStats.Sum(s => s.Count);
        var enabledSubscriptions = subscriptionStats.Where(s => s.Enabled).Sum(s => s.Count);
        var hardBlockSubscriptions = subscriptionStats
            .FirstOrDefault(s => s.Enabled && s.BlockMode == (int)BlockMode.Hard)?.Count ?? 0;
        var softBlockSubscriptions = subscriptionStats
            .FirstOrDefault(s => s.Enabled && s.BlockMode == (int)BlockMode.Soft)?.Count ?? 0;

        // Count whitelisted domains (domain_filters with FilterType=Whitelist)
        // Single query - already optimal
        var whitelistedDomains = await _context.DomainFilters
            .CountAsync(df => df.Enabled && df.FilterType == 1, cancellationToken);  // 1 = Whitelist

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
