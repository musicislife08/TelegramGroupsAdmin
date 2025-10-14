using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TelegramGroupsAdmin.Data.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all data layer services: Dapper (BackupService only), EF Core
    /// </summary>
    public static IServiceCollection AddDataServices(this IServiceCollection services, string connectionString)
    {
        // Npgsql data source for Dapper (BackupService only - infrastructure exception)
        services.AddNpgsqlDataSource(connectionString);

        // EF Core DbContext with pooling (scoped lifetime, automatically disposed)
        // Using AddDbContext instead of manual factory ensures proper disposal
        // Default tracking behavior - use .AsNoTracking() explicitly for read-only queries
        services.AddDbContext<AppDbContext>(
            options => options.UseNpgsql(connectionString),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // Also register factory for scenarios that need explicit context creation (background services)
        services.AddPooledDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
