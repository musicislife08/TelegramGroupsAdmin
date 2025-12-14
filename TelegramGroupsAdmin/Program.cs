using System.Reflection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TelegramGroupsAdmin;
using TelegramGroupsAdmin.BackgroundJobs.Extensions;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Services;
using HumanCron;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for runtime log level switching
// Note: Serilog configuration happens AFTER services are registered to access database
builder.Logging.ClearProviders(); // Remove default logging providers

// Blazor and UI services
builder.Services.AddBlazorServices();

// Authentication and Authorization
builder.Services.AddCookieAuthentication(builder.Environment);

// Configure application options from environment variables (must be early for DataPath)
builder.Services.AddApplicationConfiguration(builder.Configuration);

// Get base data path from configuration
var dataPath = builder.Configuration["App:DataPath"] ?? "/data";

// Data Protection and Identity repositories (uses {dataPath}/keys)
var dataProtectionKeysPath = Path.Combine(dataPath, "keys");
builder.Services.AddTgSpamWebDataServices(dataProtectionKeysPath);

// Create ML model directory (follows same pattern as media/keys)
var mlModelsPath = Path.Combine(dataPath, "ml-models");
Directory.CreateDirectory(mlModelsPath);

// Application services (auth, users, messages, etc.)
builder.Services.AddApplicationServices();

// HTTP clients with rate limiting
builder.Services.AddHttpClients(builder.Configuration);

// Data layer services (Dapper, EF Core, FluentMigrator)
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddDataServices(connectionString);

// Core services (audit, etc.)
builder.Services.AddCoreServices();

// Register Serilog configuration service (will be configured after app.Build())
SerilogDynamicConfiguration? serilogConfig = null;
builder.Services.AddSingleton<SerilogDynamicConfiguration>(sp =>
{
    serilogConfig = new SerilogDynamicConfiguration(sp.GetRequiredService<IServiceScopeFactory>());
    return serilogConfig;
});

// Configure Serilog with dynamic log level switching (starts with defaults, loads from DB after app starts)
builder.Host.UseSerilog((context, services, configuration) =>
{
    var config = services.GetRequiredService<SerilogDynamicConfiguration>();

    // Get observability configuration from environment variables (optional)
    var seqUrl = context.Configuration["SEQ_URL"];
    var seqApiKey = context.Configuration["SEQ_API_KEY"];
    var otlpEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    var serviceName = context.Configuration["OTEL_SERVICE_NAME"] ?? "TelegramGroupsAdmin";

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

    // Add Seq sink if URL is configured (persistent structured logs)
    if (!string.IsNullOrEmpty(seqUrl))
    {
        loggerConfig.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
    }

    // Add OpenTelemetry sink if OTLP endpoint is configured (sends to Aspire Dashboard)
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

// Repositories
builder.Services.AddRepositories();

// Content Detection library
builder.Services.AddContentDetection();

// Natural Cron Parser (for background job scheduling)
builder.Services.AddHumanCron();

// Background Jobs (Quartz.NET)
builder.Services.AddBackgroundJobs(builder.Configuration);

// Health checks for Kubernetes/Docker
// Liveness: Just checks if the app is responsive (no database checks - app restart won't fix DB issues)
// Readiness: Checks database connectivity (if DB is down, stop receiving traffic but don't restart)
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: ["ready", "db"]);

// OpenTelemetry Observability (optional - enabled via OTEL_EXPORTER_OTLP_ENDPOINT)
// Supports both Seq (logs + traces) and Aspire Dashboard (logs + traces + metrics)
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "TelegramGroupsAdmin";
var serviceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
var environment = builder.Environment.EnvironmentName;

if (!string.IsNullOrEmpty(otlpEndpoint))
{
    // Note: Logging is handled by Serilog's OpenTelemetry sink (configured above in UseSerilog)
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = environment
            }))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()  // Blazor Server requests, SignalR circuits
            .AddHttpClientInstrumentation()  // OpenAI, VirusTotal, Telegram Bot API
            .AddSource("Npgsql")             // Npgsql automatic tracing (via Npgsql.OpenTelemetry package)
            .AddSource("TelegramGroupsAdmin.*")  // Custom ActivitySources (from TelemetryConstants)
            .AddOtlpExporter())              // Send traces to Aspire Dashboard
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()  // Request rate, duration, active requests
            .AddHttpClientInstrumentation()  // HTTP client success/failure rates
            .AddRuntimeInstrumentation()     // GC, CPU, memory, thread pool
            .AddMeter("Npgsql")              // Npgsql database metrics (connections, commands, bytes)
            .AddMeter("TelegramGroupsAdmin.*")  // Custom meters (from TelemetryConstants)
            .AddOtlpExporter()               // Send metrics to Aspire Dashboard
            .AddPrometheusExporter());       // ALSO expose /metrics endpoint for Prometheus scraping
}

