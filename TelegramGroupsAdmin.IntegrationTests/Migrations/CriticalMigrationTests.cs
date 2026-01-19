using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Critical migration tests for validating manually edited or complex migrations.
///
/// When adding new migrations that transform existing data, follow this pattern:
/// 1. CreateDatabaseAndMigrateToAsync("migration_BEFORE_target")
/// 2. Seed test data using raw SQL (use schema as it exists at that point)
/// 3. ApplyNextMigrationAsync("target_migration")
/// 4. Assert schema changes and data transformations applied correctly
/// </summary>
[TestFixture]
public class CriticalMigrationTests
{
    /// <summary>
    /// Validates the manually edited UnifiedReviewsAndExamSessions migration.
    ///
    /// This migration was edited post-generation to:
    /// - Keep 'reports' table name (not rename to 'reviews')
    /// - Add type and context columns to existing reports table
    /// - Create exam_sessions table
    /// - Drop impersonation_alerts table
    ///
    /// Test ensures the manual edits don't break the migration chain.
    /// </summary>
    [Test]
    public async Task UnifiedReviewsAndExamSessions_ManuallyEdited_AppliesSuccessfully()
    {
        // Arrange - Create database and apply migrations up to (but not including) the edited migration
        using var helper = new MigrationTestHelper();

        // Apply migrations up to AddEnrichedMessagesView (the one BEFORE our edited migration)
        await helper.CreateDatabaseAndMigrateToAsync("20260114023351_AddEnrichedMessagesView");

        // Verify reports table exists (pre-migration state)
        var reportsExistsBefore = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'reports'
            )
        ");
        Assert.That(reportsExistsBefore, Is.True, "Reports table should exist before migration");

        // Verify impersonation_alerts table exists (will be dropped by migration)
        var impersonationAlertsExistsBefore = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'impersonation_alerts'
            )
        ");
        Assert.That(impersonationAlertsExistsBefore, Is.True, "Impersonation_alerts table should exist before migration");

        // Verify reports.type column does NOT exist yet
        var typeColumnExistsBefore = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'reports' AND column_name = 'type'
            )
        ");
        Assert.That(typeColumnExistsBefore, Is.False, "Type column should NOT exist before migration");

        // Seed a test report to verify data preservation
        await helper.ExecuteSqlAsync(@"
            INSERT INTO reports (chat_id, message_id, status, reported_at, reported_by_user_name)
            VALUES (-100123456789, 42, 0, now(), 'TestUser');
        ");

        var reportCountBefore = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM reports");
        Assert.That(reportCountBefore, Is.EqualTo(1), "Should have test report before migration");

        // Act - Apply the manually edited migration
        await helper.ApplyNextMigrationAsync("20260117003553_UnifiedReviewsAndExamSessions");

        // Assert - Verify migration applied successfully

        // 1. Reports table still exists (not renamed to 'reviews')
        var reportsExistsAfter = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'reports'
            )
        ");
        Assert.That(reportsExistsAfter, Is.True, "Reports table should still exist after migration");

        // 2. Verify 'reviews' table was NOT created (we removed the rename)
        var reviewsTableExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'reviews'
            )
        ");
        Assert.That(reviewsTableExists, Is.False, "Reviews table should NOT exist (rename was removed)");

        // 3. New columns added to reports table
        var typeColumnExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'reports' AND column_name = 'type'
            )
        ");
        Assert.That(typeColumnExists, Is.True, "Type column should exist after migration");

        var contextColumnExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'reports' AND column_name = 'context'
            )
        ");
        Assert.That(contextColumnExists, Is.True, "Context column should exist after migration");

        // 4. Exam sessions table created
        var examSessionsExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'exam_sessions'
            )
        ");
        Assert.That(examSessionsExists, Is.True, "Exam_sessions table should exist after migration");

        // 5. Impersonation alerts table dropped
        var impersonationAlertsExistsAfter = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'impersonation_alerts'
            )
        ");
        Assert.That(impersonationAlertsExistsAfter, Is.False, "Impersonation_alerts table should be dropped after migration");

        // 6. Report callback contexts has new report_type column
        var reportTypeColumnExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'report_callback_contexts' AND column_name = 'report_type'
            )
        ");
        Assert.That(reportTypeColumnExists, Is.True, "Report_type column should exist on report_callback_contexts");

        // 7. Test data preserved with default type value
        var reportCountAfter = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM reports");
        Assert.That(reportCountAfter, Is.EqualTo(1), "Test report should be preserved");

        var defaultTypeValue = await helper.ExecuteScalarAsync<short>(@"
            SELECT type FROM reports WHERE reported_by_user_name = 'TestUser'
        ");
        Assert.That(defaultTypeValue, Is.EqualTo((short)0), "Existing reports should have default type=0 (ContentReport)");

        // 8. Index on type column created
        var typeIndexExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE tablename = 'reports' AND indexname = 'IX_reports_type'
            )
        ");
        Assert.That(typeIndexExists, Is.True, "Index IX_reports_type should exist");
    }
}
