using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Critical migration tests for validating data transformation during migrations.
///
/// NOTE: Most tests were removed during migration consolidation (InitialCreate baseline).
/// One test is preserved as a TEMPLATE for testing future migrations that transform data.
///
/// When adding new migrations that transform existing data, copy the template pattern:
/// 1. Migrate to point BEFORE the new migration
/// 2. Seed test data in the old schema
/// 3. Apply the new migration
/// 4. Verify data was transformed correctly
/// </summary>
[TestFixture]
public class CriticalMigrationTests
{
    /// <summary>
    /// TEMPLATE: Migration Data Transformation Test
    ///
    /// This test is IGNORED because the referenced migrations no longer exist after consolidation.
    /// It's preserved as a pattern for testing future migrations that transform data.
    ///
    /// Pattern:
    /// 1. CreateDatabaseAndMigrateToAsync("migration_BEFORE_target")
    /// 2. Insert test data using raw SQL (old schema)
    /// 3. ApplyNextMigrationAsync("target_migration")
    /// 4. Assert data was transformed correctly
    ///
    /// Example: Testing a migration that routes actor_user_id="system" to actor_system_identifier
    /// </summary>
    [Test]
    [Ignore("Template only - referenced migrations removed during consolidation to InitialCreate")]
    public async Task Template_MigrationDataTransformation()
    {
        // Arrange - Create database and apply migrations up to (but not including) target migration
        using var helper = new MigrationTestHelper();

        // Apply all migrations up to the one BEFORE your target migration
        await helper.CreateDatabaseAndMigrateToAsync("20YYMMDDHHMMSS_MigrationBeforeTarget");

        // Seed test data using raw SQL (use OLD schema column names)
        await helper.ExecuteSqlAsync(@"
            -- Insert test data that will be transformed by the migration
            INSERT INTO some_table (old_column_name, other_field)
            VALUES ('test_value', 'data');
        ");

        // Verify legacy data exists
        var countBefore = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM some_table");
        Assert.That(countBefore, Is.EqualTo(1), "Should have test data before migration");

        // Act - Apply the target migration
        await helper.ApplyNextMigrationAsync("20YYMMDDHHMMSS_TargetMigration");

        // Assert - Verify data was transformed correctly
        var transformedValue = await helper.ExecuteScalarAsync<string>(@"
            SELECT new_column_name FROM some_table WHERE other_field = 'data'
        ");
        Assert.That(transformedValue, Is.EqualTo("expected_transformed_value"),
            "Migration should transform old_column_name to new_column_name");

        // Verify new constraints exist
        var constraintExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.table_constraints
                WHERE constraint_name = 'expected_constraint_name'
            )
        ");
        Assert.That(constraintExists, Is.True, "New constraint should exist after migration");
    }
}