var app = builder.Build();

// Run database migrations
await app.RunDatabaseMigrationsAsync(connectionString);

// Initialize Serilog configuration from database (now that migrations have run)
if (serilogConfig != null)
{
    await serilogConfig.InitializeAsync();
    app.Logger.LogInformation("Loaded log configuration from database");
}

// Note: Default background job configurations are ensured by QuartzSchedulingSyncService on startup

// Check for --migrate-only flag to run migrations and exit
if (args.Contains("--migrate-only") || args.Contains("--migrate"))
{
    app.Logger.LogInformation("Migration complete. Exiting (--migrate-only flag).");
    Environment.Exit(0);
}

// Check for --backup flag to create encrypted backup (requires --passphrase)
if (args.Contains("--backup"))
{
    var backupPath = args.SkipWhile(a => a != "--backup").Skip(1).FirstOrDefault() ?? $"backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.tar.gz";
    var passphraseIndex = Array.IndexOf(args, "--passphrase");

    if (passphraseIndex == -1 || passphraseIndex + 1 >= args.Length)
    {
        app.Logger.LogError("--backup requires --passphrase argument. Usage: --backup [path] --passphrase <passphrase>");
        app.Logger.LogError("Example: dotnet run --backup /data/backups/backup.tar.gz --passphrase \"your-32-char-passphrase\"");
        Environment.Exit(1);
    }

    var passphrase = args[passphraseIndex + 1];
    if (string.IsNullOrWhiteSpace(passphrase))
    {
        app.Logger.LogError("Passphrase cannot be empty");
        Environment.Exit(1);
    }

    using var scope = app.Services.CreateScope();
    var backupService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.IBackupService>();

    app.Logger.LogInformation("Creating encrypted backup...");
    var backupBytes = await backupService.ExportAsync(passphrase); // Use passphrase override for CLI
    await File.WriteAllBytesAsync(backupPath, backupBytes);

    app.Logger.LogInformation("✅ Encrypted backup created: {Path} ({Size:F2} MB)",
        backupPath, backupBytes.Length / 1024.0 / 1024.0);
    app.Logger.LogInformation("🔐 Store your passphrase securely - you'll need it to restore this backup!");
    app.Logger.LogInformation("Exiting (--backup flag).");
    Environment.Exit(0);
}

// Check for --restore flag to restore encrypted backup (WIPES ALL DATA, requires --passphrase)
if (args.Contains("--restore"))
{
    var restorePath = args.SkipWhile(a => a != "--restore").Skip(1).FirstOrDefault();
    if (restorePath == null)
    {
        app.Logger.LogError("--restore requires a file path argument");
        Environment.Exit(1);
    }
    if (!File.Exists(restorePath))
    {
        app.Logger.LogError("Backup file not found: {Path}", restorePath);
        Environment.Exit(1);
    }

    var passphraseIndex = Array.IndexOf(args, "--passphrase");
    if (passphraseIndex == -1 || passphraseIndex + 1 >= args.Length)
    {
        app.Logger.LogError("--restore requires --passphrase argument. Usage: --restore <path> --passphrase <passphrase>");
        app.Logger.LogError("Example: dotnet run --restore /data/backups/backup.tar.gz --passphrase \"your-32-char-passphrase\"");
        Environment.Exit(1);
    }

    var passphrase = args[passphraseIndex + 1];
    if (string.IsNullOrWhiteSpace(passphrase))
    {
        app.Logger.LogError("Passphrase cannot be empty");
        Environment.Exit(1);
    }

    app.Logger.LogWarning("⚠️  WARNING: This will WIPE ALL DATA and restore from backup!");
    app.Logger.LogWarning("⚠️  Press Ctrl+C within 5 seconds to cancel...");
    await Task.Delay(5000);

    using var scope = app.Services.CreateScope();
    var backupService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.IBackupService>();

    app.Logger.LogInformation("Reading encrypted backup...");
    var backupBytes = await File.ReadAllBytesAsync(restorePath);

    app.Logger.LogInformation("Decrypting and restoring backup...");
    await backupService.RestoreAsync(backupBytes, passphrase); // Use explicit passphrase for CLI restore

    app.Logger.LogInformation("✅ System restore complete. Exiting (--restore flag).");
    Environment.Exit(0);
}

// Configure HTTP request pipeline
app.ConfigurePipeline();

// Map API endpoints
app.MapApiEndpoints();

// Map Prometheus metrics endpoint (only if OpenTelemetry is enabled)
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    app.MapPrometheusScrapingEndpoint();
    app.Logger.LogInformation("Prometheus metrics endpoint mapped to /metrics");
}

app.Run();
