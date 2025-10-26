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

    /// <summary>
    /// Test 3: SpamCheckSkipReason Backfill Logic
    ///
    /// Context: AddSpamCheckSkipReasonToMessages migration (2025-10-25) intelligently backfills
    /// existing data based on chat_admins and telegram_users tables to classify why spam checks were skipped.
    ///
    /// This test validates the backfill logic correctly classifies messages by user type with
    /// proper priority handling (admin > trusted > default).
    /// </summary>
    [Test]
    public async Task SpamCheckSkipReasonBackfill_ShouldClassifyMessagesByUserType()
    {
        // Arrange - Apply migrations up to (but not including) AddSpamCheckSkipReasonToMessages
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndMigrateToAsync("20251025031236_AddUniqueConstraintToMessageTranslations");

        // Create a managed chat (required for FK constraint in chat_admins)
        await using (var context = helper.GetDbContext())
        {
            context.ManagedChats.Add(new ManagedChatRecordDto
            {
                ChatId = 100,
                ChatName = "Test Chat",
                IsActive = true,
                AddedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Create telegram users with different statuses
        await using (var context = helper.GetDbContext())
        {
            context.TelegramUsers.AddRange(
                new TelegramUserDto { TelegramUserId = 1001, FirstName = "Admin", IsTrusted = false, FirstSeenAt = DateTimeOffset.UtcNow },
                new TelegramUserDto { TelegramUserId = 2001, FirstName = "Trusted", IsTrusted = true, FirstSeenAt = DateTimeOffset.UtcNow },
                new TelegramUserDto { TelegramUserId = 3001, FirstName = "Regular", IsTrusted = false, FirstSeenAt = DateTimeOffset.UtcNow },
                new TelegramUserDto { TelegramUserId = 4001, FirstName = "Both", IsTrusted = true, FirstSeenAt = DateTimeOffset.UtcNow },
                new TelegramUserDto { TelegramUserId = 5001, FirstName = "Checked", IsTrusted = false, FirstSeenAt = DateTimeOffset.UtcNow }
            );
            await context.SaveChangesAsync();
        }

        // Create chat_admins (user 1001 and 4001 are admins in chat 100)
        await using (var context = helper.GetDbContext())
        {
            context.ChatAdmins.AddRange(
                new ChatAdminRecordDto
                {
                    ChatId = 100,
                    TelegramId = 1001,
                    IsActive = true,
                    PromotedAt = DateTimeOffset.UtcNow,
                    LastVerifiedAt = DateTimeOffset.UtcNow
                },
                new ChatAdminRecordDto
                {
                    ChatId = 100,
                    TelegramId = 4001,
                    IsActive = true,
                    PromotedAt = DateTimeOffset.UtcNow,
                    LastVerifiedAt = DateTimeOffset.UtcNow
                }
            );
            await context.SaveChangesAsync();
        }

        // Create messages (using raw SQL since spam_check_skip_reason column doesn't exist yet)
        await helper.ExecuteSqlAsync(@"
            -- Message 1: Admin user (should become UserAdmin=2)
            INSERT INTO messages (message_id, user_id, chat_id, timestamp)
            VALUES (1, 1001, 100, NOW());

            -- Message 2: Trusted user (should become UserTrusted=1)
            INSERT INTO messages (message_id, user_id, chat_id, timestamp)
            VALUES (2, 2001, 100, NOW());

            -- Message 3: Regular user (should stay NotSkipped=0, old data)
            INSERT INTO messages (message_id, user_id, chat_id, timestamp)
            VALUES (3, 3001, 100, NOW());

            -- Message 4: Both admin and trusted (should become UserAdmin=2, admin priority)
            INSERT INTO messages (message_id, user_id, chat_id, timestamp)
            VALUES (4, 4001, 100, NOW());

            -- Message 5: Has detection results (should stay NotSkipped=0, was checked)
            INSERT INTO messages (message_id, user_id, chat_id, timestamp)
            VALUES (5, 5001, 100, NOW());
        ");

        // Add detection result for message 5 using DbContext
        await using (var context = helper.GetDbContext())
        {
            context.DetectionResults.Add(new DetectionResultRecordDto
            {
                MessageId = 5,
                Confidence = 95,
                NetConfidence = 95,
                DetectionSource = "test",
                DetectionMethod = "manual",
                DetectedAt = DateTimeOffset.UtcNow,
                SystemIdentifier = "test-system",
                UsedForTraining = true
            });
            await context.SaveChangesAsync();
        }

        // Verify legacy data exists
        var countBefore = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM messages");
        Assert.That(countBefore, Is.EqualTo(5), "Should have 5 messages before migration");

        // Act - Apply the AddSpamCheckSkipReasonToMessages migration
        await helper.ApplyNextMigrationAsync("20251026043301_AddSpamCheckSkipReasonToMessages");

        // Assert - Verify backfill logic correctly classified each message

        // Message 1: Admin only → spam_check_skip_reason = 2 (UserAdmin)
        var msg1Reason = await helper.ExecuteScalarAsync<int>(
            "SELECT spam_check_skip_reason FROM messages WHERE message_id = 1");
        Assert.That(msg1Reason, Is.EqualTo(2),
            "Message from admin-only user should be classified as UserAdmin (2)");

        // Message 2: Trusted only → spam_check_skip_reason = 1 (UserTrusted)
        var msg2Reason = await helper.ExecuteScalarAsync<int>(
            "SELECT spam_check_skip_reason FROM messages WHERE message_id = 2");
        Assert.That(msg2Reason, Is.EqualTo(1),
            "Message from trusted-only user should be classified as UserTrusted (1)");

        // Message 3: Regular user → spam_check_skip_reason = 0 (NotSkipped - old data)
        var msg3Reason = await helper.ExecuteScalarAsync<int>(
            "SELECT spam_check_skip_reason FROM messages WHERE message_id = 3");
        Assert.That(msg3Reason, Is.EqualTo(0),
            "Message from regular user should remain NotSkipped (0) - old data assumption");

        // Message 4: Both admin and trusted → spam_check_skip_reason = 2 (UserAdmin - admin priority)
        var msg4Reason = await helper.ExecuteScalarAsync<int>(
            "SELECT spam_check_skip_reason FROM messages WHERE message_id = 4");
        Assert.That(msg4Reason, Is.EqualTo(2),
            "Message from user who is both admin and trusted should be UserAdmin (2) - admin takes priority");

        // Message 5: Has detection_results → spam_check_skip_reason = 0 (NotSkipped - was actually checked)
        var msg5Reason = await helper.ExecuteScalarAsync<int>(
            "SELECT spam_check_skip_reason FROM messages WHERE message_id = 5");
        Assert.That(msg5Reason, Is.EqualTo(0),
            "Message with detection_results should be NotSkipped (0) - was actually checked");

        // Verify the column exists with correct default
        var columnExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_name = 'messages'
                AND column_name = 'spam_check_skip_reason'
                AND column_default = '0'
            )
        ");
        Assert.That(columnExists, Is.True,
            "spam_check_skip_reason column should exist with default value 0");
    }

    /// <summary>
    /// Test 4: Orphaned Foreign Key Protection
    ///
    /// Context: Migrations adding FK constraints failed because existing data had orphaned
    /// references (deleted parents). This is common in production environments.
    ///
    /// This test validates that attempting to create FK constraints on data with orphaned
    /// references properly fails, demonstrating the need for orphan cleanup before adding FKs.
    /// </summary>
    [Test]
    public async Task OrphanedForeignKeyProtection_ShouldFailOnOrphanedReferences()
    {
        // Arrange - Create database and migrate to just before AddMessageTranslations
        using var helper = new MigrationTestHelper();

        // Find the migration before AddMessageTranslations (InitialCreate)
        await helper.CreateDatabaseAndMigrateToAsync("20251024031020_InitialCreate");

        // Create an orphaned translation (message_id=999 doesn't exist in messages table)
        // We need to create the message_translations table manually since the migration hasn't run yet
        await helper.ExecuteSqlAsync(@"
            CREATE TABLE message_translations (
                id BIGSERIAL PRIMARY KEY,
                message_id BIGINT,
                edit_id BIGINT,
                translated_text TEXT NOT NULL,
                detected_language VARCHAR(100) NOT NULL,
                confidence DECIMAL,
                translated_at TIMESTAMPTZ NOT NULL
            );

            -- Insert orphaned translation (message_id=999 doesn't exist)
            INSERT INTO message_translations (message_id, translated_text, detected_language, translated_at)
            VALUES (999, 'Orphaned translation', 'en', NOW());
        ");

        // Verify orphaned data exists
        var orphanCount = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM message_translations WHERE message_id = 999");
        Assert.That(orphanCount, Is.EqualTo(1), "Should have 1 orphaned translation");

        // Verify message 999 does NOT exist
        var messageExists = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM messages WHERE message_id = 999)");
        Assert.That(messageExists, Is.False, "Message 999 should NOT exist (orphaned reference)");

        // Act & Assert - Attempt to add FK constraint (should fail)
        var exception = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                ALTER TABLE message_translations
                ADD CONSTRAINT FK_message_translations_messages_message_id
                FOREIGN KEY (message_id)
                REFERENCES messages(message_id)
                ON DELETE CASCADE;
            ");
        });

        // Verify it's a foreign key violation
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.SqlState, Is.EqualTo("23503"), // Foreign key violation
            "Should fail with foreign key violation error code");
        Assert.That(exception.Message, Does.Contain("violates foreign key constraint")
            .Or.Contain("is not present in table"),
            "Error message should mention FK constraint violation");

        // Verify the orphaned data still exists (transaction rolled back)
        var orphanCountAfter = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM message_translations WHERE message_id = 999");
        Assert.That(orphanCountAfter, Is.EqualTo(1),
            "Orphaned translation should still exist after failed FK creation");

        // Verify FK constraint was NOT created
        var fkExists = await helper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.table_constraints
                WHERE constraint_name = 'FK_message_translations_messages_message_id'
                AND table_name = 'message_translations'
            )
        ");
        Assert.That(fkExists, Is.False,
            "FK constraint should NOT exist after failed creation");
    }
}
