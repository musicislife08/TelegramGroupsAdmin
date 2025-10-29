using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing file scan result caching with 24-hour TTL
/// Thread-safe via IDbContextFactory pattern
/// </summary>
public class FileScanResultRepository : IFileScanResultRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<FileScanResultRepository> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public FileScanResultRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<FileScanResultRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<FileScanResultModel>> GetCachedResultsByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffTime = DateTimeOffset.UtcNow - CacheTtl;

        var cachedResults = await context.FileScanResults
            .AsNoTracking()
            .Where(fsr => fsr.FileHash == fileHash && fsr.ScannedAt >= cutoffTime)
            .OrderByDescending(fsr => fsr.ScannedAt)
            .ToListAsync(cancellationToken)
            ;

        _logger.LogDebug("Cache lookup for hash {FileHash}: {Count} results found within TTL",
            fileHash, cachedResults.Count);

        return cachedResults.Select(dto => dto.ToModel()).ToList();
    }

    public async Task<FileScanResultModel> AddScanResultAsync(
        FileScanResultModel scanResult,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var dto = scanResult.ToDto();
        context.FileScanResults.Add(dto);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Cached scan result: hash={FileHash}, scanner={Scanner}, result={Result}",
            dto.FileHash, dto.Scanner, dto.Result);

        return dto.ToModel();
    }

    public async Task<int> CleanupExpiredResultsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffTime = DateTimeOffset.UtcNow - CacheTtl;

        var expiredResults = await context.FileScanResults
            .Where(fsr => fsr.ScannedAt < cutoffTime)
            .ToListAsync(cancellationToken)
            ;

        if (!expiredResults.Any())
        {
            _logger.LogDebug("No expired scan results to clean up");
            return 0;
        }

        context.FileScanResults.RemoveRange(expiredResults);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleaned up {Count} expired scan results (older than {TTL})",
            expiredResults.Count, CacheTtl);

        return expiredResults.Count;
    }

    public async Task<int> ClearAllCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var allResults = await context.FileScanResults
            .ToListAsync(cancellationToken)
            ;

        if (!allResults.Any())
        {
            _logger.LogDebug("No scan results to clear");
            return 0;
        }

        context.FileScanResults.RemoveRange(allResults);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Cleared ALL {Count} cached scan results (testing/debugging operation)",
            allResults.Count);

        return allResults.Count;
    }
}
