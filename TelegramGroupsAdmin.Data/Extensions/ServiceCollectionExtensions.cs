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

        // EF Core DbContext with PostgreSQL provider
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        // EF Core DbContext Factory for services that need to create multiple contexts (BackupService)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
