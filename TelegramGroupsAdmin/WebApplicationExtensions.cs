using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TelegramGroupsAdmin.Components;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Endpoints;

namespace TelegramGroupsAdmin;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Configures the HTTP request pipeline with standard middleware
    /// </summary>
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
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

        // TickerQ background job processor (dashboard configured in AddTickerQBackgroundJobs())
        app.UseTickerQ();

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
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        // Health check endpoint for Docker/Kubernetes readiness/liveness probes
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
        }))
        .WithName("HealthCheck")
        .WithTags("Health")
        .AllowAnonymous();

        app.MapAuthEndpoints();
        app.MapEmailVerificationEndpoints();

        return app;
    }

    /// <summary>
    /// Runs database migrations using EF Core
    /// </summary>
    public static async Task RunDatabaseMigrationsAsync(this WebApplication app, string connectionString)
    {
        app.Logger.LogInformation("Running PostgreSQL database migrations (EF Core)");

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.MigrateAsync();

        app.Logger.LogInformation("PostgreSQL database migration complete");
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
