using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Services.Email;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for E2E tests using .NET 10's native UseKestrel() support.
///
/// Philosophy: Override ONLY what's necessary for testing. Let the real app run as-is.
/// - Database: Point to isolated test database (required for test isolation)
/// - Email: Capture instead of send (so we don't send real emails)
/// - Everything else: Runs normally (bot inactive without token, jobs run but harmless)
///
/// See: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private readonly TestEmailService _testEmailService;
    private string? _tempDataPath;
    private bool _databaseCreated;

    public TestWebApplicationFactory(string? databaseName = null)
    {
        _databaseName = databaseName ?? E2EFixture.GetUniqueDatabaseName();
        _testEmailService = new TestEmailService();

        // Configure to use Kestrel with dynamic port (port 0)
        // This MUST be called before StartServer() or accessing Services
        UseKestrel(0);
    }

    /// <summary>
    /// Gets the test email service for verifying sent emails in tests.
    /// </summary>
    public TestEmailService EmailService => _testEmailService;

    /// <summary>
    /// Gets the connection string for this test's isolated database.
    /// </summary>
    public string ConnectionString => BuildConnectionString(_databaseName);

    /// <summary>
    /// Gets the base URL where the Kestrel server is listening.
    /// Available after calling StartServer() or accessing Services.
    /// </summary>
    public string ServerAddress => ClientOptions.BaseAddress.ToString().TrimEnd('/');

    /// <summary>
    /// Enables email verification by seeding SendGrid config in the database.
    /// Must be called after StartServer() and before registration tests.
    /// The actual emails are captured by TestEmailService, not sent via SendGrid.
    /// </summary>
    public async Task EnableEmailVerificationAsync()
    {
        using var scope = Services.CreateScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        var sendGridConfig = new SendGridConfig
        {
            Enabled = true,
            FromAddress = "test@e2e.local",
            FromName = "E2E Test"
        };

        await configRepo.SaveSendGridConfigAsync(sendGridConfig);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Create the test database before app starts
        EnsureDatabaseCreated();

        // Use Development environment so UseStaticFiles() serves CSS/JS correctly
        // (MapStaticAssets requires publish-time manifest which doesn't exist in tests)
        builder.UseEnvironment("Development");

        // Point data path to temp directory (for data protection keys, media, etc.)
        builder.UseSetting("App:DataPath", _tempDataPath);

        // Point to test database
        builder.UseSetting("ConnectionStrings:PostgreSQL", ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Remove ALL existing DbContext registrations (including pool and factory)
            // The app registers both AddDbContext and AddPooledDbContextFactory
            var dbContextDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("AppDbContext") == true ||
                            d.ServiceType.FullName?.Contains("DbContextOptions") == true ||
                            d.ServiceType.FullName?.Contains("DbContextPool") == true ||
                            d.ServiceType.FullName?.Contains("DbContextFactory") == true)
                .ToList();
            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Re-register exactly like the original app does (matching lifetimes)
            // AddDbContext with singleton options (required for factory compatibility)
            services.AddDbContext<AppDbContext>(
                options => options.UseNpgsql(ConnectionString),
                contextLifetime: ServiceLifetime.Scoped,
                optionsLifetime: ServiceLifetime.Singleton);

            // AddPooledDbContextFactory for IDbContextFactory<AppDbContext>
            services.AddPooledDbContextFactory<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            // Replace email service with test fake
            services.RemoveAll<IEmailService>();
            services.AddSingleton(_testEmailService);
            services.AddScoped<IEmailService>(sp => sp.GetRequiredService<TestEmailService>());
        });
    }

    private void EnsureDatabaseCreated()
    {
        if (_databaseCreated) return;

        // Create a temp directory for data protection keys and other files
        _tempDataPath = Path.Combine(Path.GetTempPath(), "e2e_tests", _databaseName);
        Directory.CreateDirectory(_tempDataPath);

        // Create the test database on the shared PostgreSQL container
        using var connection = new NpgsqlConnection(E2EFixture.BaseConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        cmd.ExecuteNonQuery();

        _databaseCreated = true;
    }

    private static string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(E2EFixture.BaseConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the test database when done
            try
            {
                using var connection = new NpgsqlConnection(E2EFixture.BaseConnectionString);
                connection.Open();

                // Terminate existing connections
                using var terminateCmd = connection.CreateCommand();
                terminateCmd.CommandText = $@"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid()";
                terminateCmd.ExecuteNonQuery();

                // Drop the database
                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                dropCmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Clean up temp directory
            try
            {
                if (_tempDataPath != null && Directory.Exists(_tempDataPath))
                {
                    Directory.Delete(_tempDataPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        base.Dispose(disposing);
    }
}
