using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for querying image training samples (ML-5 Layer 2)
/// Uses DbContextFactory for safe concurrent access
/// </summary>
public class ImageTrainingSamplesRepository : IImageTrainingSamplesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ImageTrainingSamplesRepository> _logger;

    public ImageTrainingSamplesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ImageTrainingSamplesRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get recent image training samples with their photo hashes
    /// Returns samples ordered by most recent first
    /// </summary>
    public async Task<List<(byte[] PhotoHash, bool IsSpam)>> GetRecentSamplesAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var samples = await context.ImageTrainingSamples
                .AsNoTracking()
                .OrderByDescending(its => its.MarkedAt)
                .Take(limit)
                .Select(its => new { its.PhotoHash, its.IsSpam })
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Retrieved {Count} image training samples for hash comparison", samples.Count);

            return samples.Select(s => (s.PhotoHash, s.IsSpam)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve image training samples");
            return [];
        }
    }
}
