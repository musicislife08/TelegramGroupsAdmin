using TelegramGroupsAdmin.IntegrationTests.TestData;
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

    /// <summary>
    /// Validates that impersonation_alerts data is correctly migrated to the unified reports table.
    ///
    /// This test ensures the data migration SQL properly:
    /// - Maps detected_at → reported_at
    /// - Sets type = 1 (ImpersonationAlert)
    /// - Creates JSONB context with all impersonation-specific fields
    /// - Maps risk_level and verdict enums to strings
    /// - Preserves reviewer relationship (web_user_id, reviewed_by)
    ///
    /// Uses golden dataset SQL scripts for test data (FK dependency order):
    /// - 00_base_telegram_users.sql (suspected_user_id, target_user_id FKs)
    /// - 01_base_web_users.sql (reviewed_by_user_id FK)
    /// - 40_pre_migration_impersonation_alerts.sql (test alerts in old schema)
    /// </summary>
    [Test]
    public async Task UnifiedReviewsAndExamSessions_ImpersonationAlertData_MigratedCorrectly()
    {
        using var helper = new MigrationTestHelper();

        // Arrange - Apply migrations up to (but not including) the data migration
        await helper.CreateDatabaseAndMigrateToAsync("20260114023351_AddEnrichedMessagesView");

        // Seed prerequisite data using golden dataset SQL scripts (FK dependency order)
        await GoldenDataset.LoadSqlScriptAsync("SQL.00_base_telegram_users.sql", helper.ExecuteSqlAsync);
        await GoldenDataset.LoadSqlScriptAsync("SQL.01_base_web_users.sql", helper.ExecuteSqlAsync);
        await GoldenDataset.LoadSqlScriptAsync("SQL.40_pre_migration_impersonation_alerts.sql", helper.ExecuteSqlAsync);

        var alertCountBefore = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM impersonation_alerts");
        Assert.That(alertCountBefore, Is.EqualTo(4), "Should have 4 test alerts before migration");

        // Act - Apply the migration with data migration SQL
        await helper.ApplyNextMigrationAsync("20260117003553_UnifiedReviewsAndExamSessions");

        // Assert - Verify data was migrated correctly

        // 1. All alerts migrated to reports with type=1 (ImpersonationAlert)
        var migratedCount = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM reports WHERE type = 1");
        Assert.That(migratedCount, Is.EqualTo(4), "All 4 impersonation alerts should be migrated");

        // 2. Verify reviewed alert (Alert 1: Critical, ConfirmedScam) has correct status and reviewer
        var reviewedAlert = await helper.ExecuteScalarAsync<string>(@"
            SELECT jsonb_build_object(
                'status', status,
                'reviewed_by', reviewed_by,
                'web_user_id', web_user_id,
                'reviewed_at_not_null', (reviewed_at IS NOT NULL)
            )::text
            FROM reports
            WHERE type = 1 AND chat_id = -1001234567890
            AND context->>'suspectedUserId' = '100001'
        ");
        Assert.That(reviewedAlert, Does.Contain("\"status\": 1"), "Reviewed alert should have status=1 (Reviewed)");
        Assert.That(reviewedAlert, Does.Contain($"\"reviewed_by\": \"{GoldenDataset.Users.User1_Email}\""), "Should have reviewer email");
        Assert.That(reviewedAlert, Does.Contain($"\"web_user_id\": \"{GoldenDataset.Users.User1_Id}\""), "Should have web_user_id FK");
        Assert.That(reviewedAlert, Does.Contain("\"reviewed_at_not_null\": true"), "Should have reviewed_at");

        // 3. Verify pending alert (Alert 2: Medium, no review) has correct status
        var pendingAlert = await helper.ExecuteScalarAsync<string>(@"
            SELECT jsonb_build_object(
                'status', status,
                'reviewed_by', reviewed_by,
                'web_user_id', web_user_id
            )::text
            FROM reports
            WHERE type = 1 AND chat_id = -1001234567890
            AND context->>'suspectedUserId' = '100002'
        ");
        Assert.That(pendingAlert, Does.Contain("\"status\": 0"), "Pending alert should have status=0 (Pending)");
        Assert.That(pendingAlert, Does.Contain("\"reviewed_by\": null"), "Pending alert should have no reviewer");
        Assert.That(pendingAlert, Does.Contain("\"web_user_id\": null"), "Pending alert should have no web_user_id");

        // 4. Verify JSONB context for Critical/ConfirmedScam alert (all fields populated)
        var criticalContext = await helper.ExecuteScalarAsync<string>(@"
            SELECT context::text FROM reports
            WHERE type = 1 AND chat_id = -1001234567890
            AND context->>'suspectedUserId' = '100001'
        ");
        Assert.That(criticalContext, Does.Contain($"\"suspectedUserId\": {GoldenDataset.TelegramUsers.User1_TelegramUserId}"), "Context should have suspectedUserId");
        Assert.That(criticalContext, Does.Contain($"\"targetUserId\": {GoldenDataset.TelegramUsers.User3_TelegramUserId}"), "Context should have targetUserId");
        Assert.That(criticalContext, Does.Contain("\"totalScore\": 100"), "Context should have totalScore");
        Assert.That(criticalContext, Does.Contain("\"riskLevel\": \"critical\""), "Risk level 3 should map to 'critical'");
        Assert.That(criticalContext, Does.Contain("\"nameMatch\": true"), "Context should have nameMatch");
        Assert.That(criticalContext, Does.Contain("\"photoMatch\": true"), "Context should have photoMatch");
        Assert.That(criticalContext, Does.Contain("\"photoSimilarity\": 0.95"), "Context should have photoSimilarity");
        Assert.That(criticalContext, Does.Contain("\"autoBanned\": true"), "Context should have autoBanned");
        Assert.That(criticalContext, Does.Contain("\"verdict\": \"confirmed_scam\""), "Verdict 1 should map to 'confirmed_scam'");

        // 5. Verify JSONB context for pending alert (partial data, nulls)
        var pendingContext = await helper.ExecuteScalarAsync<string>(@"
            SELECT context::text FROM reports
            WHERE type = 1 AND chat_id = -1001234567890
            AND context->>'suspectedUserId' = '100002'
        ");
        Assert.That(pendingContext, Does.Contain("\"riskLevel\": \"medium\""), "Risk level 1 should map to 'medium'");
        Assert.That(pendingContext, Does.Contain("\"nameMatch\": true"), "Context should have nameMatch=true");
        Assert.That(pendingContext, Does.Contain("\"photoMatch\": false"), "Context should have photoMatch=false");
        Assert.That(pendingContext, Does.Contain("\"photoSimilarity\": null"), "Context should have null photoSimilarity");
        Assert.That(pendingContext, Does.Contain("\"autoBanned\": false"), "Context should have autoBanned=false");
        Assert.That(pendingContext, Does.Contain("\"verdict\": null"), "Context should have null verdict");

        // 6. Verify FalsePositive verdict mapping (Alert 3)
        var falsePositiveContext = await helper.ExecuteScalarAsync<string>(@"
            SELECT context::text FROM reports
            WHERE type = 1 AND chat_id = -1009876543210
        ");
        Assert.That(falsePositiveContext, Does.Contain("\"verdict\": \"false_positive\""), "Verdict 0 should map to 'false_positive'");
        Assert.That(falsePositiveContext, Does.Contain("\"riskLevel\": \"low\""), "Risk level 0 should map to 'low'");

        // 7. Verify Whitelisted verdict mapping (Alert 4)
        var whitelistedContext = await helper.ExecuteScalarAsync<string>(@"
            SELECT context::text FROM reports
            WHERE type = 1 AND context->>'suspectedUserId' = '100007'
        ");
        Assert.That(whitelistedContext, Does.Contain("\"verdict\": \"whitelisted\""), "Verdict 2 should map to 'whitelisted'");
        Assert.That(whitelistedContext, Does.Contain("\"riskLevel\": \"high\""), "Risk level 2 should map to 'high'");

        // 8. Verify reported_at was mapped from detected_at
        var reportedAt = await helper.ExecuteScalarAsync<DateTimeOffset>(@"
            SELECT reported_at FROM reports
            WHERE type = 1 AND context->>'suspectedUserId' = '100001'
        ");
        Assert.That(reportedAt.Year, Is.EqualTo(2025), "reported_at should be mapped from detected_at");
        Assert.That(reportedAt.Month, Is.EqualTo(12), "reported_at month should match");
        Assert.That(reportedAt.Day, Is.EqualTo(15), "reported_at day should match");
    }
}
