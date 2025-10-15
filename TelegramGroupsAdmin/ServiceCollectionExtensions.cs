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
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.BackgroundServices;
using TelegramGroupsAdmin.Services.BotCommands;
using TelegramGroupsAdmin.Services.Telegram;
using TelegramGroupsAdmin.Services.Vision;
using Commands = TelegramGroupsAdmin.Services.BotCommands.Commands;

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
        services.AddScoped<UserRepository>();
        services.AddScoped<InviteRepository>();
        services.AddScoped<AuditLogRepository>();
        services.AddScoped<VerificationTokenRepository>();

        // Message history repository (read-only for Web UI)
        services.AddScoped<MessageHistoryRepository>();

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

        // Moderation service (shared by bot commands and UI)
        services.AddScoped<ModerationActionService>();

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

        // VirusTotal rate limiter
        var limiter = PartitionedRateLimiter.Create<HttpRequestMessage, string>(_ =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: "virustotal",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 4,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 1,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
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

    /// <summary>
    /// Adds Telegram bot services and commands
    /// </summary>
    public static IServiceCollection AddTelegramServices(this IServiceCollection services)
    {
        services.AddSingleton<TelegramBotClientFactory>();
        services.AddScoped<ITelegramImageService, TelegramImageService>();

        // Bot command system
        // Commands are Scoped (to allow injecting Scoped services like ModerationActionService)
        // CommandRouter is Singleton (creates scopes internally when executing commands)
        // Register both as interface and concrete type for CommandRouter resolution
        services.AddScoped<Commands.HelpCommand>();
        services.AddScoped<IBotCommand, Commands.HelpCommand>(sp => sp.GetRequiredService<Commands.HelpCommand>());
        services.AddScoped<Commands.LinkCommand>();
        services.AddScoped<IBotCommand, Commands.LinkCommand>(sp => sp.GetRequiredService<Commands.LinkCommand>());
        services.AddScoped<Commands.SpamCommand>();
        services.AddScoped<IBotCommand, Commands.SpamCommand>(sp => sp.GetRequiredService<Commands.SpamCommand>());
        services.AddScoped<Commands.BanCommand>();
        services.AddScoped<IBotCommand, Commands.BanCommand>(sp => sp.GetRequiredService<Commands.BanCommand>());
        services.AddScoped<Commands.TrustCommand>();
        services.AddScoped<IBotCommand, Commands.TrustCommand>(sp => sp.GetRequiredService<Commands.TrustCommand>());
        services.AddScoped<Commands.UnbanCommand>();
        services.AddScoped<IBotCommand, Commands.UnbanCommand>(sp => sp.GetRequiredService<Commands.UnbanCommand>());
        services.AddScoped<Commands.WarnCommand>();
        services.AddScoped<IBotCommand, Commands.WarnCommand>(sp => sp.GetRequiredService<Commands.WarnCommand>());
        services.AddScoped<Commands.ReportCommand>();
        services.AddScoped<IBotCommand, Commands.ReportCommand>(sp => sp.GetRequiredService<Commands.ReportCommand>());
        services.AddScoped<Commands.DeleteCommand>();
        services.AddScoped<IBotCommand, Commands.DeleteCommand>(sp => sp.GetRequiredService<Commands.DeleteCommand>());
        services.AddSingleton<CommandRouter>();

        // Background services (refactored into smaller services)
        services.AddSingleton<SpamActionService>();
        services.AddSingleton<ChatManagementService>();
        services.AddSingleton<MessageProcessingService>();
        services.AddScoped<UserAutoTrustService>();
        services.AddSingleton<TelegramAdminBotService>();
        services.AddSingleton<IMessageHistoryService>(sp => sp.GetRequiredService<TelegramAdminBotService>());
        services.AddScoped<TelegramGroupsAdmin.SpamDetection.Services.IMessageHistoryService, MessageHistoryAdapter>();
        services.AddHostedService(sp => sp.GetRequiredService<TelegramAdminBotService>());
        services.AddHostedService<CleanupBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds all repositories
    /// </summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Spam Detection repositories
        services.AddScoped<TelegramGroupsAdmin.SpamDetection.Repositories.IStopWordsRepository, TelegramGroupsAdmin.SpamDetection.Repositories.StopWordsRepository>();
        services.AddScoped<TelegramGroupsAdmin.SpamDetection.Repositories.ITrainingSamplesRepository, TelegramGroupsAdmin.SpamDetection.Repositories.TrainingSamplesRepository>();
        services.AddScoped<TelegramGroupsAdmin.SpamDetection.Repositories.ISpamDetectionConfigRepository, TelegramGroupsAdmin.SpamDetection.Repositories.SpamDetectionConfigRepository>();

        // Detection Results and User Actions repositories
        services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<ITelegramUserMappingRepository, TelegramUserMappingRepository>();
        services.AddScoped<ITelegramLinkTokenRepository, TelegramLinkTokenRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        services.AddScoped<IReportsRepository, ReportsRepository>();

        // Spam Check Orchestrator
        services.AddScoped<ISpamCheckOrchestrator, SpamCheckOrchestrator>();
        services.AddScoped<IReportActionsService, ReportActionsService>();
        services.AddScoped<AdminMentionHandler>();

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
    /// Adds TickerQ background job system with PostgreSQL backend
    /// </summary>
    public static IServiceCollection AddTickerQBackgroundJobs(this IServiceCollection services)
    {
        services.AddTickerQ(options =>
        {
            // Max concurrent jobs
            options.SetMaxConcurrency(4);

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
