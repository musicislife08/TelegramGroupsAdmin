using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Http;
using TelegramGroupsAdmin.Core.Metrics;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Metrics
        services.AddSingleton<ApiMetrics>();
        services.AddSingleton<CacheMetrics>();

        // Utility services
        services.AddSingleton<SimHashService>(); // SimHash fingerprinting for O(1) deduplication

        // Guarded HttpClient for URLs that come from users or admins (public addresses only).
        // The allowance stays NoFetchAllowance here; only a test host registers another.
        services.AddPublicUrlFetcher();

        // Audit services
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
