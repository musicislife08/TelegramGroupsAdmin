using TelegramGroupsAdmin.Core.Models;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for manual domain filters with actor resolution
/// Phase 4.13: URL Filtering
/// </summary>
public class DomainFiltersRepository : IDomainFiltersRepository
{
    private readonly AppDbContext _context;

    public DomainFiltersRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DomainFilter>> GetAllAsync(long chatId = 0, CancellationToken cancellationToken = default)
    {
        var query = _context.DomainFilters
            .AsQueryable();

        if (chatId == 0)
        {
            // Global only
            query = query.Where(df => df.ChatId == 0);
        }
        else
        {
            // Global + chat-specific (for UI display/merging)
            query = query.Where(df => df.ChatId == 0 || df.ChatId == chatId);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<List<DomainFilter>> GetEffectiveAsync(long chatId, DomainFilterType? filterType = null, BlockMode? blockMode = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DomainFilters
            .Where(df => df.ChatId == 0 || df.ChatId == chatId)  // Global (0) OR chat-specific
            .Where(df => df.Enabled);

        if (filterType.HasValue)
        {
            query = query.Where(df => df.FilterType == (int)filterType.Value);
        }

        if (blockMode.HasValue)
        {
            query = query.Where(df => df.BlockMode == (int)blockMode.Value);
        }

        var dtos = await query.ToListAsync(cancellationToken);
        return dtos.Select(ToModel).ToList();
    }

    public async Task<DomainFilter?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var dto = await _context.DomainFilters
            .FirstOrDefaultAsync(df => df.Id == id, cancellationToken);

        return dto == null ? null : ToModel(dto);
    }

    public async Task<long> InsertAsync(DomainFilter filter, CancellationToken cancellationToken = default)
    {
        var dto = new DomainFilterDto
        {
            ChatId = filter.ChatId,
            Domain = filter.Domain,
            FilterType = (int)filter.FilterType,
            BlockMode = (int)filter.BlockMode,
            Enabled = filter.Enabled,
            WebUserId = filter.AddedBy.WebUserId,
            TelegramUserId = filter.AddedBy.TelegramUserId,
            SystemIdentifier = filter.AddedBy.SystemIdentifier,
            AddedDate = filter.AddedDate,
            Notes = filter.Notes
        };

        _context.DomainFilters.Add(dto);
        await _context.SaveChangesAsync(cancellationToken);

        return dto.Id;
    }

    public async Task UpdateAsync(DomainFilter filter, CancellationToken cancellationToken = default)
    {
        var dto = await _context.DomainFilters
            .FirstOrDefaultAsync(df => df.Id == filter.Id, cancellationToken);

        if (dto == null)
        {
            throw new InvalidOperationException($"Domain filter {filter.Id} not found");
        }

        // Update mutable fields
        dto.Domain = filter.Domain;
        dto.FilterType = (int)filter.FilterType;
        dto.BlockMode = (int)filter.BlockMode;
        dto.Enabled = filter.Enabled;
        dto.Notes = filter.Notes;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var dto = await _context.DomainFilters
            .FirstOrDefaultAsync(df => df.Id == id, cancellationToken);

        if (dto != null)
        {
            _context.DomainFilters.Remove(dto);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Convert DTO to UI model with actor resolution
    /// </summary>
    private static DomainFilter ToModel(DomainFilterDto dto)
    {
        return new DomainFilter(
            Id: dto.Id,
            ChatId: dto.ChatId,
            Domain: dto.Domain,
            FilterType: (DomainFilterType)dto.FilterType,
            BlockMode: (BlockMode)dto.BlockMode,
            Enabled: dto.Enabled,
            AddedBy: ResolveActor(dto),
            AddedDate: dto.AddedDate,
            Notes: dto.Notes
        );
    }

    /// <summary>
    /// Resolve actor from DTO fields
    /// Note: For domain filters, we don't do expensive JOINs for actor names
    /// System actors show as "System: <identifier>", others show as ID
    /// For full resolution with email/username, use separate query
    /// </summary>
    private static Actor ResolveActor(DomainFilterDto dto)
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
