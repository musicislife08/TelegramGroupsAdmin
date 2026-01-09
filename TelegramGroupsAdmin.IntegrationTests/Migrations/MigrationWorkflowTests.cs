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
    /// Test 13: Migration Ordering (fresh DB)
    ///
    /// **What it tests**: Validates that all migrations can be applied in order to a
    /// fresh database without dependency errors (FK before table exists, etc.).
    ///
    /// **Why it matters**: This simulates a production deployment to a new environment.
    /// If migrations have ordering issues, the deployment will fail catastrophically.
    ///
    /// **Production scenario**: Deploying to a new production instance, or provisioning
    /// a new development environment. All migrations must apply cleanly in sequence.
    ///
    /// **Note**: This test is already partially validated by CreateDatabaseAndApplyMigrationsAsync
    /// in earlier tests, but we make it explicit here to verify the full workflow.
    /// </summary>
    [Test]
    public async Task MigrationOrdering_ShouldApplyAllMigrationsToFreshDatabase()
    {
        // Arrange - Create a fresh database (no tables, no schema)
        using var helper = new MigrationTestHelper();

        // Act - Apply ALL migrations in order
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Assert - Verify all migrations applied successfully

        // 1. Check that __EFMigrationsHistory table exists and has entries
        var migrationCount = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM ""__EFMigrationsHistory""
        ");

        Assert.That(migrationCount, Is.GreaterThan(0),
            "Should have at least one migration applied");

        // 2. Verify all expected tables exist (sample of critical tables)
        var criticalTables = new[]
        {
            "users",
            "telegram_users",
            "managed_chats",
            "messages",
            "message_edits",
            "message_translations",
            "audit_log",
            "chat_admins",
            "detection_results"
        };

        foreach (var tableName in criticalTables)
        {
            var tableExists = await helper.ExecuteScalarAsync<bool>($@"
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                    AND table_name = '{tableName}'
                )
            ");

            Assert.That(tableExists, Is.True,
                $"Critical table '{tableName}' should exist after migrations");
        }

        // 3. Verify FK constraints exist (proves tables created before FKs)
        var fkCount = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_type = 'FOREIGN KEY'
            AND table_schema = 'public'
        ");

        Assert.That(fkCount, Is.GreaterThan(0),
            "Should have foreign key constraints (proves migration ordering correct)");

        // 4. Verify CHECK constraints exist (proves constraints added after data migration)
        var checkCount = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_type = 'CHECK'
            AND table_schema = 'public'
        ");

        Assert.That(checkCount, Is.GreaterThan(0),
            "Should have CHECK constraints (proves schema integrity enforced)");

        // 5. Verify indexes exist
        var indexCount = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
            AND indexname NOT LIKE 'pg_%'  -- Exclude system indexes
        ");

        Assert.That(indexCount, Is.GreaterThan(0),
            "Should have indexes (proves performance optimization applied)");

        // Success! All migrations applied in correct order without errors
        Console.WriteLine($"✅ Successfully applied {migrationCount} migrations to fresh database");
    }

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
    /// rolling back drops the entire schema. Re-enable when a second migration is added.
    /// </summary>
    [Test]
    [Ignore("Requires at least 2 migrations - consolidated to single InitialCreate")]
    public async Task RollbackSafety_ShouldRevertMostRecentMigration()
    {
        // Arrange - Create database and apply all migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

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
