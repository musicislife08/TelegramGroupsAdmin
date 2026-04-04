using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TelegramGroupsAdmin.IntegrationTests.PgBouncer;

/// <summary>
/// Starts a PostgreSQL 18 container and a PgBouncer 1.25.1 container in transaction mode.
/// PgBouncer sits between tests and PostgreSQL, matching production deployment topology.
/// </summary>
public class PgBouncerFixture : IAsyncDisposable
{
    private INetwork? _network;
    private PostgreSqlContainer? _postgresContainer;
    private IContainer? _pgBouncerContainer;
    private string? _tempConfigPath;
    private string? _tempUserlistPath;
    private bool _disposed;

    /// <summary>
    /// Connection string pointing through PgBouncer (with No Reset On Close=true).
    /// </summary>
    public string PgBouncerConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Direct connection string to PostgreSQL (bypasses PgBouncer).
    /// Used for admin operations like CREATE DATABASE.
    /// </summary>
    public string DirectConnectionString { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        // 1. Create shared Docker network
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        // 2. Start PostgreSQL on the shared network
        _postgresContainer = new PostgreSqlBuilder("postgres:18")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithCleanUp(true)
            .Build();

        await _postgresContainer.StartAsync();
        DirectConnectionString = _postgresContainer.GetConnectionString();

        // 3. Create pgbouncer_auth SUPERUSER role (mirrors production auth_query setup)
        var adminBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "CREATE ROLE pgbouncer_auth WITH LOGIN SUPERUSER PASSWORD 'pgbouncer_auth_password'",
                connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // 4. Extract PgBouncer config from embedded resource
        var assembly = typeof(PgBouncerFixture).Assembly;
        var configResourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("pgbouncer.ini"));

        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"pgbouncer_{Guid.NewGuid():N}.ini");
        await using (var stream = assembly.GetManifestResourceStream(configResourceName)!)
        await using (var fileStream = File.Create(_tempConfigPath))
        {
            await stream.CopyToAsync(fileStream);
        }

        // 5. Extract userlist.txt for PgBouncer auth
        var userlistResourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("userlist.txt"));

        _tempUserlistPath = Path.Combine(Path.GetTempPath(), $"pgbouncer_userlist_{Guid.NewGuid():N}.txt");
        await using (var stream = assembly.GetManifestResourceStream(userlistResourceName)!)
        await using (var fileStream = File.Create(_tempUserlistPath))
        {
            await stream.CopyToAsync(fileStream);
        }

        // 6. Start PgBouncer on the same network (random host port to avoid conflicts)
        // Uses bind mount (not WithResourceMapping) so config exists at container start time
        _pgBouncerContainer = new ContainerBuilder("ghcr.io/icoretech/pgbouncer-docker:1.25.1")
            .WithNetwork(_network)
            .WithPortBinding(5432, assignRandomHostPort: true)
            .WithBindMount(_tempConfigPath, "/etc/pgbouncer/pgbouncer.ini")
            .WithBindMount(_tempUserlistPath, "/etc/pgbouncer/userlist.txt")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .WithCleanUp(true)
            .Build();

        await _pgBouncerContainer.StartAsync();

        // 7. Build connection string through PgBouncer
        var pgBouncerPort = _pgBouncerContainer.GetMappedPublicPort(5432);
        var directBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString);

        var pgBouncerBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = pgBouncerPort,
            Database = directBuilder.Database,
            Username = directBuilder.Username,
            Password = directBuilder.Password,
            NoResetOnClose = true
        };
        PgBouncerConnectionString = pgBouncerBuilder.ConnectionString;
    }

    /// <summary>
    /// Creates a unique database accessible through both direct and PgBouncer connections.
    /// </summary>
    public async Task<(string directConnStr, string pgBouncerConnStr)> CreateUniqueDatabaseAsync()
    {
        var dbName = $"test_db_{Guid.NewGuid():N}";
        var roleName = $"tga_test_{Guid.NewGuid():N}";
        var rolePassword = $"pw_{Guid.NewGuid():N}";

        // Create non-superuser role and database via direct superuser connection
        // Mirrors production: each tenant gets a restricted role that owns its database
        var adminBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString)
        {
            Database = "postgres"
        };

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();

            // Create non-superuser role (matches production tga_{instanceId} pattern)
            await using (var roleCmd = new NpgsqlCommand(
                $"CREATE ROLE \"{roleName}\" WITH LOGIN PASSWORD '{rolePassword}' NOSUPERUSER",
                connection))
            {
                await roleCmd.ExecuteNonQueryAsync();
            }

            // Create database owned by the non-superuser role
            await using (var dbCmd = new NpgsqlCommand(
                $"CREATE DATABASE \"{dbName}\" OWNER \"{roleName}\"",
                connection))
            {
                await dbCmd.ExecuteNonQueryAsync();
            }
        }

        // Return connection strings authenticating as the non-superuser role
        var directBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString)
        {
            Database = dbName,
            Username = roleName,
            Password = rolePassword
        };

        var pgBouncerBuilder = new NpgsqlConnectionStringBuilder(PgBouncerConnectionString)
        {
            Database = dbName,
            Username = roleName,
            Password = rolePassword
        };

        return (directBuilder.ConnectionString, pgBouncerBuilder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pgBouncerContainer is not null)
            await _pgBouncerContainer.DisposeAsync();
        if (_postgresContainer is not null)
            await _postgresContainer.DisposeAsync();
        if (_network is not null)
            await _network.DisposeAsync();

        if (_tempConfigPath is not null)
            File.Delete(_tempConfigPath);
        if (_tempUserlistPath is not null)
            File.Delete(_tempUserlistPath);
    }
}
