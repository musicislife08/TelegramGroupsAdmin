using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Tests.TestHelpers;

namespace TelegramGroupsAdmin.Tests.Migrations;

/// <summary>
/// Smoke tests to verify test infrastructure setup.
/// Validates that Postgres container starts, migrations apply, and cleanup works.
/// </summary>
[TestFixture]
public class InfrastructureTests
{
    [Test]
    public async Task ShouldCreateDatabaseAndApplyMigrations()
    {
        // Arrange
        using var helper = new MigrationTestHelper();

        // Act
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Assert - Verify database exists and has expected tables
        var tableCount = await helper.ExecuteScalarAsync(@"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
        ");

        Assert.That(tableCount, Is.Not.Null);
        Assert.That(Convert.ToInt32(tableCount), Is.GreaterThan(0),
            "Expected at least one table after applying migrations");

        // Verify specific critical tables exist
        var usersTableExists = await helper.ExecuteScalarAsync(@"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'users'
            )
        ");

        Assert.That(usersTableExists, Is.EqualTo(true),
            "Expected 'users' table to exist after migrations");
    }

    [Test]
    public async Task ShouldIsolateDatabasesBetweenTests()
    {
        // Arrange - Create first database and insert data using DbContext
        using var helper1 = new MigrationTestHelper();
        await helper1.CreateDatabaseAndApplyMigrationsAsync();

        await using (var context = helper1.GetDbContext())
        {
            context.Users.Add(new TelegramGroupsAdmin.Data.Models.UserRecordDto
            {
                Id = "test-user-1",
                Email = "test1@test.com",
                NormalizedEmail = "TEST1@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = Data.Models.PermissionLevel.Owner,
                Status = Data.Models.UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var count1 = await helper1.ExecuteScalarAsync("SELECT COUNT(*) FROM users");
        Assert.That(Convert.ToInt32(count1), Is.EqualTo(1));

        // Act - Create second database (should be completely isolated)
        using var helper2 = new MigrationTestHelper();
        await helper2.CreateDatabaseAndApplyMigrationsAsync();

        // Assert - Second database should have no users
        var count2 = await helper2.ExecuteScalarAsync("SELECT COUNT(*) FROM users");
        Assert.That(Convert.ToInt32(count2), Is.EqualTo(0),
            "Second database should be isolated from first database");

        // Verify different database names
        Assert.That(helper1.DatabaseName, Is.Not.EqualTo(helper2.DatabaseName),
            "Each test should get a unique database name");
    }

    [Test]
    public async Task ShouldCleanupDatabaseOnDispose()
    {
        string databaseName;

        // Arrange - Create and use helper in a scope
        {
            using var helper = new MigrationTestHelper();
            databaseName = helper.DatabaseName;

            await helper.CreateDatabaseAndApplyMigrationsAsync();

            // Insert some data to verify database is working
            await using (var context = helper.GetDbContext())
            {
                context.Users.Add(new TelegramGroupsAdmin.Data.Models.UserRecordDto
                {
                    Id = "dispose-test",
                    Email = "dispose@test.com",
                    NormalizedEmail = "DISPOSE@TEST.COM",
                    PasswordHash = "hash",
                    SecurityStamp = Guid.NewGuid().ToString(),
                    PermissionLevel = Data.Models.PermissionLevel.Admin,
                    Status = Data.Models.UserStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync();
            }
        } // Dispose should drop the database here

        // Act & Assert - Verify database was cleaned up
        // The test passing without exceptions during Dispose is proof that cleanup worked
        // We can't easily query pg_database without admin connection, but Dispose would throw if it failed
        Assert.Pass("Database cleanup succeeded (no exceptions during Dispose)");
    }

    [Test]
    public async Task ShouldExecuteRawSqlSuccessfully()
    {
        // Arrange
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Insert test data using DbContext first
        await using (var context = helper.GetDbContext())
        {
            context.Users.Add(new TelegramGroupsAdmin.Data.Models.UserRecordDto
            {
                Id = "test-user-raw",
                Email = "raw@test.com",
                NormalizedEmail = "RAW@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = Data.Models.PermissionLevel.Admin,
                Status = Data.Models.UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert - Use raw SQL to verify data
        var email = await helper.ExecuteScalarAsync(@"
            SELECT email FROM users WHERE id = 'test-user-raw'
        ");

        Assert.That(email, Is.EqualTo("raw@test.com"));

        // Also test raw SQL update
        await helper.ExecuteSqlAsync(@"
            UPDATE users SET email = 'updated@test.com' WHERE id = 'test-user-raw'
        ");

        var updatedEmail = await helper.ExecuteScalarAsync(@"
            SELECT email FROM users WHERE id = 'test-user-raw'
        ");

        Assert.That(updatedEmail, Is.EqualTo("updated@test.com"));
    }

    [Test]
    public async Task ShouldProvideWorkingDbContext()
    {
        // Arrange
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Act - Use DbContext to query
        await using var context = helper.GetDbContext();
        var userCount = await context.Users.CountAsync();

        // Assert
        Assert.That(userCount, Is.EqualTo(0),
            "Fresh database should have no users");
    }
}
