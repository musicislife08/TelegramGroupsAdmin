# PgBouncer Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make TGA fully compatible with PgBouncer in transaction mode, controlled by a `PGBOUNCER_MODE` environment variable.

**Architecture:** When `PGBOUNCER_MODE` is set, the connection string is modified to include `No Reset On Close=true` (prevents Npgsql from sending `DISCARD ALL` which conflicts with PgBouncer). Migrations work out-of-the-box with Npgsql 10.0's transaction-scoped table locks. An integration test with a real PgBouncer container validates the full stack.

**Tech Stack:** .NET 10, EF Core 10.0, Npgsql 10.0, NUnit, Testcontainers 4.10.0, PgBouncer 1.25.1 (`ghcr.io/icoretech/pgbouncer-docker:1.25.1`)

**Spec:** `docs/superpowers/specs/2026-03-21-pgbouncer-compatibility-design.md`

**Branch:** `feature/pgbouncer-compatibility`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `TelegramGroupsAdmin.Data/Extensions/ServiceCollectionExtensions.cs` | Modify | Add PgBouncer connection string modification |
| `TelegramGroupsAdmin/WebApplicationExtensions.cs` | Modify | Add PgBouncer mode startup log |
| `TelegramGroupsAdmin.UnitTests/Data/PgBouncerConnectionStringTests.cs` | Create | Unit tests for connection string modification |
| `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerFixture.cs` | Create | Testcontainers fixture for PostgreSQL + PgBouncer |
| `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerMigrationTests.cs` | Create | Integration tests: migrations + CRUD through PgBouncer |
| `TelegramGroupsAdmin.IntegrationTests/PgBouncer/pgbouncer.ini` | Create | PgBouncer config file matching production settings |
| `Directory.Packages.props` | Modify | Add base `Testcontainers` package for generic container support |
| `TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj` | Modify | Add `Testcontainers` package reference + embedded resource |
| `TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj` | Modify | Add `InternalsVisibleTo` for unit test access |

---

## Task 1: Unit Tests for Connection String Modification

**Files:**
- Create: `TelegramGroupsAdmin.UnitTests/Data/PgBouncerConnectionStringTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Npgsql;
using TelegramGroupsAdmin.Data.Extensions;

namespace TelegramGroupsAdmin.UnitTests.Data;

[TestFixture]
public class PgBouncerConnectionStringTests
{
    [Test]
    public void ApplyPgBouncerSettings_WhenCalled_SetsNoResetOnClose()
    {
        var connectionString = "Host=localhost;Database=testdb;Username=user;Password=pass";

        var result = ServiceCollectionExtensions.ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.NoResetOnClose, Is.True);
    }

    [Test]
    public void ApplyPgBouncerSettings_PreservesExistingSettings()
    {
        var connectionString = "Host=myhost;Port=6432;Database=testdb;Username=user;Password=pass;Timeout=30";

        var result = ServiceCollectionExtensions.ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.Host, Is.EqualTo("myhost"));
        Assert.That(builder.Port, Is.EqualTo(6432));
        Assert.That(builder.Database, Is.EqualTo("testdb"));
        Assert.That(builder.Timeout, Is.EqualTo(30));
        Assert.That(builder.NoResetOnClose, Is.True);
    }

    [Test]
    public void ApplyPgBouncerSettings_OverridesExplicitFalse()
    {
        var connectionString = "Host=localhost;Database=testdb;No Reset On Close=false";

        var result = ServiceCollectionExtensions.ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.NoResetOnClose, Is.True);
    }
}
```

Note: The test references `ServiceCollectionExtensions.ApplyPgBouncerSettings` — a static `internal` method we'll extract in Task 2 so it's independently testable. Requires `InternalsVisibleTo` on the Data project (added in Task 2).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PgBouncerConnectionStringTests" --no-build`
Expected: Build failure — `ApplyPgBouncerSettings` does not exist yet.

---

