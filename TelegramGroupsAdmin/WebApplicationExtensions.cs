using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TickerQ.DependencyInjection;
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

        // Configure static file serving for images
        ConfigureImageStaticFiles(app);

        // TickerQ background job processor
        app.UseTickerQ();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    /// <summary>
    /// Maps all API endpoints
    /// </summary>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
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
    /// Configures static file serving for uploaded images
    /// </summary>
    private static void ConfigureImageStaticFiles(WebApplication app)
    {
        var messageHistoryOptions = app.Services.GetRequiredService<IOptions<MessageHistoryOptions>>().Value;

        if (!messageHistoryOptions.Enabled)
        {
            return;
        }

        var imagesPath = Path.GetFullPath(messageHistoryOptions.ImageStoragePath);
        Directory.CreateDirectory(imagesPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(imagesPath),
            RequestPath = "/images",
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=86400");
            }
        });

        app.Logger.LogInformation("Configured static file serving for images at {ImagesPath}", imagesPath);
    }
}
