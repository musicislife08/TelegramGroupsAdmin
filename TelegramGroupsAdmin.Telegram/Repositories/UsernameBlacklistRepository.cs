using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class UsernameBlacklistRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<UsernameBlacklistRepository> logger) : IUsernameBlacklistRepository
{
    public async Task<IReadOnlyList<UsernameBlacklistEntry>> GetEnabledEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dtos = await context.UsernameBlacklistEntries
            .AsNoTracking()
            .Where(e => e.Enabled)
            .OrderBy(e => e.Pattern)
            .ToListAsync(cancellationToken);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<IReadOnlyList<UsernameBlacklistEntry>> GetAllEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dtos = await context.UsernameBlacklistEntries
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<long> AddEntryAsync(UsernameBlacklistEntry entry,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dto = entry.ToDto();
        context.UsernameBlacklistEntries.Add(dto);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Added username blacklist entry: {Pattern} (ID: {Id})", entry.Pattern, dto.Id);
        return dto.Id;
    }

    public async Task<bool> ExistsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UsernameBlacklistEntries
            .AsNoTracking()
            .AnyAsync(e => e.Enabled && e.Pattern.ToLower() == pattern.ToLower(), cancellationToken);
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.UsernameBlacklistEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Enabled, enabled), cancellationToken);
        return rows > 0;
    }

    public async Task<bool> DeleteEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.UsernameBlacklistEntries
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> UpdateNotesAsync(long id, string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.UsernameBlacklistEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Notes, notes), cancellationToken);
        return rows > 0;
    }
}
