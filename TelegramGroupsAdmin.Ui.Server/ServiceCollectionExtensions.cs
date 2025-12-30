using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Polly;
using Polly.RateLimiting;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Ui.Server.Auth;
using TelegramGroupsAdmin.Ui.Server.Constants;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services;
using TelegramGroupsAdmin.Ui.Server.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;
using TelegramGroupsAdmin.Ui.Server.Services.Email;
using TelegramGroupsAdmin.Ui.Server.Services.Docs;
using TelegramGroupsAdmin.Ui.Server.Services.PromptBuilder;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Ui.Server;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Web UI data services (Identity repositories + TOTP protection + Data Protection API)
        /// </summary>
        public IServiceCollection AddWebDataServices(
            string dataProtectionKeysPath)
        {
            ConfigureDataProtection(services, dataProtectionKeysPath);

            // Identity-related repositories and services
            services.AddSingleton<IDataProtectionService, DataProtectionService>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IInviteRepository, InviteRepository>();
            services.AddScoped<IVerificationTokenRepository, VerificationTokenRepository>();
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
                options.MaximumReceiveMessageSize = BlazorConstants.MaxSignalRMessageSize;
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
                    options.Cookie.Name = AuthenticationConstants.CookieName;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = environment.IsDevelopment()
                        ? CookieSecurePolicy.None
                        : CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = AuthenticationConstants.CookieExpiration;
                    options.SlidingExpiration = true;
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.AccessDeniedPath = "/access-denied";

                    // SECURITY: Validate SecurityStamp on each request to invalidate sessions
                    // after password change, TOTP reset, or other security-sensitive changes
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        var cookieSecurityStamp = context.Principal?.FindFirst(CustomClaimTypes.SecurityStamp)?.Value;

                        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(cookieSecurityStamp))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            return;
                        }

                        // Get user repository from DI to check current SecurityStamp
                        var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                        var user = await userRepository.GetByIdAsync(userId, context.HttpContext.RequestAborted);

                        // Reject if user not found or SecurityStamp doesn't match
                        if (user is null || user.SecurityStamp != cookieSecurityStamp)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    };
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
            services.AddScoped<TelegramGroupsAdmin.Ui.Server.Services.Auth.IAuthCookieService, TelegramGroupsAdmin.Ui.Server.Services.Auth.AuthCookieService>();

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
            services.AddSingleton<TelegramGroupsAdmin.Ui.Server.Services.Auth.IPasswordHasher, TelegramGroupsAdmin.Ui.Server.Services.Auth.PasswordHasher>();
            services.AddScoped<TelegramGroupsAdmin.Ui.Server.Services.Auth.ITotpService, TelegramGroupsAdmin.Ui.Server.Services.Auth.TotpService>();
            services.AddSingleton<TelegramGroupsAdmin.Ui.Server.Services.Auth.IIntermediateAuthService, TelegramGroupsAdmin.Ui.Server.Services.Auth.IntermediateAuthService>();
            services.AddSingleton<TelegramGroupsAdmin.Ui.Server.Services.Auth.IRateLimitService, TelegramGroupsAdmin.Ui.Server.Services.Auth.RateLimitService>(); // SECURITY-5
            services.AddScoped<TelegramGroupsAdmin.Ui.Server.Services.Auth.IAccountLockoutService, TelegramGroupsAdmin.Ui.Server.Services.Auth.AccountLockoutService>(); // SECURITY-6

            // Core services
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IProfilePageService, ProfilePageService>();
            services.AddScoped<IInviteService, InviteService>();
            services.AddScoped<IMessageExportService, MessageExportService>();
            services.AddScoped<IUserManagementService, UserManagementService>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IFeatureAvailabilityService, FeatureAvailabilityService>(); // FEATURE-5.3: Check external service configuration status
            services.AddScoped<BlazorAuthHelper>(); // Authentication context extraction helper for UI components

            // Prompt builder service (Phase 4.X: AI-powered prompt generation)
            services.AddScoped<IPromptBuilderService, PromptBuilderService>();

            // Email service (SendGrid)
            services.AddScoped<IEmailService, SendGridEmailService>();

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

            // Message context adapter for spam detection library
            services.AddScoped<TelegramGroupsAdmin.ContentDetection.Services.IMessageContextProvider, MessageContextAdapter>();

            // Media refetch services (Phase 4.X: Re-download missing media after restore)
            services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.Media.IMediaNotificationService, TelegramGroupsAdmin.Telegram.Services.Media.MediaNotificationService>();
            services.AddSingleton<TelegramGroupsAdmin.Telegram.Services.Media.IMediaRefetchQueueService, TelegramGroupsAdmin.Telegram.Services.Media.MediaRefetchQueueService>();
            services.AddHostedService<TelegramGroupsAdmin.Telegram.Services.Media.MediaRefetchWorkerService>();

            // Runtime logging configuration service (Phase 4.7)
            services.AddSingleton<IRuntimeLoggingService, RuntimeLoggingService>();

            // API key migration service (one-time migration from env vars to encrypted database storage)
            services.AddScoped<ApiKeyMigrationService>();

            // Similarity hash backfill service (one-time migration for SimHash deduplication)
            services.AddScoped<SimilarityHashBackfillService>();

            // Documentation service (Phase 4.X: Folder-based portable markdown documentation)
            services.AddSingleton<IDocumentationService, DocumentationService>();
            services.AddHostedService<DocumentationStartupService>();

            // Auth cookie service (WASM auth - generates encrypted cookies for API login)
            services.AddScoped<IAuthCookieService, AuthCookieService>();

            // SSE connection manager (WASM real-time updates)
            services.AddSingleton<SseConnectionManager>();

            // SSE event bridge - connects message processing events to SSE
            services.AddHostedService<SseEventBridgeService>();

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
                options.MaximumPayloadBytes = HttpConstants.HybridCacheMaxPayloadBytes;
            });

            // HTTP clients
            services.AddHttpClient<SeoPreviewScraper>(client =>
            {
                client.Timeout = HttpConstants.SeoScraperTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SeoPreviewScraper/1.0)");
            });

            // VirusTotal rate limiter (4 requests/minute free tier)
            // Queue up to 10 requests to handle burst during file upload + analysis polling
            var limiter = PartitionedRateLimiter.Create<HttpRequestMessage, string>(_ =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "virustotal",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = HttpConstants.VirusTotalPermitLimit,
                        Window = HttpConstants.VirusTotalWindow,
                        SegmentsPerWindow = HttpConstants.VirusTotalSegmentsPerWindow,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = HttpConstants.VirusTotalQueueLimit
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
