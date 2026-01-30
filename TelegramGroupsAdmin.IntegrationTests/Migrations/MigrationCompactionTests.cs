using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Integration tests for MigrationHistoryCompactionService.
/// Tests all 4 scenarios:
/// 1. Fresh database (no history table) → FreshDatabase
/// 2. At baseline migration → NoActionNeeded (hot path)
/// 3. At required v1.5.0 migration → Compacted
/// 4. Behind required migration → IncompatibleState
///
/// Uses Testcontainers.PostgreSQL for isolated real PostgreSQL testing.
/// Each test gets its own database for complete isolation.
/// </summary>
[TestFixture]
public class MigrationCompactionTests
{
    private MigrationTestHelper _testHelper = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public void SetUp()
    {
        _testHelper = new MigrationTestHelper();
        // NOTE: Don't create database here - each test controls its own setup
    }

    [TearDown]
    public void TearDown()
    {
        _dataSource?.Dispose();
        _testHelper?.Dispose();
    }

    /// <summary>
    /// Creates the test database (empty, no tables).
    /// Used when test needs to control migration state manually.
    /// </summary>
    private async Task CreateEmptyDatabaseAsync()
    {
        // Create just the database, no tables (simulates fresh installation)
        var adminBuilder = new NpgsqlConnectionStringBuilder(PostgresFixture.BaseConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_testHelper.DatabaseName}\"", connection);
        await cmd.ExecuteNonQueryAsync();

