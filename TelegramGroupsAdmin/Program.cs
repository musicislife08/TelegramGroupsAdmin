using System.Threading.RateLimiting;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using MudBlazor.Services;
using Polly;
using Polly.RateLimiting;
using TelegramGroupsAdmin;
using TelegramGroupsAdmin.Components;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Infrastructure;
using TelegramGroupsAdmin.Endpoints;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.BackgroundServices;
using TelegramGroupsAdmin.Services.Telegram;
using TelegramGroupsAdmin.Services.Vision;

// Register Dapper type handler for SQLite boolean conversion (must be done early, before any queries)
SqlMapper.AddTypeHandler(new SqliteBooleanHandler());

var builder = WebApplication.CreateBuilder(args);

// Configure logging - suppress most Microsoft logs in development
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning); // Only warnings and errors from Microsoft namespaces
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information); // Keep startup/shutdown messages
builder.Logging.AddFilter("TelegramGroupsAdmin", LogLevel.Information); // Keep our app logs at Info level

// Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add HttpContextAccessor for authentication
builder.Services.AddHttpContextAccessor();

// Add HttpClient for Blazor components (for calling our own API)
builder.Services.AddScoped(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var httpContext = httpContextAccessor.HttpContext;

    if (httpContext == null) return new HttpClient { BaseAddress = new Uri("http://localhost:5161") };
    var host = httpContext.Request.Host;
    // Replace 0.0.0.0 with localhost for loopback connections
    var hostString = host.Host == "0.0.0.0" ? $"localhost:{host.Port}" : host.ToString();
    var baseAddress = $"{httpContext.Request.Scheme}://{hostString}";
    return new HttpClient { BaseAddress = new Uri(baseAddress) };

});

// Register data services (Identity repos, TOTP protection, Message history, Data Protection API)
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/keys";
builder.Services.AddTgSpamWebDataServices(dataProtectionKeysPath);

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TgSpam.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.None
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax; // Lax for cross-origin requests during development
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
    });

builder.Services.AddAuthorizationBuilder();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// Web-specific services
builder.Services.AddScoped<TelegramGroupsAdmin.Services.Auth.IPasswordHasher, TelegramGroupsAdmin.Services.Auth.PasswordHasher>();
builder.Services.AddScoped<TelegramGroupsAdmin.Services.Auth.ITotpService, TelegramGroupsAdmin.Services.Auth.TotpService>();
builder.Services.AddSingleton<TelegramGroupsAdmin.Services.Auth.IIntermediateAuthService, TelegramGroupsAdmin.Services.Auth.IntermediateAuthService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInviteService, InviteService>();
builder.Services.AddScoped<IMessageExportService, MessageExportService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// Email service (SendGrid)
builder.Services.Configure<TelegramGroupsAdmin.Services.Email.SendGridOptions>(
    builder.Configuration.GetSection("SendGrid"));
builder.Services.AddScoped<TelegramGroupsAdmin.Services.Email.IEmailService, TelegramGroupsAdmin.Services.Email.SendGridEmailService>();

// HybridCache for blocklists
builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 10 * 1024 * 1024; // 10 MB
});

// HTTP clients
builder.Services.AddHttpClient<SeoPreviewScraper>(client =>
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
            PermitLimit = 4,                    // 4 requests
            Window = TimeSpan.FromMinutes(1),   // per minute
            SegmentsPerWindow = 1,              // 1 segment is fine here
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

    OnRejected = static _ =>
    {
        // Polly's telemetry integration automatically emits metrics for rate limit rejections
        // For explicit logging, implement within VirusTotalService where ILogger is available
        return ValueTask.CompletedTask;
    }
};

builder.Services.AddHttpClient<IThreatIntelService, VirusTotalService>(client =>
    {
        client.BaseAddress = new Uri("https://www.virustotal.com/api/v3/");

        var apiKey = builder.Configuration["VirusTotal:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("x-apikey", apiKey);
        }
    })
    .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
    {
        resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
    });

// HttpClient factory
builder.Services.AddHttpClient();

// Bind configuration options from environment variables
// use the pattern: SectionName__PropertyName
// Example: OPENAI__APIKEY maps to OpenAI:ApiKey
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<SpamDetectionOptions>(builder.Configuration.GetSection("SpamDetection"));
builder.Services.Configure<MessageHistoryOptions>(builder.Configuration.GetSection("MessageHistory"));

