using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

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
    public async Task<long> AddStopWordAsync(string word, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var stopWord = new StopWordDto
            {
                Word = word.ToLowerInvariant(),
                Enabled = true,
                AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AddedBy = addedBy,
                Notes = notes
            };

            context.StopWords.Add(stopWord);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added stop word: {Word} (ID: {Id})", word, stopWord.Id);
            return stopWord.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add stop word: {Word}", word);
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
            // Query with LEFT JOIN to users table to get email for AddedBy
            // Convert DTO to domain model before returning (DTO stays internal)
            var stopWordDtos = await context.StopWords
                .AsNoTracking()
                .GroupJoin(
                    context.Users,
                    sw => sw.AddedBy,
                    u => u.Id,
                    (sw, users) => new StopWordWithEmailDto
                    {
                        StopWord = sw,
                        AddedByEmail = users.Select(u => u.Email).FirstOrDefault()
                    })
                .OrderBy(sw => sw.StopWord.Word)
                .ToListAsync(cancellationToken);

            return stopWordDtos.Select(dto => dto.ToModel());
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
