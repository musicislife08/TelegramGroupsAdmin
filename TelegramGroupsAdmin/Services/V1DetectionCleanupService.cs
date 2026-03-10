using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// One-time cleanup of historical V1 detection records for CAS and SeoScraping.
/// Both checks no longer run in V2: CAS moved to WelcomeService (user join flow),
/// SeoScraping absorbed into IUrlContentScrapingService as a preprocessing step.
/// These records show misleading 0% hit rates in analytics.
/// Can be removed after it has run on the production database.
/// </summary>
public class V1DetectionCleanupService(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<V1DetectionCleanupService> logger)
{
    private static readonly string[] ObsoleteCheckNames = ["CAS", "SeoScraping"];

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var count = await context.DetectionResults
            .Where(dr => ObsoleteCheckNames.Contains(dr.DetectionMethod))
            .ExecuteDeleteAsync(cancellationToken);

        if (count > 0)
        {
            logger.LogInformation("Cleaned up {Count} historical V1 detection records (CAS, SeoScraping)", count);
        }
        else
        {
            logger.LogDebug("No historical V1 detection records to clean up");
        }
    }
}
