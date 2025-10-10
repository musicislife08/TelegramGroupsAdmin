using FluentMigrator.Runner;
using TelegramGroupsAdmin;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.SpamDetection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure logging - suppress most Microsoft logs in development
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
});
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Logging.AddFilter("TelegramGroupsAdmin.SpamDetection", LogLevel.Debug);
builder.Logging.AddFilter("TelegramGroupsAdmin", LogLevel.Information);
builder.Logging.AddFilter("Npgsql", LogLevel.Warning);

// Blazor and UI services
builder.Services.AddBlazorServices();

// Authentication and Authorization
builder.Services.AddCookieAuthentication(builder.Environment);

// Data Protection and Identity repositories
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/keys";
builder.Services.AddTgSpamWebDataServices(dataProtectionKeysPath);

// Application services (auth, users, messages, etc.)
builder.Services.AddApplicationServices();

// Configure application options from environment variables
builder.Services.AddApplicationConfiguration(builder.Configuration);

// HTTP clients with rate limiting
builder.Services.AddHttpClients(builder.Configuration);

// PostgreSQL connection
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddNpgsqlDataSource(connectionString);

// Telegram services and bot commands
builder.Services.AddTelegramServices();

// Repositories
builder.Services.AddRepositories();

// Spam Detection library
builder.Services.AddSpamDetection();

// FluentMigrator for database migrations
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(rb => rb
        .AddPostgres()
        .WithGlobalConnectionString(connectionString)
        .ScanIn(typeof(TelegramGroupsAdmin.Data.Migrations.IdentitySchema).Assembly).For.Migrations())
    .AddLogging(lb => lb.AddFluentMigratorConsole());

var app = builder.Build();

// Run database migrations
await app.RunDatabaseMigrationsAsync(connectionString);

// Check for --migrate-only flag to run migrations and exit
if (args.Contains("--migrate-only") || args.Contains("--migrate"))
{
    app.Logger.LogInformation("Migration complete. Exiting (--migrate-only flag).");
    Environment.Exit(0);
}

// Check for --export-users flag to export decrypted user data and exit
if (args.Contains("--export-users"))
{
    var exportPath = args.SkipWhile(a => a != "--export-users").Skip(1).FirstOrDefault() ?? "users_export.json";
    using var scope = app.Services.CreateScope();
    var exportService = scope.ServiceProvider.GetRequiredService<IUserDataExportService>();
    await exportService.ExportAsync(exportPath);
    app.Logger.LogInformation("User export complete. Exiting (--export-users flag).");
    Environment.Exit(0);
}

// Check for --import-users flag to import user data (will be encrypted) and exit
if (args.Contains("--import-users"))
{
    var importPath = args.SkipWhile(a => a != "--import-users").Skip(1).FirstOrDefault();
    if (importPath == null)
    {
        app.Logger.LogError("--import-users requires a file path argument");
        Environment.Exit(1);
    }
    using var scope = app.Services.CreateScope();
    var exportService = scope.ServiceProvider.GetRequiredService<IUserDataExportService>();
    await exportService.ImportAsync(importPath);
    app.Logger.LogInformation("User import complete. Exiting (--import-users flag).");
    Environment.Exit(0);
}

// Configure HTTP request pipeline
app.ConfigurePipeline();

// Map API endpoints
app.MapApiEndpoints();

app.Run();
