using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Polly;
using Polly.RateLimiting;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Web UI data services (Identity repositories + TOTP protection + Data Protection API)
        /// </summary>
        public IServiceCollection AddTgSpamWebDataServices(
            string dataProtectionKeysPath)
        {
            ConfigureDataProtection(services, dataProtectionKeysPath);

            // Identity-related repositories and services
            services.AddSingleton<IDataProtectionService, DataProtectionService>();
            services.AddScoped<Repositories.IUserRepository, Repositories.UserRepository>();
            services.AddScoped<Repositories.IInviteRepository, Repositories.InviteRepository>();
            services.AddScoped<Repositories.IVerificationTokenRepository, Repositories.VerificationTokenRepository>();
            services.AddScoped<Core.Repositories.INotificationPreferencesRepository, Core.Repositories.NotificationPreferencesRepository>();
            services.AddScoped<Core.Repositories.IWebNotificationRepository, Core.Repositories.WebNotificationRepository>();

            // Note: AuditLogRepository is registered in AddCoreServices(), IMessageHistoryRepository in AddTelegramServices()

            return services;
        }

        /// <summary>
        /// Adds API data services (Message history repository only - no identity)
        /// </summary>

        /// <summary>
        /// Adds Blazor Server and MudBlazor services
        /// </summary>
        public IServiceCollection AddBlazorServices()
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
        public IServiceCollection AddCookieAuthentication(
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

            // Auth cookie service for programmatic cookie generation (used by app and tests)
            services.AddScoped<TelegramGroupsAdmin.Services.Auth.IAuthCookieService, TelegramGroupsAdmin.Services.Auth.AuthCookieService>();

            return services;
        }

        /// <summary>
        /// Adds application services (auth, users, messages, etc.)
        /// </summary>
        public IServiceCollection AddApplicationServices()
        {
            // PERF-CFG-1: Memory cache for configuration caching (95% query reduction)
            services.AddMemoryCache();

            // Auth services
            services.AddScoped<TelegramGroupsAdmin.Services.Auth.IPasswordHasher, TelegramGroupsAdmin.Services.Auth.PasswordHasher>();
            services.AddScoped<TelegramGroupsAdmin.Services.Auth.ITotpService, TelegramGroupsAdmin.Services.Auth.TotpService>();
            services.AddSingleton<TelegramGroupsAdmin.Services.Auth.IIntermediateAuthService, TelegramGroupsAdmin.Services.Auth.IntermediateAuthService>();
            services.AddSingleton<TelegramGroupsAdmin.Services.Auth.IRateLimitService, TelegramGroupsAdmin.Services.Auth.RateLimitService>(); // SECURITY-5
            services.AddScoped<TelegramGroupsAdmin.Services.Auth.IAccountLockoutService, TelegramGroupsAdmin.Services.Auth.AccountLockoutService>(); // SECURITY-6

            // Core services
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IInviteService, InviteService>();
            services.AddScoped<IMessageExportService, MessageExportService>();
            services.AddScoped<IUserManagementService, UserManagementService>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IFeatureAvailabilityService, FeatureAvailabilityService>(); // FEATURE-5.3: Check external service configuration status
            services.AddScoped<BlazorAuthHelper>(); // Authentication context extraction helper for UI components

            // Prompt builder service (Phase 4.X: AI-powered prompt generation)
            services.AddScoped<Services.PromptBuilder.IPromptBuilderService, Services.PromptBuilder.PromptBuilderService>();

            // Backup services (replaces old UserDataExportService)
            services.AddBackupServices();

            // Email service (SendGrid)
            services.AddScoped<Services.Email.IEmailService, Services.Email.SendGridEmailService>();

            // Notification services (User notification preferences with Telegram DM, Email, and Web Push channels)
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IWebPushNotificationService, WebPushNotificationService>();
            services.AddScoped<NotificationStateService>(); // Blazor state for notification bell

            // Web Push browser notifications (PushServiceClient + VAPID auto-generation)
            services.AddHttpClient<Lib.Net.Http.WebPush.PushServiceClient>();
            services.AddHostedService<VapidKeyGenerationService>(); // Auto-generates VAPID keys on first startup
            services.AddHostedService<AIProviderMigrationService>(); // Migrates OpenAI config to multi-provider format

            // Push subscriptions repository (browser push endpoints)
            services.AddScoped<Core.Repositories.IPushSubscriptionsRepository, Core.Repositories.PushSubscriptionsRepository>();

            // Message history adapter for spam detection library
            services.AddScoped<TelegramGroupsAdmin.ContentDetection.Services.IMessageHistoryService, MessageHistoryAdapter>();

            // Media refetch services (Phase 4.X: Re-download missing media after restore)
            services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.Media.IMediaNotificationService, TelegramGroupsAdmin.Telegram.Services.Media.MediaNotificationService>();
            services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.Media.IMediaRefetchQueueService, TelegramGroupsAdmin.Telegram.Services.Media.MediaRefetchQueueService>();
            services.AddHostedService<TelegramGroupsAdmin.Telegram.Services.Media.MediaRefetchWorkerService>();

            // Runtime logging configuration service (Phase 4.7)
            services.AddSingleton<IRuntimeLoggingService, RuntimeLoggingService>();

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
        public IServiceCollection AddHttpClients(
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
                .AddHttpMessageHandler(sp => new ApiKeyDelegatingHandler(
                    sp,
                    configuration,
                    serviceName: "VirusTotal",
                    headerName: "x-apikey"))
                .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
                {
                    resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
                });

            // Note: OpenAI HttpClient removed - now using Semantic Kernel via IChatService
            // which handles multiple providers (OpenAI, Azure OpenAI, local endpoints)

            services.AddHttpClient();

            return services;
        }

        // NOTE: AddTelegramServices() is now in TelegramGroupsAdmin.Telegram.Extensions
        // Call services.AddTelegramServices() to register all Telegram-related services

        /// <summary>
        /// Adds remaining application repositories (non-Telegram)
        /// NOTE: Telegram repositories are registered via AddTelegramServices()
        /// </summary>
        public IServiceCollection AddRepositories()
        {
            // Spam Detection repositories (from ContentDetection library)
            services.AddScoped<TelegramGroupsAdmin.ContentDetection.Repositories.IStopWordsRepository, TelegramGroupsAdmin.ContentDetection.Repositories.StopWordsRepository>();
            services.AddScoped<TelegramGroupsAdmin.ContentDetection.Repositories.IContentDetectionConfigRepository, TelegramGroupsAdmin.ContentDetection.Repositories.ContentDetectionConfigRepository>();

            // Analytics repository (Phase 5: Performance metrics, from ContentDetection library)
            services.AddScoped<TelegramGroupsAdmin.ContentDetection.Repositories.IAnalyticsRepository, TelegramGroupsAdmin.ContentDetection.Repositories.AnalyticsRepository>();

            // Report Actions Service (uses repositories from Telegram library)
            services.AddScoped<IReportActionsService, ReportActionsService>();

            return services;
        }

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
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Add backup services and handlers to DI container
        /// </summary>
        public IServiceCollection AddBackupServices()
        {
            // Note: Backup services are now registered by AddBackgroundJobs() in BackgroundJobs library

            return services;
        }
    }
}