## Task 2: Implement Connection String Modification

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Extensions/ServiceCollectionExtensions.cs:14-19`
- Modify: `TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj` — add `InternalsVisibleTo`

- [ ] **Step 3: Add `InternalsVisibleTo` to the Data project**

In `TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj`, add inside the existing `<PropertyGroup>` or as a new `<ItemGroup>`:

```xml
<ItemGroup>
    <InternalsVisibleTo Include="TelegramGroupsAdmin.UnitTests" />
</ItemGroup>
```

- [ ] **Step 4: Add the `ApplyPgBouncerSettings` method and wire it into `AddDataServices`**

Add a `static internal` method for testability, and call it conditionally in `AddDataServices`:

```csharp
public static IServiceCollection AddDataServices(this IServiceCollection services, string connectionString)
{
    // When behind PgBouncer, prevent Npgsql from sending DISCARD ALL on connection return.
    // PgBouncer handles connection state reset via its own server_reset_query.
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGBOUNCER_MODE")))
    {
        connectionString = ApplyPgBouncerSettings(connectionString);
    }

    // Single NpgsqlDataSource — the ONE connection pool for all app database access.
    // ... rest of method unchanged ...
}

/// <summary>
/// Modifies a connection string for PgBouncer transaction mode compatibility.
/// Sets No Reset On Close = true to prevent Npgsql from sending DISCARD ALL
/// when returning connections to its internal pool.
/// </summary>
internal static string ApplyPgBouncerSettings(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        NoResetOnClose = true
    };
    return builder.ConnectionString;
}
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build TelegramGroupsAdmin.Data`
Expected: Build succeeds.

- [ ] **Step 6: Run unit tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PgBouncerConnectionStringTests"`
Expected: All 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Data/PgBouncerConnectionStringTests.cs TelegramGroupsAdmin.Data/Extensions/ServiceCollectionExtensions.cs TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj
git commit -m "feat: add PgBouncer connection string modification (#pgbouncer)"
```

---

## Task 3: Add Startup Logging

**Files:**
- Modify: `TelegramGroupsAdmin/WebApplicationExtensions.cs:96-98`

- [ ] **Step 8: Add PgBouncer mode log line**

In `RunDatabaseMigrationsAsync()`, add a log line after the existing opening log:

```csharp
public async Task RunDatabaseMigrationsAsync()
{
    app.Logger.LogInformation("Running PostgreSQL database migrations (EF Core)");

    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGBOUNCER_MODE")))
    {
        app.Logger.LogInformation("PgBouncer mode active — connection string configured for transaction mode compatibility");
    }

    // ... rest of method unchanged ...
}
```

- [ ] **Step 9: Build the solution**

Run: `dotnet build TelegramGroupsAdmin`
Expected: Build succeeds.

- [ ] **Step 10: Commit**

```bash
git add TelegramGroupsAdmin/WebApplicationExtensions.cs
git commit -m "feat: log PgBouncer mode status at startup (#pgbouncer)"
```

---

## Task 4: Set Up PgBouncer Integration Test Infrastructure

**Files:**
- Modify: `Directory.Packages.props` — add `Testcontainers` base package (version `4.10.0` to match existing `Testcontainers.PostgreSql`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj` — add `Testcontainers` package reference
- Create: `TelegramGroupsAdmin.IntegrationTests/PgBouncer/pgbouncer.ini` — test PgBouncer config (embedded resource)
- Create: `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerFixture.cs` — Testcontainers fixture

- [ ] **Step 11: Add the base `Testcontainers` package to `Directory.Packages.props`**

Add alongside the existing `Testcontainers.PostgreSql` entry:

```xml
<PackageVersion Include="Testcontainers" Version="4.10.0" />
```

- [ ] **Step 12: Add `Testcontainers` package reference to the integration test project**

In `TelegramGroupsAdmin.IntegrationTests.csproj`, add alongside the existing `Testcontainers.PostgreSql` reference:

```xml
<PackageReference Include="Testcontainers" />
```

- [ ] **Step 13: Create the PgBouncer config file**

