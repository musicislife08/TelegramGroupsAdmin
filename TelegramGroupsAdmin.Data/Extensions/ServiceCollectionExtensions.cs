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

        // EF Core DbContext Factory with pooling (works for both scoped and singleton scenarios)
        services.AddPooledDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Also register AppDbContext for DI (uses the factory internally)
        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            return factory.CreateDbContext();
        });

        return services;
    }
}