        // Create data source for service
        _dataSource = new NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build();
    }

    /// <summary>
    /// Creates the __EFMigrationsHistory table structure (without any rows).
    /// </summary>
    private async Task CreateHistoryTableAsync()
    {
        await _testHelper.ExecuteSqlAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            )
            """);
    }

    /// <summary>
    /// Inserts a migration record into the history table.
    /// </summary>
    private async Task InsertMigrationAsync(string migrationId, string productVersion = "10.0.0")
    {
        await _testHelper.ExecuteSqlAsync($"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('{migrationId}', '{productVersion}')
            """);
    }

    /// <summary>
    /// Gets the count of migrations in the history table.
    /// </summary>
    private async Task<int> GetMigrationCountAsync()
    {
        var result = await _testHelper.ExecuteScalarAsync("SELECT COUNT(*)::int FROM \"__EFMigrationsHistory\"");
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Gets the most recent migration ID from the history table.
    /// </summary>
    private async Task<string?> GetLastMigrationAsync()
    {
        var result = await _testHelper.ExecuteScalarAsync("""
            SELECT "MigrationId" FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId" DESC LIMIT 1
            """);
        return result as string;
    }

    private MigrationHistoryCompactionService CreateService()
    {
        var logger = NullLogger<MigrationHistoryCompactionService>.Instance;
        return new MigrationHistoryCompactionService(_dataSource, logger);
    }

    #region Scenario Tests

    [Test]
    public async Task FreshDatabase_NoHistoryTable_ReturnsFreshDatabase()
    {
        // Arrange - Empty database, no tables at all
        await CreateEmptyDatabaseAsync();
        var service = CreateService();

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.FreshDatabase),
            "Fresh database (no history table) should return FreshDatabase");
    }

    [Test]
    public async Task AtBaseline_BaselineMigrationExists_ReturnsNoActionNeeded()
    {
        // Arrange - Database with only baseline migration (hot path scenario)
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();
        await InsertMigrationAsync(MigrationCompactionConstants.BaselineMigrationId);
        var service = CreateService();

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.NoActionNeeded),
            "Database at baseline should return NoActionNeeded (hot path)");

        // Verify history unchanged
        var count = await GetMigrationCountAsync();
        Assert.That(count, Is.EqualTo(1), "History should remain unchanged with 1 migration");
    }

    [Test]
    public async Task AtRequiredMigration_CompactsHistoryToBaseline()
    {
        // Arrange - Simulate a v1.5.0 database by manually inserting the required migration
        // (We can't use CreateDatabaseAndApplyMigrationsAsync because old migrations are deleted)
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();

        // Insert multiple old migrations to simulate a real v1.5.0 database
        await InsertMigrationAsync("20251024031020_InitialCreate");
        await InsertMigrationAsync("20251024172206_AddMessageTranslations");
        await InsertMigrationAsync(MigrationCompactionConstants.LastRequiredMigration); // v1.5.0

        var service = CreateService();

        // Verify we have multiple migrations before compaction
        var countBefore = await GetMigrationCountAsync();
        Assert.That(countBefore, Is.EqualTo(3),
            "Should have 3 migrations before compaction (simulated v1.5.0 database)");

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.Compacted),
            "Database at required migration should compact and return Compacted");

        // Verify history was compacted to single baseline entry
        var countAfter = await GetMigrationCountAsync();
        Assert.That(countAfter, Is.EqualTo(1), "History should be compacted to 1 row");

        var lastMigration = await GetLastMigrationAsync();
        Assert.That(lastMigration, Is.EqualTo(MigrationCompactionConstants.BaselineMigrationId),
            "Single remaining migration should be the baseline");
    }

    [Test]
    public async Task BehindRequired_HistoryExistsButOldMigration_ReturnsIncompatibleState()
    {
        // Arrange - Database with old migration (not at v1.5.0)
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();
        await InsertMigrationAsync("20251024031020_InitialCreate"); // Very old migration
        var service = CreateService();

        // Verify initial state
        var countBefore = await GetMigrationCountAsync();
        Assert.That(countBefore, Is.EqualTo(1), "Should have exactly 1 old migration");

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.IncompatibleState),
            "Database behind required migration should return IncompatibleState");

        // Verify history unchanged (no compaction occurred)
        var countAfter = await GetMigrationCountAsync();
        Assert.That(countAfter, Is.EqualTo(1), "History should be unchanged");

        var lastMigration = await GetLastMigrationAsync();
        Assert.That(lastMigration, Is.EqualTo("20251024031020_InitialCreate"),
            "Migration should remain unchanged (no compaction)");
    }

    [Test]
    public async Task EmptyHistoryTable_ButTableExists_ReturnsIncompatibleState()
    {
        // Arrange - History table exists but is empty (unusual state)
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();
        // Don't insert any migrations
        var service = CreateService();

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert - Empty history table is incompatible (not fresh, not at required)
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.IncompatibleState),
            "Empty history table (exists but no migrations) should return IncompatibleState");
    }

    [Test]
    public async Task BaselinePlusAdditionalMigrations_ReturnsNoActionNeeded()
    {
        // Arrange - Database with baseline + additional migrations applied after
        // Simulates a database that was compacted, then had new migrations applied
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();
        await InsertMigrationAsync(MigrationCompactionConstants.BaselineMigrationId);
        await InsertMigrationAsync("20260201000000_FutureFeature"); // Newer migration after baseline
        await InsertMigrationAsync("20260301000000_AnotherFeature"); // Even newer
        var service = CreateService();

        // Act
        var result = await service.CompactIfEligibleAsync();

        // Assert - Should return NoActionNeeded since baseline exists
        Assert.That(result, Is.EqualTo(MigrationCompactionResult.NoActionNeeded),
            "Database with baseline + additional migrations should return NoActionNeeded");

        // Verify history unchanged (no compaction should occur)
        var count = await GetMigrationCountAsync();
        Assert.That(count, Is.EqualTo(3), "History should remain unchanged with all 3 migrations");
    }

    #endregion

    #region Idempotency Tests

    [Test]
    public async Task CompactionIsIdempotent_MultipleCallsReturnNoActionNeeded()
    {
        // Arrange - Simulate a v1.5.0 database
        await CreateEmptyDatabaseAsync();
        await CreateHistoryTableAsync();
        await InsertMigrationAsync("20251024031020_InitialCreate");
        await InsertMigrationAsync(MigrationCompactionConstants.LastRequiredMigration);

        var service = CreateService();

        // First call - should compact
        var firstResult = await service.CompactIfEligibleAsync();
        Assert.That(firstResult, Is.EqualTo(MigrationCompactionResult.Compacted));

        // Act - Second call should be idempotent (hot path)
        var secondResult = await service.CompactIfEligibleAsync();

        // Assert
        Assert.That(secondResult, Is.EqualTo(MigrationCompactionResult.NoActionNeeded),
            "Second call after compaction should return NoActionNeeded (idempotent)");

        // Verify still single baseline
        var count = await GetMigrationCountAsync();
        Assert.That(count, Is.EqualTo(1), "Should still have exactly 1 migration");
    }

    #endregion
}
