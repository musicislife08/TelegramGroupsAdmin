using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TelegramGroupsAdmin.BackgroundJobs.Extensions;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Ui.Server;
using TelegramGroupsAdmin.Ui.Server.Constants;
using TelegramGroupsAdmin.Ui.Server.Endpoints;
using TelegramGroupsAdmin.Ui.Server.Endpoints.Actions;
using TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;
using TelegramGroupsAdmin.Ui.Server.Services;
using HumanCron;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for runtime log level switching
builder.Logging.ClearProviders();

// Authentication and Authorization (cookie-based for WASM)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AuthenticationConstants.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = AuthenticationConstants.CookieExpiration;
        options.SlidingExpiration = true;

        // API returns 401 instead of redirecting to login
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// HttpContext accessor (for API auth helper services)
builder.Services.AddHttpContextAccessor();

// Configure application options from environment variables (must be early for DataPath)
builder.Services.AddApplicationConfiguration(builder.Configuration);

// Get base data path from configuration
var dataPath = builder.Configuration["App:DataPath"] ?? "/data";

// Data Protection and Identity repositories (uses {dataPath}/keys)
var dataProtectionKeysPath = Path.Combine(dataPath, "keys");
builder.Services.AddWebDataServices(dataProtectionKeysPath);

// Create ML model directory (follows same pattern as media/keys)
var mlModelsPath = Path.Combine(dataPath, "ml-models");
Directory.CreateDirectory(mlModelsPath);

// HTTP clients with rate limiting
builder.Services.AddHttpClients(builder.Configuration);

// Data layer services (Dapper, EF Core, FluentMigrator)
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddDataServices(connectionString);

// Core services (audit, etc.)
builder.Services.AddCoreServices();

// Application services (auth, notifications, email, etc.)
builder.Services.AddApplicationServices();

// Register Serilog configuration service (will be configured after app.Build())
SerilogDynamicConfiguration? serilogConfig = null;
builder.Services.AddSingleton<SerilogDynamicConfiguration>(sp =>
{
    serilogConfig = new SerilogDynamicConfiguration(sp.GetRequiredService<IServiceScopeFactory>());
    return serilogConfig;
});

// Configure Serilog with dynamic log level switching
builder.Host.UseSerilog((context, services, configuration) =>
{
    var config = services.GetRequiredService<SerilogDynamicConfiguration>();

    var seqUrl = context.Configuration["SEQ_URL"];
    var seqApiKey = context.Configuration["SEQ_API_KEY"];
    var otlpEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    var serviceName = context.Configuration["OTEL_SERVICE_NAME"] ?? "TelegramGroupsAdmin.Ui.Server";

    var loggerConfig = configuration
        .MinimumLevel.ControlledBy(config.DefaultSwitch)
        .MinimumLevel.Override("Microsoft", config.GetSwitch("Microsoft"))
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", config.GetSwitch("Microsoft.Hosting.Lifetime"))
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", config.GetSwitch("Microsoft.EntityFrameworkCore"))
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", config.GetSwitch("Microsoft.EntityFrameworkCore.Database.Command"))
        .MinimumLevel.Override("Npgsql", config.GetSwitch("Npgsql"))
        .MinimumLevel.Override("System", config.GetSwitch("System"))
        .MinimumLevel.Override("TelegramGroupsAdmin", config.GetSwitch("TelegramGroupsAdmin"))
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");

    if (!string.IsNullOrEmpty(seqUrl))
    {
        loggerConfig.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
    }

    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        loggerConfig.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = otlpEndpoint;
            options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = serviceName
            };
        });
    }
});

// Telegram services and bot commands
builder.Services.AddTelegramServices();

// Content Detection library
builder.Services.AddContentDetection();

// Natural Cron Parser (for background job scheduling)
builder.Services.AddHumanCron();

// Background Jobs (Quartz.NET)
builder.Services.AddBackgroundJobs(builder.Configuration);

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: ["ready", "db"]);

// OpenTelemetry Observability (optional)
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "TelegramGroupsAdmin.Ui.Server";
var serviceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
var environment = builder.Environment.EnvironmentName;

if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = environment
            }))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql")
            .AddSource("TelegramGroupsAdmin.*")
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Npgsql")
            .AddMeter("TelegramGroupsAdmin.*")
            .AddOtlpExporter()
            .AddPrometheusExporter());
}

var app = builder.Build();

// Run database migrations
app.Logger.LogInformation("Running PostgreSQL database migrations (EF Core)");
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync();
}
app.Logger.LogInformation("PostgreSQL database migration complete");

// Initialize Serilog configuration from database
if (serilogConfig != null)
{
    await serilogConfig.InitializeAsync();
    app.Logger.LogInformation("Loaded log configuration from database");
}

// Train ML.NET spam classifier model on startup
var mlClassifier = app.Services.GetRequiredService<TelegramGroupsAdmin.ContentDetection.ML.IMLTextClassifierService>();
app.Logger.LogInformation("Training ML spam classifier model with latest data...");
await mlClassifier.TrainModelAsync();
var metadata = mlClassifier.GetMetadata();
app.Logger.LogInformation(
    "ML classifier trained: {SpamSamples} spam + {HamSamples} ham samples (ratio: {SpamRatio:P1}, balanced: {Balanced})",
    metadata?.SpamSampleCount,
    metadata?.HamSampleCount,
    metadata?.SpamRatio,
    metadata?.IsBalanced);

// Check for --migrate-only flag to run migrations and exit
if (args.Contains("--migrate-only") || args.Contains("--migrate"))
{
    app.Logger.LogInformation("Migration complete. Exiting (--migrate-only flag).");
    Environment.Exit(0);
}

// Configure forwarded headers for reverse proxy support
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve WASM client static files
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// Configure static file serving for media
ConfigureMediaStaticFiles(app);

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoints
app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = healthCheck => healthCheck.Tags.Contains("ready")
}).AllowAnonymous();

// API endpoints
app.MapAuthEndpoints();
app.MapEmailVerificationEndpoints();
app.MapSseEndpoints();
app.MapMessagesPageEndpoints();
app.MapDashboardEndpoints();
app.MapRegisterPageEndpoints();
app.MapMessagesEndpoints();
app.MapUsersEndpoints();
app.MapBackupEndpoints();

// Prometheus metrics endpoint (if OpenTelemetry enabled)
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    app.MapPrometheusScrapingEndpoint();
    app.Logger.LogInformation("Prometheus metrics endpoint mapped to /metrics");
}

// Fallback to WASM client for client-side routing
app.MapFallbackToFile("index.html");

app.Run();

// Helper method for media static files
static void ConfigureMediaStaticFiles(WebApplication app)
{
    var messageHistoryOptions = app.Services.GetRequiredService<IOptions<MessageHistoryOptions>>().Value;

    if (!messageHistoryOptions.Enabled)
    {
        return;
    }

    var basePath = Path.GetFullPath(messageHistoryOptions.ImageStoragePath);
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
