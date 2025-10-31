using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for querying video training samples (ML-6 Layer 2)
/// Uses DbContextFactory for safe concurrent access
/// </summary>
public class VideoTrainingSamplesRepository : IVideoTrainingSamplesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<VideoTrainingSamplesRepository> _logger;

    public VideoTrainingSamplesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<VideoTrainingSamplesRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get recent video training samples with their keyframe hashes
    /// Returns samples ordered by most recent first
    /// </summary>
    public async Task<List<(string KeyframeHashes, bool IsSpam)>> GetRecentSamplesAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var samples = await context.VideoTrainingSamples
                .AsNoTracking()
                .OrderByDescending(vts => vts.MarkedAt)
                .Take(limit)
                .Select(vts => new { vts.KeyframeHashes, vts.IsSpam })
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Retrieved {Count} video training samples for hash comparison", samples.Count);

            return samples.Select(s => (s.KeyframeHashes, s.IsSpam)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve video training samples");
            return [];
        }
    }
}
