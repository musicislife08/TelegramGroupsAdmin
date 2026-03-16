using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing file scan quota tracking (Phase 4.17 - Phase 2: Cloud Queue)
/// Supports calendar-based daily/monthly quotas and rolling window tracking
/// Thread-safe via IDbContextFactory pattern
/// </summary>
public class FileScanQuotaRepository : IFileScanQuotaRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<FileScanQuotaRepository> _logger;

    public FileScanQuotaRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<FileScanQuotaRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<bool> IsQuotaAvailableAsync(
        string serviceName,
        string quotaType,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Find current quota window for this service
        var currentQuota = await context.FileScanQuotas
            .AsNoTracking()
            .Where(q =>
                q.Service == serviceName &&
                q.QuotaType == quotaType &&
                q.QuotaWindowStart <= now &&
                q.QuotaWindowEnd > now)
            .FirstOrDefaultAsync(cancellationToken)
            ;

        if (currentQuota == null)
        {
            // No quota record exists for current window = quota available
            _logger.LogDebug("No quota record found for {Service} ({QuotaType}), quota available",
                serviceName, quotaType);
            return true;
        }

        bool isAvailable = currentQuota.Count < currentQuota.LimitValue;

        _logger.LogDebug("Quota check for {Service} ({QuotaType}): {Count}/{Limit} = {Available}",
            serviceName, quotaType, currentQuota.Count, currentQuota.LimitValue,
            isAvailable ? "AVAILABLE" : "EXHAUSTED");

        return isAvailable;
    }

    public async Task IncrementQuotaUsageAsync(
        string serviceName,
        string quotaType,
        int limitValue,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Calculate quota window based on type
        var (windowStart, windowEnd) = CalculateQuotaWindow(quotaType, now);

        // Find or create quota record for current window
        var quota = await context.FileScanQuotas
            .Where(q =>
                q.Service == serviceName &&
                q.QuotaType == quotaType &&
                q.QuotaWindowStart == windowStart)
            .FirstOrDefaultAsync(cancellationToken)
            ;

        if (quota == null)
        {
            // Create new quota record
            quota = new DataModels.FileScanQuotaRecord
            {
                Service = serviceName,
                QuotaType = quotaType,
                QuotaWindowStart = windowStart,
                QuotaWindowEnd = windowEnd,
                Count = 1,
                LimitValue = limitValue,
                LastUpdated = now
            };

            context.FileScanQuotas.Add(quota);

            _logger.LogInformation("Created new quota window for {Service} ({QuotaType}): {Start} to {End}, initial count=1",
                serviceName, quotaType, windowStart, windowEnd);
        }
        else
        {
            // Increment existing quota
            quota.Count++;
            quota.LastUpdated = now;

            _logger.LogDebug("Incremented quota for {Service} ({QuotaType}): {Count}/{Limit}",
                serviceName, quotaType, quota.Count, quota.LimitValue);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Calculate quota window boundaries based on quota type
    /// </summary>
    private (DateTimeOffset WindowStart, DateTimeOffset WindowEnd) CalculateQuotaWindow(string quotaType, DateTimeOffset now)
    {
        return quotaType.ToLowerInvariant() switch
        {
            "daily" => CalculateDailyWindow(now),
            "monthly" => CalculateMonthlyWindow(now),
            _ => throw new ArgumentException($"Unknown quota type: {quotaType}", nameof(quotaType))
        };
    }

    /// <summary>
    /// Calculate daily quota window (midnight UTC to midnight UTC next day)
    /// </summary>
    private (DateTimeOffset WindowStart, DateTimeOffset WindowEnd) CalculateDailyWindow(DateTimeOffset now)
    {
        var startOfDay = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1);

        return (startOfDay, endOfDay);
    }

    /// <summary>
    /// Calculate monthly quota window (first of month to first of next month)
    /// </summary>
    private (DateTimeOffset WindowStart, DateTimeOffset WindowEnd) CalculateMonthlyWindow(DateTimeOffset now)
    {
        var firstOfMonth = new DateTimeOffset(new DateTime(now.Year, now.Month, 1), TimeSpan.Zero);
        var firstOfNextMonth = firstOfMonth.AddMonths(1);

        return (firstOfMonth, firstOfNextMonth);
    }

    public async Task<List<FileScanQuotaModel>> GetAllActiveQuotasAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Get all quotas where window_end > now (still active)
        var quotas = await context.FileScanQuotas
            .Where(q => q.QuotaWindowEnd > now)
            .OrderBy(q => q.Service)
            .ThenBy(q => q.QuotaType)
            .ToListAsync(cancellationToken);

        return quotas.Select(q => q.ToModel()).ToList();
    }
}
