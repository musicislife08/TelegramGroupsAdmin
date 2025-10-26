using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Tests.TestHelpers;

namespace TelegramGroupsAdmin.Tests.Migrations;

/// <summary>
/// Critical migration tests from DATABASE_TESTING.md Phase 2.
/// These tests validate production failures that caused downtime.
/// </summary>
[TestFixture]
public class CriticalMigrationTests
{
    /// <summary>
    /// Test 1: System Actor Migration Routing
    ///
    /// Context: AddActorExclusiveArcToAuditLog migration failed because it tried to copy
    /// "system" actor to actor_web_user_id, then create FK to users table.
    /// "system" doesn't exist in users table.
    ///
    /// This test validates the fixed migration correctly routes:
    /// - actor_user_id = "system" → actor_system_identifier = "unknown"
    /// - other actor_user_id values → actor_web_user_id
    /// - NULL actor_user_id → actor_system_identifier = "SYSTEM"
    /// </summary>
    [Test]
    public async Task SystemActorMigrationRouting_ShouldRouteSystemToSystemIdentifier()
    {
        // Arrange - Create database and apply migrations up to (but not including) AddActorExclusiveArcToAuditLog
        using var helper = new MigrationTestHelper();

        // Apply all migrations up to BackfillAutoTrustedUsers (the migration before AddActorExclusiveArcToAuditLog)
        await helper.CreateDatabaseAndMigrateToAsync("20251024213912_BackfillAutoTrustedUsers");

        // First, create a web user that will be referenced in audit_log
        await using (var context = helper.GetDbContext())
        {
            context.Users.Add(new UserRecordDto
            {
                Id = "user-123",
                Email = "user123@test.com",
                NormalizedEmail = "USER123@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = PermissionLevel.Admin,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Seed audit_log with test data using LEGACY schema (before Actor exclusive arc migration)
        await helper.ExecuteSqlAsync(@"
            -- Row 1: System actor (the problematic case that caused the original failure)
            INSERT INTO audit_log (event_type, timestamp, actor_user_id, value)
            VALUES (2, NOW(), 'system', 'System configuration changed');

            -- Row 2: Regular web user actor (references user-123 in users table)
            INSERT INTO audit_log (event_type, timestamp, actor_user_id, value)
            VALUES (5, NOW(), 'user-123', 'User logged in');

            -- Row 3: NULL actor (system event)
            INSERT INTO audit_log (event_type, timestamp, actor_user_id, value)
            VALUES (2, NOW(), NULL, 'Automated cleanup');
        ");

        // Verify legacy data was inserted
        var countBefore = await helper.ExecuteScalarAsync("SELECT COUNT(*) FROM audit_log");
        Assert.That(Convert.ToInt32(countBefore), Is.EqualTo(3), "Should have 3 audit log entries before migration");

        // Act - Apply the AddActorExclusiveArcToAuditLog migration
        await helper.ApplyNextMigrationAsync("20251025003104_AddActorExclusiveArcToAuditLog");

        // Assert - Verify data was migrated correctly

        // Row 1: actor_user_id="system" should route to actor_system_identifier="unknown"
        var systemRow = await helper.ExecuteScalarAsync<string>(@"
            SELECT actor_system_identifier
            FROM audit_log
            WHERE value = 'System configuration changed'
        ");
        Assert.That(systemRow, Is.EqualTo("unknown"),
            "System actor should be routed to actor_system_identifier='unknown'");

        var systemActorWebUserId = await helper.ExecuteScalarAsync(@"
            SELECT actor_web_user_id
            FROM audit_log
            WHERE value = 'System configuration changed'
        ");
        Assert.That(systemActorWebUserId, Is.Null.Or.EqualTo(DBNull.Value),
            "System actor should NOT be copied to actor_web_user_id");

        // Row 2: actor_user_id="user-123" should route to actor_web_user_id="user-123"
        var userRow = await helper.ExecuteScalarAsync<string>(@"
            SELECT actor_web_user_id
            FROM audit_log
            WHERE value = 'User logged in'
        ");
        Assert.That(userRow, Is.EqualTo("user-123"),
            "Regular user actor should be routed to actor_web_user_id");

        var userSystemIdentifier = await helper.ExecuteScalarAsync(@"
            SELECT actor_system_identifier
            FROM audit_log
            WHERE value = 'User logged in'
        ");
        Assert.That(userSystemIdentifier, Is.Null.Or.EqualTo(DBNull.Value),
            "Regular user should NOT have actor_system_identifier");

        // Row 3: actor_user_id=NULL should route to actor_system_identifier="SYSTEM"
        var nullActorRow = await helper.ExecuteScalarAsync<string>(@"
            SELECT actor_system_identifier
            FROM audit_log
            WHERE value = 'Automated cleanup'
        ");
        Assert.That(nullActorRow, Is.EqualTo("SYSTEM"),
            "NULL actor should be routed to actor_system_identifier='SYSTEM'");

        // Verify FK constraint exists and would prevent invalid data
        var fkExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.table_constraints
                WHERE constraint_name = 'FK_audit_log_users_actor_web_user_id'
                AND table_name = 'audit_log'
            )
        ");
        Assert.That(fkExists, Is.True,
            "Foreign key constraint on actor_web_user_id should exist");

        // Verify exclusive arc CHECK constraint exists
        var checkExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.check_constraints
                WHERE constraint_name = 'CK_audit_log_exclusive_actor'
            )
        ");
        Assert.That(checkExists, Is.True,
            "Exclusive arc CHECK constraint should exist");
    }

    /// <summary>
    /// Test 2: Duplicate Translation Prevention
    ///
    /// Context: Found 2 translations for message 22053 despite exclusive arc CHECK constraint.
    /// CHECK constraint validated exclusive arc but didn't prevent duplicates.
    ///
    /// This test validates AddUniqueConstraintToMessageTranslations migration creates
    /// unique partial indexes that prevent duplicate translations for the same message/edit.
    /// </summary>
    [Test]
    public async Task DuplicateTranslationPrevention_ShouldRejectDuplicateMessageTranslation()
    {
        // Arrange - Apply all migrations (including AddUniqueConstraintToMessageTranslations)
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Create a message to attach translation to
        await using (var context = helper.GetDbContext())
        {
            context.Messages.Add(new MessageRecordDto
            {
                MessageId = 1,
                UserId = 100,
                ChatId = 200,
                Timestamp = DateTimeOffset.UtcNow,
                MessageText = "Test message"
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert - Insert first translation (should succeed)
        await using (var context = helper.GetDbContext())
        {
            context.MessageTranslations.Add(new MessageTranslationDto
            {
                MessageId = 1,
                TranslatedText = "First translation",
                DetectedLanguage = "en",
                TranslatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Verify first translation was inserted
        var count1 = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM message_translations WHERE message_id = 1");
        Assert.That(count1, Is.EqualTo(1), "First translation should be inserted successfully");

        // Attempt to insert second translation for same message_id (should fail)
        var exception = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            context.MessageTranslations.Add(new MessageTranslationDto
            {
                MessageId = 1,  // Same message_id as first translation
                TranslatedText = "Second translation (duplicate)",
                DetectedLanguage = "en",
                TranslatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        });

        // Verify exception is due to unique constraint violation
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.InnerException?.Message, Does.Contain("IX_message_translations_message_id")
            .Or.Contain("duplicate key"),
            "Exception should mention unique index violation");

        // Verify only 1 translation exists (duplicate was rejected)
        var count2 = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM message_translations WHERE message_id = 1");
        Assert.That(count2, Is.EqualTo(1), "Should still have only 1 translation after duplicate rejection");

        // Verify unique index exists
        var indexExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE indexname = 'IX_message_translations_message_id'
                AND indexdef LIKE '%UNIQUE%'
            )
        ");
        Assert.That(indexExists, Is.True,
            "Unique partial index on message_id should exist");
    }
}