// Database paths for FluentMigrator (pulled from configuration)
var identityDbPath = builder.Configuration["Identity:DatabasePath"] ?? "/data/identity.db";
var messageHistoryDbPath = builder.Configuration["MessageHistory:DatabasePath"] ?? "/data/message_history.db";

// Telegram services
builder.Services.AddSingleton<TelegramBotClientFactory>();
builder.Services.AddScoped<ITelegramImageService, TelegramImageService>();

// Background services (register as singleton first, then add as hosted service)
// Also expose IMessageHistoryService interface for UI components
builder.Services.AddSingleton<HistoryBotService>();
builder.Services.AddSingleton<IMessageHistoryService>(sp => sp.GetRequiredService<HistoryBotService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<HistoryBotService>());
builder.Services.AddHostedService<CleanupBackgroundService>();

// OpenAI Vision
builder.Services.AddHttpClient<IVisionSpamDetectionService, OpenAIVisionSpamDetectionService>();

// Register spam check service
builder.Services.AddScoped<SpamCheckService>();

var app = builder.Build();

// Run database migrations
await RunDatabaseMigrationsAsync(app, identityDbPath, messageHistoryDbPath);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Configure static file serving for images
ConfigureImageStaticFiles(app);

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map API endpoints
app.MapAuthEndpoints();
app.MapSpamCheckEndpoints();
app.MapEmailVerificationEndpoints();

app.Run();

return;

// ============================================================================
// Helper Methods
// ============================================================================

static async Task RunDatabaseMigrationsAsync(WebApplication app, string identityDbPath, string messageHistoryDbPath)
{
    var migrationAssembly = typeof(TelegramGroupsAdmin.Data.Migrations.IdentitySchema).Assembly;

    // Migrate Identity database
    await RunMigrationAsync(app, identityDbPath, migrationAssembly, "Identity");

    // Migrate Message History database (with legacy database support)
    await HandleLegacyMessageHistoryDatabaseAsync(app, messageHistoryDbPath);
    await RunMigrationAsync(app, messageHistoryDbPath, migrationAssembly, "Message History");
}

static Task RunMigrationAsync(WebApplication app, string dbPath, System.Reflection.Assembly migrationAssembly, string dbName)
{
    app.Logger.LogInformation("Migrating {DbName} database: {Path}", dbName, dbPath);

#pragma warning disable ASP0000 // BuildServiceProvider is acceptable for migration setup
    var serviceProvider = new ServiceCollection()
        .AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddSQLite()
            .WithGlobalConnectionString($"Data Source={dbPath}")
            .ScanIn(migrationAssembly).For.Migrations())
        .AddLogging(lb => lb.AddFluentMigratorConsole())
        .BuildServiceProvider(validateScopes: false);
#pragma warning restore ASP0000

    using var scope = serviceProvider.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
    runner.MigrateUp();

    app.Logger.LogInformation("{DbName} database migration complete", dbName);
    return Task.CompletedTask;
}

static async Task HandleLegacyMessageHistoryDatabaseAsync(WebApplication app, string dbPath)
{
    // Handle legacy database: if VersionInfo is empty but messages table exists, mark InitialSchema as applied
    await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();

    // Check if VersionInfo table exists and is empty
    var versionInfoExists = await conn.QueryFirstOrDefaultAsync<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='VersionInfo'") > 0;

    if (!versionInfoExists) return;

    var versionCount = await conn.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM VersionInfo");
    var messagesTableExists = await conn.QueryFirstOrDefaultAsync<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='messages'") > 0;

    // Legacy database detected: VersionInfo empty but messages table exists
    if (versionCount == 0 && messagesTableExists)
    {
        app.Logger.LogInformation("Legacy database detected. Marking InitialSchema (202510061) as applied.");
        await conn.ExecuteAsync(
            "INSERT INTO VersionInfo (Version, AppliedOn, Description) VALUES (202510061, @Now, 'InitialSchema')",
            new { Now = DateTime.UtcNow });
    }
}

static void ConfigureImageStaticFiles(WebApplication app)
{
    var imageStoragePath = app.Configuration["MessageHistory:ImageStoragePath"] ?? "/data/images";

    // Convert to absolute path if relative
    var absoluteImagePath = Path.IsPathRooted(imageStoragePath)
        ? imageStoragePath
        : Path.GetFullPath(imageStoragePath);

    // Ensure image storage path exists (CreateDirectory is idempotent)
    Directory.CreateDirectory(absoluteImagePath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(absoluteImagePath),
        RequestPath = "/images"
    });
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
