using TelegramGroupsAdmin;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.Telegram.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure logging - suppress most Microsoft logs in development
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
});
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Logging.AddFilter("TelegramGroupsAdmin.ContentDetection", LogLevel.Debug);
builder.Logging.AddFilter("TelegramGroupsAdmin", LogLevel.Information);
builder.Logging.AddFilter("Npgsql", LogLevel.Warning);

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

// Application services (auth, users, messages, etc.)
builder.Services.AddApplicationServices();

// HTTP clients with rate limiting
builder.Services.AddHttpClients(builder.Configuration);

// Data layer services (Dapper, EF Core, FluentMigrator)
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddDataServices(connectionString);

// Background job system (TickerQ with PostgreSQL backend)
builder.Services.AddTickerQBackgroundJobs(builder.Environment);

// Telegram services and bot commands
builder.Services.AddTelegramServices();

// Repositories
builder.Services.AddRepositories();

// Content Detection library
builder.Services.AddContentDetection();

// Health checks for Kubernetes/Docker
// Liveness: Just checks if the app is responsive (no database checks - app restart won't fix DB issues)
// Readiness: Checks database connectivity (if DB is down, stop receiving traffic but don't restart)
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: ["ready", "db"]);

var app = builder.Build();

// Explicitly initialize TickerQ functions BEFORE UseTickerQ() is called
// (for .NET 10 RC2 compatibility - ModuleInitializer doesn't auto-execute)
TelegramGroupsAdmin.TickerQInstanceFactory.Initialize();

// Run database migrations
await app.RunDatabaseMigrationsAsync(connectionString);

// Ensure default background job configurations exist
using (var scope = app.Services.CreateScope())
{
    var jobConfigService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Services.IBackgroundJobConfigService>();
    await jobConfigService.EnsureDefaultConfigsAsync();
    app.Logger.LogInformation("Ensured default background job configurations exist");
}

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
    var backupService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Services.Backup.IBackupService>();

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
    var backupService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Services.Backup.IBackupService>();

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

app.Run();
