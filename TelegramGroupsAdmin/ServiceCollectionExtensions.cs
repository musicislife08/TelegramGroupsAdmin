using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Polly;
using Polly.RateLimiting;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Vision;

namespace TelegramGroupsAdmin;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Web UI data services (Identity repositories + TOTP protection + Data Protection API)
    /// </summary>
    public static IServiceCollection AddTgSpamWebDataServices(
        this IServiceCollection services,
        string dataProtectionKeysPath)
    {
        ConfigureDataProtection(services, dataProtectionKeysPath);

        // Identity-related repositories and services
        services.AddSingleton<ITotpProtectionService, TotpProtectionService>();
        services.AddScoped<Repositories.InviteRepository>();
        services.AddScoped<Repositories.VerificationTokenRepository>();

        // Note: UserRepository, AuditLogRepository, IMessageHistoryRepository are registered in TelegramGroupsAdmin.Telegram.Extensions.AddTelegramServices()

        return services;
    }

    /// <summary>
    /// Adds API data services (Message history repository only - no identity)
    /// </summary>

    /// <summary>
    /// Adds Blazor Server and MudBlazor services
    /// </summary>
    public static IServiceCollection AddBlazorServices(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Configure SignalR Hub options for Blazor Server (increase message size for image paste)
        services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = 20 * 1024 * 1024; // 20MB (allow for base64 overhead)
        });

        services.AddMudServices();
        services.AddHttpContextAccessor();

        // Add HttpClient for Blazor components (for calling our own API)
        services.AddScoped(sp =>
        {
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var httpContext = httpContextAccessor.HttpContext;

            if (httpContext == null) return new HttpClient { BaseAddress = new Uri("http://localhost:5161") };
            var host = httpContext.Request.Host;
            var hostString = host.Host == "0.0.0.0" ? $"localhost:{host.Port}" : host.ToString();
            var baseAddress = $"{httpContext.Request.Scheme}://{hostString}";
            return new HttpClient { BaseAddress = new Uri(baseAddress) };
        });

        return services;
    }

    /// <summary>
    /// Adds cookie-based authentication with proper security settings
    /// </summary>
    public static IServiceCollection AddCookieAuthentication(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "TgSpam.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.AccessDeniedPath = "/access-denied";
            });

        services.AddAuthorizationBuilder();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

        return services;
    }

    /// <summary>
    /// Adds application services (auth, users, messages, etc.)
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Auth services
        services.AddScoped<TelegramGroupsAdmin.Services.Auth.IPasswordHasher, TelegramGroupsAdmin.Services.Auth.PasswordHasher>();
        services.AddScoped<TelegramGroupsAdmin.Services.Auth.ITotpService, TelegramGroupsAdmin.Services.Auth.TotpService>();
        services.AddSingleton<TelegramGroupsAdmin.Services.Auth.IIntermediateAuthService, TelegramGroupsAdmin.Services.Auth.IntermediateAuthService>();

        // Core services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInviteService, InviteService>();
        services.AddScoped<IMessageExportService, MessageExportService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAuditService, AuditService>();

        // Backup service (replaces old UserDataExportService)
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.IBackupService, TelegramGroupsAdmin.Services.Backup.BackupService>();

        // Email service (SendGrid)
        services.AddScoped<TelegramGroupsAdmin.Services.Email.IEmailService, TelegramGroupsAdmin.Services.Email.SendGridEmailService>();

        // Message history adapter for spam detection library
        services.AddScoped<TelegramGroupsAdmin.ContentDetection.Services.IMessageHistoryService, MessageHistoryAdapter>();

        // Telegram photo service (chat icons, user profile photos)
        services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.TelegramPhotoService>();

        // Telegram media service (Phase 4.X: GIF, Video, Audio, Voice, Sticker, VideoNote, Document downloads)
        services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.TelegramMediaService>();

        // Runtime logging configuration service (Phase 4.7)
        services.AddSingleton<IRuntimeLoggingService, RuntimeLoggingService>();

        return services;
    }

    /// <summary>
    /// Adds HTTP clients with rate limiting and custom configurations
    /// </summary>
    public static IServiceCollection AddHttpClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // HybridCache for blocklists
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 10 * 1024 * 1024; // 10 MB
        });

        // HTTP clients
        services.AddHttpClient<SeoPreviewScraper>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SeoPreviewScraper/1.0)");
        });

        // VirusTotal rate limiter (4 requests/minute free tier)
        // Queue up to 10 requests to handle burst during file upload + analysis polling
        var limiter = PartitionedRateLimiter.Create<HttpRequestMessage, string>(_ =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: "virustotal",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 4,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,  // 15-second segments for smoother rate limiting
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10  // Queue up to 10 requests (analysis polling can generate 5-10 requests per file)
                }));

        var limiterOptions = new RateLimiterStrategyOptions
        {
            RateLimiter = async args =>
            {
                var request = args.Context.GetRequestMessage();
                if (request is null)
                {
                    return RejectedRateLimitLease.Instance;
                }

                var lease = await limiter.AcquireAsync(
                    request,
                    permitCount: 1,
                    cancellationToken: args.Context.CancellationToken);

                return lease.IsAcquired ? lease : RejectedRateLimitLease.Instance;
            },
            OnRejected = static _ => ValueTask.CompletedTask
        };

        // Named HttpClient for VirusTotal
        services.AddHttpClient("VirusTotal", client =>
            {
                client.BaseAddress = new Uri("https://www.virustotal.com/api/v3/");
                var apiKey = configuration["VirusTotal:ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    client.DefaultRequestHeaders.Add("x-apikey", apiKey);
                }
            })
            .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
            {
                resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
            });

        services.AddHttpClient();
        services.AddHttpClient<IVisionSpamDetectionService, OpenAIVisionSpamDetectionService>();

        return services;
    }

    // NOTE: AddTelegramServices() is now in TelegramGroupsAdmin.Telegram.Extensions
    // Call services.AddTelegramServices() to register all Telegram-related services

    /// <summary>
    /// Adds remaining application repositories (non-Telegram)
    /// NOTE: Telegram repositories are registered via AddTelegramServices()
    /// </summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Spam Detection repositories (from ContentDetection library)
        services.AddScoped<TelegramGroupsAdmin.ContentDetection.Repositories.IStopWordsRepository, TelegramGroupsAdmin.ContentDetection.Repositories.StopWordsRepository>();
        services.AddScoped<TelegramGroupsAdmin.ContentDetection.Repositories.ISpamDetectionConfigRepository, TelegramGroupsAdmin.ContentDetection.Repositories.SpamDetectionConfigRepository>();

        // Report Actions Service (uses repositories from Telegram library)
        services.AddScoped<IReportActionsService, ReportActionsService>();

        return services;
    }

    private static void ConfigureDataProtection(IServiceCollection services, string dataProtectionKeysPath)
    {
        // Create keys directory
        Directory.CreateDirectory(dataProtectionKeysPath);

        // Set restrictive permissions on keys directory (Linux/macOS only)
        if (!OperatingSystem.IsWindows())
        {
            // Get logger from service provider (early in startup, before app is built)
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetService<ILogger<IServiceCollection>>();

            try
            {
                // chmod 700 - only owner can read/write/execute
                File.SetUnixFileMode(dataProtectionKeysPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                logger?.LogInformation("Set permissions on {KeysPath} to 700", dataProtectionKeysPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to set Unix permissions on {KeysPath}", dataProtectionKeysPath);
            }
        }

        // Configure Data Protection API
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
            .SetApplicationName("TgSpamPreFilter");
    }

    /// <summary>
    /// Adds TickerQ background job system with PostgreSQL backend and job registrations
    /// </summary>
    public static IServiceCollection AddTickerQBackgroundJobs(this IServiceCollection services)
    {
        services.AddTickerQ(options =>
        {
            // Max concurrent jobs
            options.SetMaxConcurrency(4);

            // Polling interval: Check for due jobs every 5 seconds (default is 60s)
            // For smaller apps like this, faster polling improves responsiveness without significant overhead
            // File scanning feels more immediate (5s vs 60s delay)
            options.UpdateMissedJobCheckDelay(TimeSpan.FromSeconds(5));

            // Use EF Core for persistence (PostgreSQL via AppDbContext)
            options.AddOperationalStore<TelegramGroupsAdmin.Data.AppDbContext>(efOptions =>
            {
                // Only include TickerQ tables during design-time migrations
                efOptions.UseModelCustomizerForMigrations();
            });

            // Optional: Add dashboard UI at /tickerq-dashboard
            // options.AddDashboard(basePath: "/tickerq-dashboard");
            // options.AddDashboardBasicAuth();
        });

        // Register job classes (TickerQ discovers [TickerFunction] methods via source generators)
        services.AddScoped<Jobs.TestJob>();
        services.AddScoped<Jobs.WelcomeTimeoutJob>();
        services.AddScoped<Jobs.DeleteMessageJob>();
        services.AddScoped<Jobs.TempbanExpiryJob>();
        services.AddScoped<Jobs.BlocklistSyncJob>(); // Phase 4.13: URL Filtering

        return services;
    }
}

file sealed class RejectedRateLimitLease : RateLimitLease
{
    public static readonly RejectedRateLimitLease Instance = new();

    public override bool IsAcquired => false;
    public override IEnumerable<string> MetadataNames => [];
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = null;
        return false;
    }
}