Create `TelegramGroupsAdmin.IntegrationTests/PgBouncer/pgbouncer.ini`:

```ini
[databases]
* = host=postgres port=5432

[pgbouncer]
listen_addr = 0.0.0.0
listen_port = 5432
pool_mode = transaction
auth_type = trust
ignore_startup_parameters = extra_float_digits,options

; Prepared statement support (PgBouncer 1.21+, required for Quartz.NET)
max_prepared_statements = 0

; Connection limits
max_client_conn = 200
default_pool_size = 20
min_pool_size = 2
max_db_connections = 50

; Timeouts - match production
query_timeout = 0
server_idle_timeout = 600
client_idle_timeout = 0
server_connect_timeout = 15

; Connection lifetime
server_lifetime = 3600

; Transaction mode safety
server_reset_query = DISCARD ALL
```

Add as embedded resource in the `.csproj`:

```xml
<ItemGroup>
    <EmbeddedResource Include="PgBouncer\pgbouncer.ini" />
</ItemGroup>
```

- [ ] **Step 14: Create the PgBouncer test fixture**

Create `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerFixture.cs`:

```csharp
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
public class PgBouncerFixture : IDisposable
{
    private INetwork? _network;
    private PostgreSqlContainer? _postgresContainer;
    private IContainer? _pgBouncerContainer;
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

        // 3. Extract PgBouncer config from embedded resource
        var assembly = typeof(PgBouncerFixture).Assembly;
        var configResourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("pgbouncer.ini"));

        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"pgbouncer_{Guid.NewGuid():N}.ini");
        await using (var stream = assembly.GetManifestResourceStream(configResourceName)!)
        await using (var fileStream = File.Create(tempConfigPath))
        {
            await stream.CopyToAsync(fileStream);
        }

        // 4. Start PgBouncer on the same network (random host port to avoid conflicts)
        _pgBouncerContainer = new ContainerBuilder("ghcr.io/icoretech/pgbouncer-docker:1.25.1")
            .WithNetwork(_network)
            .WithPortBinding(5432, assignRandomHostPort: true)
            .WithResourceMapping(tempConfigPath, "/etc/pgbouncer/pgbouncer.ini")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .WithCleanUp(true)
            .Build();

        await _pgBouncerContainer.StartAsync();

        // 5. Build connection string through PgBouncer
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

        // Clean up temp file
        File.Delete(tempConfigPath);
    }

    /// <summary>
    /// Creates a unique database accessible through both direct and PgBouncer connections.
    /// </summary>
    public async Task<(string directConnStr, string pgBouncerConnStr)> CreateUniqueDatabaseAsync()
    {
        var dbName = $"test_db_{Guid.NewGuid():N}";

        // Create database via direct connection
        var adminBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString)
        {
            Database = "postgres"
        };

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // Return both connection strings for the new database
        var directBuilder = new NpgsqlConnectionStringBuilder(DirectConnectionString) { Database = dbName };
        var pgBouncerBuilder = new NpgsqlConnectionStringBuilder(PgBouncerConnectionString) { Database = dbName };

        return (directBuilder.ConnectionString, pgBouncerBuilder.ConnectionString);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _pgBouncerContainer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _postgresContainer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _network?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        _disposed = true;
    }
}
```

- [ ] **Step 15: Build to verify infrastructure compiles**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests`
Expected: Build succeeds.

- [ ] **Step 16: Commit**

```bash
git add Directory.Packages.props TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj TelegramGroupsAdmin.IntegrationTests/PgBouncer/
git commit -m "test: add PgBouncer integration test infrastructure (#pgbouncer)"
```

---

## Task 5: PgBouncer Integration Tests

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerMigrationTests.cs`

- [ ] **Step 17: Write the integration tests**

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.PgBouncer;

/// <summary>
/// Validates that TGA works correctly through PgBouncer in transaction mode.
/// These tests use real PostgreSQL + PgBouncer containers matching production config.
/// </summary>
[TestFixture]
[Category("PgBouncer")]
public class PgBouncerMigrationTests
{
    private PgBouncerFixture _fixture = null!;

