using TelegramGroupsAdmin.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository implementation for stop words management (EF Core)
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class StopWordsRepository : IStopWordsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<StopWordsRepository> _logger;

    public StopWordsRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<StopWordsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get all enabled stop words
    /// </summary>
    public async Task<IEnumerable<string>> GetEnabledStopWordsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            return await context.StopWords
                .AsNoTracking()
                .Where(sw => sw.Enabled)
                .OrderBy(sw => sw.Word)
                .Select(sw => sw.Word)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve enabled stop words");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Add a new stop word
    /// </summary>
    public async Task<long> AddStopWordAsync(Models.StopWord stopWord, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var dto = stopWord.ToDto();
            context.StopWords.Add(dto);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added stop word: {Word} (ID: {Id})", stopWord.Word, dto.Id);
            return dto.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add stop word: {Word}", stopWord.Word);
            throw;
        }
    }

    /// <summary>
    /// Enable or disable a stop word
    /// </summary>
    public async Task<bool> SetStopWordEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var stopWord = await context.StopWords.FindAsync([id], cancellationToken);
            if (stopWord == null)
            {
                return false;
            }

            stopWord.Enabled = enabled;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated stop word {Id} enabled status to {Enabled}", id, enabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stop word {Id} enabled status", id);
            throw;
        }
    }

    /// <summary>
    /// Check if a word exists as a stop word
    /// </summary>
    public async Task<bool> ExistsAsync(string word, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            return await context.StopWords
                .AsNoTracking()
                .AnyAsync(sw => sw.Word == word.ToLowerInvariant(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if stop word exists: {Word}", word);
            return false;
        }
    }

    /// <summary>
    /// Get all stop words (enabled and disabled) with full details
    /// </summary>
    public async Task<IEnumerable<Models.StopWord>> GetAllStopWordsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Query with LEFT JOINs to resolve actor display names (Phase 4.19)
            var stopWords = await context.StopWords
                .AsNoTracking()
                .GroupJoin(context.Users, sw => sw.WebUserId, u => u.Id, (sw, users) => new { sw, users })
                .SelectMany(x => x.users.DefaultIfEmpty(), (x, user) => new { x.sw, user })
                .GroupJoin(context.TelegramUsers, x => x.sw.TelegramUserId, tu => tu.TelegramUserId, (x, tgUsers) => new { x.sw, x.user, tgUsers })
                .SelectMany(x => x.tgUsers.DefaultIfEmpty(), (x, tgUser) => new
                {
                    x.sw,
                    ActorWebEmail = x.user != null ? x.user.Email : null,
                    ActorTelegramUsername = tgUser != null ? tgUser.Username : null,
                    ActorTelegramFirstName = tgUser != null ? tgUser.FirstName : null
                })
                .OrderBy(x => x.sw.Word)
                .Select(x => new Models.StopWord(
                    x.sw.Id,
                    x.sw.Word,
                    x.sw.Enabled,
                    x.sw.AddedDate,
                    // Resolve actor display name (Phase 4.19: Actor system)
                    x.sw.WebUserId != null
                        ? (x.ActorWebEmail ?? "User " + x.sw.WebUserId.Substring(0, 8) + "...")
                        : x.sw.TelegramUserId != null
                            ? (x.ActorTelegramUsername != null ? "@" + x.ActorTelegramUsername : x.ActorTelegramFirstName ?? "User " + x.sw.TelegramUserId.ToString())
                            : x.sw.SystemIdentifier ?? "System",
                    x.sw.Notes
                ))
                .ToListAsync(cancellationToken);

            return stopWords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all stop words");
            return Enumerable.Empty<Models.StopWord>();
        }
    }

    /// <summary>
    /// Update stop word notes
    /// </summary>
    public async Task<bool> UpdateStopWordNotesAsync(long id, string? notes, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var stopWord = await context.StopWords.FindAsync([id], cancellationToken);
            if (stopWord == null)
            {
                return false;
            }

            stopWord.Notes = notes;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated stop word {Id} notes", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stop word {Id} notes", id);
            throw;
        }
    }
}
