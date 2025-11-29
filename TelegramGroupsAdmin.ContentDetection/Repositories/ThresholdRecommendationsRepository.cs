using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing ML-generated threshold optimization recommendations
/// </summary>
public class ThresholdRecommendationsRepository : IThresholdRecommendationsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ThresholdRecommendationsRepository> _logger;

    public ThresholdRecommendationsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ThresholdRecommendationsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> InsertAsync(ThresholdRecommendation recommendation, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var dto = recommendation.ToDto();
        context.ThresholdRecommendations.Add(dto);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Inserted threshold recommendation for algorithm {AlgorithmName}: {CurrentThreshold} → {RecommendedThreshold}",
            recommendation.AlgorithmName,
            recommendation.CurrentThreshold,
            recommendation.RecommendedThreshold);

        return dto.Id;
    }

    public async Task<ThresholdRecommendation?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await context.ThresholdRecommendations
            .AsNoTracking()
            .GroupJoin(context.Users,
                tr => tr.ReviewedByUserId,
                u => u.Id,
                (tr, users) => new { tr, users })
            .SelectMany(x => x.users.DefaultIfEmpty(),
                (x, user) => new
                {
                    x.tr,
                    ReviewedByUsername = user != null ? user.Email : null
                })
            .Where(x => x.tr.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        return result?.tr.ToModel(result.ReviewedByUsername);
    }

    public async Task<List<ThresholdRecommendation>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        return await GetByStatusAsync("pending", cancellationToken);
    }

    public async Task<List<ThresholdRecommendation>> GetByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var results = await context.ThresholdRecommendations
            .AsNoTracking()
            .Where(tr => tr.Status == status)
            .GroupJoin(context.Users,
                tr => tr.ReviewedByUserId,
                u => u.Id,
                (tr, users) => new { tr, users })
            .SelectMany(x => x.users.DefaultIfEmpty(),
                (x, user) => new
                {
                    x.tr,
                    ReviewedByUsername = user != null ? user.Email : null
                })
            .OrderByDescending(x => x.tr.CreatedAt)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.tr.ToModel(r.ReviewedByUsername)).ToList();
    }

    public async Task<List<ThresholdRecommendation>> GetByAlgorithmAsync(string algorithmName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var results = await context.ThresholdRecommendations
            .AsNoTracking()
            .Where(tr => tr.AlgorithmName == algorithmName)
            .GroupJoin(context.Users,
                tr => tr.ReviewedByUserId,
                u => u.Id,
                (tr, users) => new { tr, users })
            .SelectMany(x => x.users.DefaultIfEmpty(),
                (x, user) => new
                {
                    x.tr,
                    ReviewedByUsername = user != null ? user.Email : null
                })
            .OrderByDescending(x => x.tr.CreatedAt)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.tr.ToModel(r.ReviewedByUsername)).ToList();
    }

    public async Task<List<ThresholdRecommendation>> GetLatestPendingByAlgorithmAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get the most recent pending recommendation for each algorithm
        var latestIds = await context.ThresholdRecommendations
            .AsNoTracking()
            .Where(tr => tr.Status == "pending")
            .GroupBy(tr => tr.AlgorithmName)
            .Select(g => g.OrderByDescending(tr => tr.CreatedAt).First().Id)
            .ToListAsync(cancellationToken);

        var results = await context.ThresholdRecommendations
            .AsNoTracking()
            .Where(tr => latestIds.Contains(tr.Id))
            .GroupJoin(context.Users,
                tr => tr.ReviewedByUserId,
                u => u.Id,
                (tr, users) => new { tr, users })
            .SelectMany(x => x.users.DefaultIfEmpty(),
                (x, user) => new
                {
                    x.tr,
                    ReviewedByUsername = user != null ? user.Email : null
                })
            .ToListAsync(cancellationToken);

        return results.Select(r => r.tr.ToModel(r.ReviewedByUsername)).ToList();
    }

    public async Task UpdateStatusAsync(
        long id,
        string status,
        string reviewedByUserId,
        string? reviewNotes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var recommendation = await context.ThresholdRecommendations
            .Where(tr => tr.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (recommendation == null)
        {
            _logger.LogWarning("Threshold recommendation {Id} not found for status update", id);
            return;
        }

        recommendation.Status = status;
        recommendation.ReviewedByUserId = reviewedByUserId;
        recommendation.ReviewedAt = DateTimeOffset.UtcNow;
        recommendation.ReviewNotes = reviewNotes;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated threshold recommendation {Id} status to {Status} by user {UserId}",
            id,
            status,
            reviewedByUserId);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var deleted = await context.ThresholdRecommendations
            .Where(tr => tr.CreatedAt < timestamp)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} threshold recommendations older than {Timestamp}", deleted, timestamp);

        return deleted;
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ThresholdRecommendations
            .AsNoTracking()
            .Where(tr => tr.Status == "pending")
            .CountAsync(cancellationToken);
    }

    public async Task<List<ThresholdRecommendation>> GetAllRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var results = await context.ThresholdRecommendations
            .AsNoTracking()
            .GroupJoin(context.Users,
                tr => tr.ReviewedByUserId,
                u => u.Id,
                (tr, users) => new { tr, users })
            .SelectMany(x => x.users.DefaultIfEmpty(),
                (x, user) => new
                {
                    x.tr,
                    ReviewedByUsername = user != null ? user.Email : null
                })
            .OrderByDescending(x => x.tr.CreatedAt)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.tr.ToModel(r.ReviewedByUsername)).ToList();
    }

    public async Task AddRecommendationAsync(ThresholdRecommendation recommendation, CancellationToken cancellationToken = default)
    {
        await InsertAsync(recommendation, cancellationToken);
    }

    public async Task ApplyRecommendationAsync(long id, string userId, CancellationToken cancellationToken = default)
    {
        await UpdateStatusAsync(id, "applied", userId, "Recommendation applied by admin", cancellationToken);
    }

    public async Task RejectRecommendationAsync(long id, string userId, string reason, CancellationToken cancellationToken = default)
    {
        await UpdateStatusAsync(id, "rejected", userId, reason, cancellationToken);
    }
}