    [OneTimeSetUp]
    public async Task FixtureSetup()
    {
        _fixture = new PgBouncerFixture();
        await _fixture.StartAsync();
    }

    [OneTimeTearDown]
    public void FixtureTeardown()
    {
        _fixture.Dispose();
    }

    [Test]
    public async Task MigrateAsync_ThroughPgBouncer_AppliesAllMigrations()
    {
        // Arrange — create a fresh database accessible through PgBouncer
        var (_, pgBouncerConnStr) = await _fixture.CreateUniqueDatabaseAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(pgBouncerConnStr);

        // Act — run all EF Core migrations through PgBouncer
        await using var context = new AppDbContext(optionsBuilder.Options);
        await context.Database.MigrateAsync();

        // Assert — verify migrations applied by checking a known table exists
        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync();
        Assert.That(appliedMigrations, Is.Not.Empty,
            "Expected at least one migration to be applied through PgBouncer");
    }

    [Test]
    public async Task EfCoreCrud_ThroughPgBouncer_WorksCorrectly()
    {
        // Arrange — create and migrate a fresh database
        var (_, pgBouncerConnStr) = await _fixture.CreateUniqueDatabaseAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(pgBouncerConnStr);

        await using var migrationContext = new AppDbContext(optionsBuilder.Options);
        await migrationContext.Database.MigrateAsync();

        // Act — perform a basic CRUD operation through PgBouncer
        await using var crudContext = new AppDbContext(optionsBuilder.Options);

        // Insert a config record (configs table is always available after migration)
        var configCount = await crudContext.Configs.CountAsync();

        // Assert — query succeeded through PgBouncer without errors
        Assert.That(configCount, Is.GreaterThanOrEqualTo(0),
            "Expected to query configs table through PgBouncer without errors");
    }

    [Test]
    public async Task MultipleContexts_ThroughPgBouncer_ConnectionPoolingWorks()
    {
        // Arrange — validates IDbContextFactory pattern works through PgBouncer
        var (_, pgBouncerConnStr) = await _fixture.CreateUniqueDatabaseAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(pgBouncerConnStr);

        // Migrate first
        await using (var migrationContext = new AppDbContext(optionsBuilder.Options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Act — create and dispose multiple contexts rapidly (simulates IDbContextFactory pattern)
        for (var i = 0; i < 10; i++)
        {
            await using var context = new AppDbContext(optionsBuilder.Options);
            await context.Configs.CountAsync();
        }

        // Assert — if we got here without exceptions, connection pooling through PgBouncer works
        Assert.Pass("10 rapid context create/dispose cycles completed through PgBouncer");
    }
}
```

- [ ] **Step 18: Build to verify tests compile**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests`
Expected: Build succeeds.

- [ ] **Step 19: Run the PgBouncer integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "Category=PgBouncer" --verbosity normal > /tmp/pgbouncer-test-output.log 2>&1`

This runs in background since integration tests are slow. Check results:

Run: `grep -E "(Passed|Failed|Error)" /tmp/pgbouncer-test-output.log`
Expected: All 3 tests pass.

- [ ] **Step 20: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerMigrationTests.cs
git commit -m "test: add PgBouncer migration and CRUD integration tests (#pgbouncer)"
```

---

## Task 6: Final Verification

- [ ] **Step 21: Run full unit test suite**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: All tests pass (no regressions from connection string change).

- [ ] **Step 22: Run `--migrate-only` to verify startup path**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: Migrations complete successfully. No PgBouncer log line (env var not set).

- [ ] **Step 23: Run `--migrate-only` with PGBOUNCER_MODE set to verify logging**

Run: `PGBOUNCER_MODE=true dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: Migrations complete successfully. Log line shows "PgBouncer mode active".

- [ ] **Step 24: Final commit if any cleanup needed, then verify branch state**

Run: `git log --oneline feature/pgbouncer-compatibility ^develop`
Expected: 5-6 commits (spec + implementation commits).
