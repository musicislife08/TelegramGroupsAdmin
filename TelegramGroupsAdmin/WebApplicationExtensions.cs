using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Components;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Endpoints;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin;

public static class WebApplicationExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Configures the HTTP request pipeline with standard middleware
        /// </summary>
        public WebApplication ConfigurePipeline()
        {
            // Configure forwarded headers for reverse proxy support (SWAG, nginx, etc.)
            // This allows the app to understand the original HTTPS request from clients
            // even though it's running on HTTP inside the container
            var forwardedHeadersOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            // Trust all proxies (Docker internal network)
            // In production behind a reverse proxy, the proxy is the only source of requests
            forwardedHeadersOptions.KnownIPNetworks.Clear();
            forwardedHeadersOptions.KnownProxies.Clear();

            app.UseForwardedHeaders(forwardedHeadersOptions);

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // Serve static files from wwwroot
            // UseStaticFiles is needed for running in Production mode from source (e.g., Rider debugging)
            // MapStaticAssets (below) provides optimized delivery when running from published output
            app.UseStaticFiles();

            // Configure static file serving for images
            ConfigureImageStaticFiles(app);

            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapStaticAssets();

            return app;
        }

        /// <summary>
        /// Maps all API endpoints
        /// </summary>
        public WebApplication MapApiEndpoints()
        {
            // Kubernetes/Docker health check endpoints
            // Liveness probe: Returns 200 if app is alive and responsive (no dependency checks)
            // Used by Kubernetes to determine if container should be restarted
            // Database failures should NOT cause liveness to fail (restarting won't fix database issues)
            app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = _ => false // Exclude all checks - just confirms app is responsive
            })
            .AllowAnonymous();

            // Readiness probe: Returns 200 if app is ready to handle requests (includes database check)
            // Used by Kubernetes to determine if container should receive traffic
            // If database is down, app becomes "not ready" but doesn't restart
            app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = healthCheck => healthCheck.Tags.Contains("ready")
            })
            .AllowAnonymous();

            app.MapAuthEndpoints();
            app.MapEmailVerificationEndpoints();

            return app;
        }

        /// <summary>
        /// Runs database migrations using EF Core
        /// </summary>
        public async Task RunDatabaseMigrationsAsync()
        {
            app.Logger.LogInformation("Running PostgreSQL database migrations (EF Core)");

            using var scope = app.Services.CreateScope();

            // Pre-migration: Check if history compaction is needed
            var compactionService = scope.ServiceProvider.GetRequiredService<IMigrationHistoryCompactionService>();
            var compactionResult = await compactionService.CompactIfEligibleAsync();

            switch (compactionResult)
            {
                case MigrationCompactionResult.FreshDatabase:
                    app.Logger.LogInformation("Fresh database - applying all migrations");
                    break;
                case MigrationCompactionResult.Compacted:
                    app.Logger.LogInformation("Migration history compacted to baseline");
                    break;
                case MigrationCompactionResult.NoActionNeeded:
                    app.Logger.LogDebug("Migration history at or past baseline");
                    break;
                case MigrationCompactionResult.IncompatibleState:
                    // Service already logged the error with version guidance
                    Environment.Exit(1);
                    return;
            }

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();

            app.Logger.LogInformation("PostgreSQL database migration complete");

            // One-time migration: Populate api_keys column from environment variables
            try
            {
                var apiKeyMigration = scope.ServiceProvider.GetRequiredService<ApiKeyMigrationService>();
                await apiKeyMigration.MigrateApiKeysFromEnvironmentAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to migrate API keys from environment variables (non-fatal)");
            }

            // One-time backfill: Populate similarity_hash columns for SimHash deduplication
            try
            {
                var hashBackfill = scope.ServiceProvider.GetRequiredService<SimilarityHashBackfillService>();
                await hashBackfill.BackfillAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to backfill similarity hashes (non-fatal)");
            }

            // Seed default ban celebration captions if empty
            try
            {
                var captionRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationCaptionRepository>();
                await captionRepository.SeedDefaultsIfEmptyAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to seed default ban celebration captions (non-fatal)");
            }
        }
    }

    /// <summary>
    /// Configures static file serving for all uploaded media (images, videos, audio, user photos, chat icons, etc.)
    /// </summary>
    private static void ConfigureImageStaticFiles(WebApplication app)
    {
        var messageHistoryOptions = app.Services.GetRequiredService<IOptions<MessageHistoryOptions>>().Value;

        if (!messageHistoryOptions.Enabled)
        {
            return;
        }

        // Base data path (e.g., /data or ./bin/Debug/net10.0/data)
        var basePath = Path.GetFullPath(messageHistoryOptions.ImageStoragePath);

        // Serve media/ subdirectory (all user-uploaded content: images, videos, audio, user photos, chat icons, etc.)
        var mediaPath = Path.Combine(basePath, "media");
        Directory.CreateDirectory(mediaPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaPath),
            RequestPath = "/media",
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=86400");
            }
        });

        app.Logger.LogInformation("Configured static file serving for media at {MediaPath}", mediaPath);
    }
}
