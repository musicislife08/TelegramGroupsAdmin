using System.Threading.RateLimiting;
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
using TelegramGroupsAdmin.Endpoints;
using TelegramGroupsAdmin.Services.BackgroundServices;
using TelegramGroupsAdmin.Services.Telegram;
using TelegramGroupsAdmin.Services.Vision;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add HttpContextAccessor for authentication
builder.Services.AddHttpContextAccessor();

// Register data services (Identity repos, TOTP protection, Message history, Data Protection API)
var dataProtectionKeysPath = Path.Combine("/data", "keys");
builder.Services.AddTgSpamWebDataServices(dataProtectionKeysPath);

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TgSpam.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// Web-specific services
builder.Services.AddScoped<TelegramGroupsAdmin.Services.IAuthService, TelegramGroupsAdmin.Services.AuthService>();
builder.Services.AddScoped<TelegramGroupsAdmin.Services.IInviteService, TelegramGroupsAdmin.Services.InviteService>();

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
        Console.WriteLine("Rate limit hit for VirusTotal.");
        return ValueTask.CompletedTask;
    }
};

builder.Services.AddHttpClient<IThreatIntelService, VirusTotalService>(options =>
    {
        options.BaseAddress = new Uri("https://www.virustotal.com/api/v3/");
        options.DefaultRequestHeaders.Add("x-apikey", Environment.GetEnvironmentVariable("VIRUSTOTAL_API_KEY")
                                                      ?? throw new InvalidOperationException("VIRUSTOTAL_API_KEY not set"));
    })
    .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
    {
        resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
    });

// HttpClient factory
builder.Services.AddHttpClient();

// Configuration from environment variables
builder.Services.Configure<OpenAIOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    options.Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    options.MaxTokens = int.TryParse(Environment.GetEnvironmentVariable("OPENAI_MAX_TOKENS"), out var maxTokens) ? maxTokens : 500;
});

builder.Services.Configure<TelegramOptions>(options =>
{
    options.HistoryBotToken = Environment.GetEnvironmentVariable("TELEGRAM_HISTORY_BOT_TOKEN") ?? "";
    options.ChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";
});

builder.Services.Configure<MessageHistoryOptions>(options =>
{
    options.DatabasePath = Environment.GetEnvironmentVariable("MESSAGE_HISTORY_DATABASE_PATH") ?? "/data/message_history.db";
    options.RetentionHours = int.TryParse(Environment.GetEnvironmentVariable("MESSAGE_HISTORY_RETENTION_HOURS"), out var hours) ? hours : 24;
    options.CleanupIntervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("MESSAGE_HISTORY_CLEANUP_INTERVAL_MINUTES"), out var minutes) ? minutes : 5;
});

builder.Services.Configure<SpamDetectionOptions>(options =>
{
    options.TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_TIMEOUT_SECONDS"), out var timeout) ? timeout : 30;
    options.ImageLookupRetryDelayMs = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_RETRY_DELAY_MS"), out var delay) ? delay : 100;
    options.MinConfidenceThreshold = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_MIN_CONFIDENCE"), out var confidence) ? confidence : 85;
});

// Configure database paths
builder.Configuration["Identity:DatabasePath"] = Environment.GetEnvironmentVariable("IDENTITY_DATABASE_PATH") ?? "/data/identity.db";
builder.Configuration["MessageHistory:DatabasePath"] = Environment.GetEnvironmentVariable("MESSAGE_HISTORY_DATABASE_PATH") ?? "/data/message_history.db";

// FluentMigrator for both databases
var identityDbPath = builder.Configuration["Identity:DatabasePath"] ?? "/data/identity.db";
var messageHistoryDbPath = builder.Configuration["MessageHistory:DatabasePath"] ?? "/data/message_history.db";

// Identity database migrations
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(rb => rb
        .AddSQLite()
        .WithGlobalConnectionString($"Data Source={identityDbPath}")
        .ScanIn(typeof(TelegramGroupsAdmin.Data.Migrations.IdentitySchema).Assembly).For.Migrations())
    .AddLogging(lb => lb.AddFluentMigratorConsole());

// Telegram services
builder.Services.AddSingleton<TelegramBotClientFactory>();
builder.Services.AddScoped<ITelegramImageService, TelegramImageService>();

// Background services
builder.Services.AddHostedService<HistoryBotService>();
builder.Services.AddHostedService<CleanupBackgroundService>();

// OpenAI Vision
builder.Services.AddHttpClient<IVisionSpamDetectionService, OpenAIVisionSpamDetectionService>();

// Register spam check service
builder.Services.AddScoped<SpamCheckService>();

var app = builder.Build();

// Run Identity database migrations
using (var scope = app.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

    if (runner.HasMigrationsToApplyUp())
    {
        app.Logger.LogInformation("Applying pending database migrations...");
        runner.MigrateUp();
    }
    else
    {
        app.Logger.LogInformation("Database schema is up to date");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map API endpoints
app.MapSpamCheckEndpoints();

app.Run();


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
