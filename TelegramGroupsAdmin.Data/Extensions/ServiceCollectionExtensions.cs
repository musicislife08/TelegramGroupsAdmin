using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.Data.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all data layer services: single NpgsqlDataSource pool shared by EF Core and raw ADO.NET
    /// </summary>
    public static IServiceCollection AddDataServices(this IServiceCollection services, string connectionString)
    {
        // Single NpgsqlDataSource — the ONE connection pool for all app database access.
        // EF Core (via pooled factory) and raw ADO.NET services (BackupService, etc.) share this pool.
        // Quartz.NET is the only consumer with its own separate pool (API limitation).
        services.AddNpgsqlDataSource(connectionString);

        // Pooled DbContext factory — background services and scoped contexts share this.
        // Uses the registered NpgsqlDataSource (resolved from DI) so all connections come from one pool.
        services.AddPooledDbContextFactory<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 6,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                })
                .ConfigureWarnings(w => w
                    .Ignore(RelationalEventId.PendingModelChangesWarning)
                    .Ignore(RelationalEventId.MultipleCollectionIncludeWarning)));

        // Scoped DbContext derived from the pooled factory — for repositories, Blazor components, etc.
        // DI container calls Dispose() at end of scope, which returns context to the pool.
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        // Migration history compaction service (runs before EF Core migrations)
        services.AddScoped<IMigrationHistoryCompactionService, MigrationHistoryCompactionService>();

        return services;
    }
}
