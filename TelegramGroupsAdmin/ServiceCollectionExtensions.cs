using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Polly;
using Polly.RateLimiting;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Core.Services;

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
        services.AddSingleton<IDataProtectionService, DataProtectionService>();
        services.AddScoped<Repositories.InviteRepository>();
        services.AddScoped<Repositories.VerificationTokenRepository>();
        services.AddScoped<Repositories.INotificationPreferencesRepository, Repositories.NotificationPreferencesRepository>(); // Phase 5.1

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

        // Add authorization policies
        services.AddAuthorizationBuilder()
            .AddPolicy("GlobalAdminOrOwner", policy =>
                policy.RequireRole("GlobalAdmin", "Owner"))
            .AddPolicy("OwnerOnly", policy =>
                policy.RequireRole("Owner"));

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

        return services;
    }

    /// <summary>
    /// Adds application services (auth, users, messages, etc.)
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // PERF-CFG-1: Memory cache for configuration caching (95% query reduction)
        services.AddMemoryCache();

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
        services.AddScoped<BlazorAuthHelper>(); // Authentication context extraction helper for UI components

        // Prompt builder service (Phase 4.X: AI-powered prompt generation)
        services.AddScoped<TelegramGroupsAdmin.Services.PromptBuilder.IPromptBuilderService, TelegramGroupsAdmin.Services.PromptBuilder.PromptBuilderService>();

        // Backup services (replaces old UserDataExportService)
        services.AddBackupServices();

        // Email service (SendGrid)
        services.AddScoped<TelegramGroupsAdmin.Services.Email.IEmailService, TelegramGroupsAdmin.Services.Email.SendGridEmailService>();

        // Notification service (Phase 5.1: User notification preferences with Telegram DM + Email channels)
        services.AddScoped<INotificationService, NotificationService>();

        // Message history adapter for spam detection library
        services.AddScoped<TelegramGroupsAdmin.ContentDetection.Services.IMessageHistoryService, MessageHistoryAdapter>();

        // Media refetch services (Phase 4.X: Re-download missing media after restore)
        services.AddSingleton<TelegramGroupsAdmin.Services.Media.IMediaNotificationService, TelegramGroupsAdmin.Services.Media.MediaNotificationService>();
        services.AddSingleton<TelegramGroupsAdmin.Services.Media.IMediaRefetchQueueService, TelegramGroupsAdmin.Services.Media.MediaRefetchQueueService>();
        services.AddHostedService<TelegramGroupsAdmin.Services.Media.MediaRefetchWorkerService>();

        // Runtime logging configuration service (Phase 4.7)
        services.AddSingleton<IRuntimeLoggingService, RuntimeLoggingService>();

        // Background jobs configuration service
        services.AddScoped<IBackgroundJobConfigService, BackgroundJobConfigService>();

        // Recurring job scheduler (Phase 4.X: Generic TickerQ job scheduler with interval/cron support)
        services.AddHostedService<Services.BackgroundServices.RecurringJobSchedulerService>();

        // API key migration service (one-time migration from env vars to encrypted database storage)
        services.AddScoped<ApiKeyMigrationService>();


        // Documentation service (Phase 4.X: Folder-based portable markdown documentation)
        services.AddSingleton<Services.Docs.IDocumentationService, Services.Docs.DocumentationService>();
        services.AddHostedService<Services.Docs.DocumentationStartupService>();

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

        // Named HttpClient for VirusTotal (with dynamic API key from database)
        services.AddHttpClient("VirusTotal", client =>
            {
                client.BaseAddress = new Uri("https://www.virustotal.com/api/v3/");
            })
            .AddHttpMessageHandler(sp => new Services.ApiKeyDelegatingHandler(
                sp,
                configuration,
                serviceName: "VirusTotal",
                headerName: "x-apikey"))
            .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
            {
                resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
            });

        // Named HttpClient for OpenAI (shared by all OpenAI services)
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "TelegramGroupsAdmin/1.0");

            var apiKey = configuration["OpenAI:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
        });

        services.AddHttpClient();

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

        // Analytics repository (Phase 5: Performance metrics)
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

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
            try
            {
                // chmod 700 - only owner can read/write/execute
                File.SetUnixFileMode(dataProtectionKeysPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Console.WriteLine($"[DataProtection] Set permissions on {dataProtectionKeysPath} to 700");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataProtection] Warning: Failed to set Unix permissions on {dataProtectionKeysPath}: {ex.Message}");
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
    public static IServiceCollection AddTickerQBackgroundJobs(
        this IServiceCollection services,
        IHostEnvironment environment)
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

            // Dashboard UI at /tickerq-dashboard (development only)
            if (environment.IsDevelopment())
            {
                options.AddDashboard(dbopt =>
                {
                    dbopt.BasePath = "/tickerq-dashboard";
                    dbopt.EnableBasicAuth = false;
                });
            }
        });

        // Register job classes (TickerQ discovers [TickerFunction] methods via source generators)
        services.AddScoped<Jobs.WelcomeTimeoutJob>();
        services.AddScoped<Jobs.DeleteMessageJob>();
        services.AddScoped<Jobs.TempbanExpiryJob>();
        services.AddScoped<Jobs.BlocklistSyncJob>(); // Phase 4.13: URL Filtering
        services.AddScoped<Jobs.RefreshUserPhotosJob>(); // Phase 4.X: Nightly user photo refresh
        services.AddScoped<Jobs.ScheduledBackupJob>(); // Background Jobs Management: Automatic database backups
        services.AddScoped<Jobs.RotateBackupPassphraseJob>(); // Backup passphrase rotation with re-encryption
        services.AddScoped<Jobs.DatabaseMaintenanceJob>(); // Background Jobs Management: PostgreSQL maintenance (STUB)
        services.AddScoped<Jobs.ChatHealthCheckJob>(); // Phase 4.X: Chat health monitoring (replaces PeriodicTimer)

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

/// <summary>
/// Extension methods for registering backup services
/// Used by both main app and tests to ensure consistent registration
/// </summary>
public static class BackupServiceCollectionExtensions
{
    /// <summary>
    /// Add backup services and handlers to DI container
    /// </summary>
    public static IServiceCollection AddBackupServices(this IServiceCollection services)
    {
        // Core backup services
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.IBackupService, TelegramGroupsAdmin.Services.Backup.BackupService>();
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.IBackupEncryptionService, TelegramGroupsAdmin.Services.Backup.BackupEncryptionService>();
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.BackupRetentionService>();

        // Backup configuration and passphrase management (REFACTOR-2)
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.IBackupConfigurationService, TelegramGroupsAdmin.Services.Backup.BackupConfigurationService>();
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.IPassphraseManagementService, TelegramGroupsAdmin.Services.Backup.PassphraseManagementService>();

        // Backup handlers (REFACTOR-2 - internal implementation details)
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.Handlers.TableDiscoveryService>();
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.Handlers.TableExportService>();
        services.AddScoped<TelegramGroupsAdmin.Services.Backup.Handlers.DependencyResolutionService>();

        return services;
    }
}
