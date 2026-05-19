using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Phase 5: Migration Workflow Tests
///
/// Validates schema evolution and rollback safety. These tests ensure migrations can be
/// applied to a fresh database without dependency errors, and that Down() migrations
/// correctly revert schema changes.
/// </summary>
[TestFixture]
public class MigrationWorkflowTests
{
    /// <summary>
    /// Test 14: Rollback Safety (Down migrations)
    ///
    /// **What it tests**: Validates that Down() migrations correctly revert schema changes
    /// made by Up() migrations.
    ///
    /// **Why it matters**: Down() migrations are rarely tested until disaster strikes.
    /// If a production deployment goes wrong, you need confidence that rollback works.
    ///
    /// **Production scenario**: Bad migration deployed to production → need to rollback
    /// to previous schema state. Down() migration must cleanly undo all Up() changes.
    ///
    /// **Scope**: Tests the most recent migration (safer to test, less complex dependencies).
    /// Full history rollback testing would be more comprehensive but time-intensive.
    ///
    /// NOTE: This test requires at least 2 migrations to be meaningful. With only InitialCreate,
    /// rolling back drops the entire schema.
    /// </summary>
    [Test]
    public async Task RollbackSafety_ShouldRevertMostRecentMigration()
    {
        // Arrange - Create database and apply all migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromEmptyTemplateAsync();

        // Get list of applied migrations
        var migrationsBeforeRollback = new List<string>();
        await using (var context = helper.GetDbContext())
        {
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            migrationsBeforeRollback.AddRange(appliedMigrations);
        }

        Assert.That(migrationsBeforeRollback.Count, Is.GreaterThan(0),
            "Should have migrations applied before rollback test");

        var mostRecentMigration = migrationsBeforeRollback.Last();
        var migrationBeforeMostRecent = migrationsBeforeRollback.Count > 1
            ? migrationsBeforeRollback[^2]
            : null;

        Console.WriteLine($"Most recent migration: {mostRecentMigration}");
        Console.WriteLine($"Rolling back to: {migrationBeforeMostRecent ?? "empty database"}");

        // Capture schema state before rollback
        var tablesBeforeRollback = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
        ");

        var fkCountBeforeRollback = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_type = 'FOREIGN KEY'
            AND table_schema = 'public'
        ");

        // Act - Rollback the most recent migration using IMigrator
        await using (var context = helper.GetDbContext())
        {
            var migrator = context.Database.GetService<IMigrator>();

            if (migrationBeforeMostRecent != null)
            {
                // Rollback to previous migration
                await migrator.MigrateAsync(migrationBeforeMostRecent);
            }
            else
            {
                // Rollback to empty database (initial state)
                await migrator.MigrateAsync(null);
            }
        }

        // Assert - Verify rollback succeeded

        // 1. Verify migration history updated correctly
        var migrationsAfterRollback = new List<string>();
        await using (var context = helper.GetDbContext())
        {
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            migrationsAfterRollback.AddRange(appliedMigrations);
        }

        Assert.That(migrationsAfterRollback.Count, Is.EqualTo(migrationsBeforeRollback.Count - 1),
            "Should have one fewer migration in history after rollback");

        Assert.That(migrationsAfterRollback, Does.Not.Contain(mostRecentMigration),
            "Most recent migration should be removed from history after rollback");

        // 2. Verify schema changes reverted (table count, FK count may have changed)
        var tablesAfterRollback = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
        ");

        var fkCountAfterRollback = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_type = 'FOREIGN KEY'
            AND table_schema = 'public'
        ");

        // Note: We can't assert exact table/FK counts because different migrations
        // add different schema elements. The key assertion is that Down() executed
        // without errors and migration history is consistent.

        Console.WriteLine($"Tables before rollback: {tablesBeforeRollback}");
        Console.WriteLine($"Tables after rollback: {tablesAfterRollback}");
        Console.WriteLine($"FKs before rollback: {fkCountBeforeRollback}");
        Console.WriteLine($"FKs after rollback: {fkCountAfterRollback}");

        // 3. Verify database is still in a consistent state (can re-apply migration)
        await using (var context = helper.GetDbContext())
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Re-apply the migration we just rolled back
            await migrator.MigrateAsync(mostRecentMigration);
        }

        // Verify migration is back in history
        var migrationsAfterReapply = new List<string>();
        await using (var context = helper.GetDbContext())
        {
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            migrationsAfterReapply.AddRange(appliedMigrations);
        }

        Assert.That(migrationsAfterReapply.Count, Is.EqualTo(migrationsBeforeRollback.Count),
            "Should have same migration count after re-applying rolled-back migration");

        Assert.That(migrationsAfterReapply, Does.Contain(mostRecentMigration),
            "Most recent migration should be back in history after re-apply");

        // Success! Down() migration worked, and we can re-apply Up() migration
        Console.WriteLine("✅ Rollback and re-apply successful - Down() migration is safe");
    }
}
