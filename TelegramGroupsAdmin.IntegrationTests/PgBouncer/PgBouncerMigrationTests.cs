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
    private PgBouncerFixture? _fixture;

    [OneTimeSetUp]
    public async Task FixtureSetup()
    {
        _fixture = new PgBouncerFixture();
        await _fixture.StartAsync();
    }

    [OneTimeTearDown]
    public async Task FixtureTeardown()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Test]
    public async Task MigrateAsync_ThroughPgBouncer_AppliesAllMigrations()
    {
        // Arrange — create a fresh database accessible through PgBouncer
        var (_, pgBouncerConnStr) = await _fixture!.CreateUniqueDatabaseAsync();

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
        var (_, pgBouncerConnStr) = await _fixture!.CreateUniqueDatabaseAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(pgBouncerConnStr);

        await using var migrationContext = new AppDbContext(optionsBuilder.Options);
        await migrationContext.Database.MigrateAsync();

        // Act — perform a basic CRUD operation through PgBouncer
        await using var crudContext = new AppDbContext(optionsBuilder.Options);

        // Query configs table (always available after migration)
        var configCount = await crudContext.Configs.CountAsync();

        // Assert — query succeeded through PgBouncer without errors
        Assert.That(configCount, Is.GreaterThanOrEqualTo(0),
            "Expected to query configs table through PgBouncer without errors");
    }

    [Test]
    public async Task MultipleContexts_ThroughPgBouncer_ConnectionPoolingWorks()
    {
        // Arrange — validates IDbContextFactory pattern works through PgBouncer
        var (_, pgBouncerConnStr) = await _fixture!.CreateUniqueDatabaseAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(pgBouncerConnStr);

        // Migrate first
        await using (var migrationContext = new AppDbContext(optionsBuilder.Options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Act — create and dispose multiple contexts rapidly (simulates IDbContextFactory pattern)
        // If connection pooling through PgBouncer breaks, this will throw
        for (var i = 0; i < 10; i++)
        {
            await using var context = new AppDbContext(optionsBuilder.Options);
            await context.Configs.CountAsync();
        }
    }
}
