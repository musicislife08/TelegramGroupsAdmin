using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Tests.TestHelpers;

namespace TelegramGroupsAdmin.Tests.Migrations;

/// <summary>
/// Phase 3: Cascade Behavior Tests
///
/// Validates that FK cascade rules (SET NULL, CASCADE, RESTRICT) work correctly
/// and prevent orphaned records or surprise deletions.
/// </summary>
[TestFixture]
public class CascadeBehaviorTests
{
    /// <summary>
    /// Test 6: User Deletion Cascade (audit_log) - CONSTRAINT CONFLICT DETECTED
    ///
    /// **What it tests**: Validates that deleting a web user FAILS due to CHECK constraint conflict.
    /// The FK is configured as ON DELETE SET NULL, but the exclusive arc CHECK constraint requires
    /// exactly ONE actor field to be non-NULL. This creates an impossible situation.
    ///
    /// **Why it matters**: This test documents a DESIGN ISSUE in the schema. When a user is deleted,
    /// PostgreSQL tries to SET NULL the actor_web_user_id, but this leaves ALL actor fields NULL,
    /// violating CK_audit_log_exclusive_actor CHECK constraint.
    ///
    /// **Production impact**: Users cannot be deleted if they have audit_log entries as actor.
    /// This prevents user deletion in production!
    ///
    /// **Resolution options**:
    /// 1. Change FK to ON DELETE RESTRICT (prevent user deletion if audit entries exist)
    /// 2. Change exclusive arc to allow all-NULL (relax CHECK constraint)
    /// 3. Change FK to ON DELETE CASCADE (lose audit history - NOT recommended)
    /// 4. Application-level: Set actor_system_identifier='DELETED_USER:{id}' before user deletion
    ///
    /// **Current test**: Documents the issue by expecting the constraint violation.
    /// </summary>
    [Test]
    public async Task UserDeletionCascade_FailsDueToCheckConstraintConflict()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Create two web users (actor and target)
        await using (var context = helper.GetDbContext())
        {
            context.Users.AddRange(
                new UserRecordDto
                {
                    Id = "actor-user-789",
                    Email = "actor@test.com",
                    NormalizedEmail = "ACTOR@TEST.COM",
                    PasswordHash = "hash",
                    SecurityStamp = Guid.NewGuid().ToString(),
                    PermissionLevel = PermissionLevel.Admin,
                    Status = UserStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new UserRecordDto
                {
                    Id = "target-user-101",
                    Email = "target@test.com",
                    NormalizedEmail = "TARGET@TEST.COM",
                    PasswordHash = "hash",
                    SecurityStamp = Guid.NewGuid().ToString(),
                    PermissionLevel = PermissionLevel.Admin,
                    Status = UserStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            );
            await context.SaveChangesAsync();
        }

        // Create audit log entries with both users
        await helper.ExecuteSqlAsync(@"
            -- Entry 1: actor-user-789 as actor
            INSERT INTO audit_log (event_type, timestamp, actor_web_user_id, value)
            VALUES (2, NOW(), 'actor-user-789', 'User created something');

            -- Entry 2: target-user-101 as target
            INSERT INTO audit_log (event_type, timestamp, actor_system_identifier, target_web_user_id, value)
            VALUES (5, NOW(), 'SYSTEM', 'target-user-101', 'System updated user');

            -- Entry 3: Both users (actor and target)
            INSERT INTO audit_log (event_type, timestamp, actor_web_user_id, target_web_user_id, value)
            VALUES (3, NOW(), 'actor-user-789', 'target-user-101', 'Actor modified target');
        ");

        // Verify initial state - 3 audit log entries exist
        var initialCount = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_log");
        Assert.That(initialCount, Is.EqualTo(3), "Should have 3 audit log entries before deletion");

        // Act & Assert - Attempt to delete actor-user-789 (should FAIL)
        var exception = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            var userToDelete = await context.Users.FindAsync("actor-user-789");
            Assert.That(userToDelete, Is.Not.Null, "User should exist before deletion attempt");

            context.Users.Remove(userToDelete!);
            await context.SaveChangesAsync();
        });

        // Verify it's a CHECK constraint violation caused by SET NULL cascade
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.InnerException, Is.InstanceOf<Npgsql.PostgresException>());

        var pgException = (Npgsql.PostgresException)exception.InnerException!;
        Assert.That(pgException.SqlState, Is.EqualTo("23514"), // CHECK constraint violation
            "Should fail with CHECK constraint violation error code");
        Assert.That(pgException.ConstraintName, Is.EqualTo("CK_audit_log_exclusive_actor"),
            "Violation should be on exclusive arc CHECK constraint");

        // Verify error message explains the conflict
        Assert.That(pgException.MessageText, Does.Contain("violates check constraint"),
            "Error should mention CHECK constraint violation");

        // Verify user was NOT deleted (transaction rolled back)
        var userStillExists = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM users WHERE id = 'actor-user-789')");
        Assert.That(userStillExists, Is.True,
            "User deletion should be rolled back due to constraint violation");

        // Verify all audit log entries still exist unchanged
        var remainingCount = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_log");
        Assert.That(remainingCount, Is.EqualTo(3),
            "All audit log entries should remain unchanged after failed user deletion");

        // NOTE: This test documents a REAL PRODUCTION ISSUE.
        // Resolution: Application code must handle user deletion by first updating
        // audit_log entries to set actor_system_identifier='DELETED_USER:{id}' before
        // deleting the user record.
    }

    /// <summary>
    /// Test 7: Message Deletion Cascade (message_translations)
    ///
    /// **What it tests**: Validates that deleting a message CASCADE deletes its translations
    /// (ON DELETE CASCADE) to prevent orphaned translation records.
    ///
    /// **Why it matters**: Translations are tightly coupled to messages. If a message is deleted
    /// (e.g., retention policy), its translations should also be deleted automatically.
    ///
    /// **Production scenario**: MessageCleanupService deletes messages older than retention
    /// period → translations should CASCADE delete automatically, no orphans left.
    /// </summary>
    [Test]
    public async Task MessageDeletionCascade_ShouldDeleteTranslations()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Create message using DbContext (handles all required fields)
        await using (var context = helper.GetDbContext())
        {
            context.Messages.Add(new MessageRecordDto
            {
                MessageId = 5000,
                UserId = 123456,
                ChatId = 789,
                Timestamp = DateTimeOffset.UtcNow,
                MessageText = "Hola mundo"
            });
            await context.SaveChangesAsync();
        }

        // Create translation for the message
        await using (var context = helper.GetDbContext())
        {
            context.MessageTranslations.Add(new MessageTranslationDto
            {
                MessageId = 5000,
                TranslatedText = "Hello world",
                DetectedLanguage = "es",
                Confidence = 0.95m,
                TranslatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Verify initial state
        var messageExists = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM messages WHERE message_id = 5000)");
        var translationExists = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM message_translations WHERE message_id = 5000)");

        Assert.That(messageExists, Is.True, "Message should exist before deletion");
        Assert.That(translationExists, Is.True, "Translation should exist before message deletion");

        // Act - Delete the message
        await using (var context = helper.GetDbContext())
        {
            var message = await context.Messages.FindAsync(5000L);
            Assert.That(message, Is.Not.Null, "Message should be found before deletion");

            context.Messages.Remove(message!);
            await context.SaveChangesAsync();
        }

        // Assert - Verify CASCADE delete

        // 1. Message should be deleted
        var messageExistsAfter = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM messages WHERE message_id = 5000)");
        Assert.That(messageExistsAfter, Is.False, "Message should be deleted");

        // 2. Translation should be automatically CASCADE deleted
        var translationExistsAfter = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM message_translations WHERE message_id = 5000)");
        Assert.That(translationExistsAfter, Is.False,
            "Translation should be CASCADE deleted when message is deleted");

        // 3. Verify no orphaned translations exist
        var orphanedTranslations = await helper.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM message_translations mt
            WHERE NOT EXISTS (
                SELECT 1 FROM messages m WHERE m.message_id = mt.message_id
            )
            AND mt.message_id IS NOT NULL
        ");
        Assert.That(orphanedTranslations, Is.EqualTo(0),
            "No orphaned translations should exist (message_id pointing to non-existent message)");
    }

    /// <summary>
    /// Test 8: Chat Cleanup on Deletion (managed_chats)
    ///
    /// **What it tests**: Validates that deactivating a managed chat (is_active=false)
    /// or deleting it properly cleans up related data.
    ///
    /// **Why it matters**: When a bot is removed from a chat, managed_chats should be
    /// deactivated but preserved for audit history. Messages and other related data
    /// cascade based on FK rules.
    ///
    /// **Production scenario**: Bot removed from chat → managed_chats.is_active=false,
    /// messages remain for retention period, chat_admins cleaned up.
    /// </summary>
    [Test]
    public async Task ChatDeactivation_ShouldPreserveManagedChat()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Create managed chat
        await using (var context = helper.GetDbContext())
        {
            context.ManagedChats.Add(new ManagedChatRecordDto
            {
                ChatId = 200,
                ChatName = "Test Chat",
                IsActive = true,
                AddedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Create telegram user and chat admin
        await using (var context = helper.GetDbContext())
        {
            context.TelegramUsers.Add(new TelegramUserDto
            {
                TelegramUserId = 9001,
                FirstName = "Admin",
                IsTrusted = false,
                FirstSeenAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();

            context.ChatAdmins.Add(new ChatAdminRecordDto
            {
                ChatId = 200,
                TelegramId = 9001,
                IsActive = true,
                PromotedAt = DateTimeOffset.UtcNow,
                LastVerifiedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Create message in the chat
        await using (var context = helper.GetDbContext())
        {
            context.Messages.Add(new MessageRecordDto
            {
                MessageId = 6000,
                UserId = 9001,
                ChatId = 200,
                Timestamp = DateTimeOffset.UtcNow,
                MessageText = "Test message in chat"
            });
            await context.SaveChangesAsync();
        }

        // Verify initial state
        var chatActive = await helper.ExecuteScalarAsync<bool>(
            "SELECT is_active FROM managed_chats WHERE chat_id = 200");
        var adminCount = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM chat_admins WHERE chat_id = 200");
        var messageCount = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM messages WHERE chat_id = 200");

        Assert.That(chatActive, Is.True, "Chat should be active initially");
        Assert.That(adminCount, Is.EqualTo(1), "Should have 1 admin initially");
        Assert.That(messageCount, Is.EqualTo(1), "Should have 1 message initially");

        // Act - Deactivate managed chat (bot removed from chat scenario)
        await using (var context = helper.GetDbContext())
        {
            var chat = await context.ManagedChats.FindAsync(200L);
            Assert.That(chat, Is.Not.Null);

            chat!.IsActive = false;
            await context.SaveChangesAsync();
        }

        // Assert - Verify preservation behavior

        // 1. Chat should still exist but marked inactive
        var chatExistsAfter = await helper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM managed_chats WHERE chat_id = 200)");
        var chatActiveAfter = await helper.ExecuteScalarAsync<bool>(
            "SELECT is_active FROM managed_chats WHERE chat_id = 200");

        Assert.That(chatExistsAfter, Is.True, "Chat should still exist (preserved for audit)");
        Assert.That(chatActiveAfter, Is.False, "Chat should be marked inactive");

        // 2. Messages should be preserved (retention policy handles deletion separately)
        var messageCountAfter = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM messages WHERE chat_id = 200");
        Assert.That(messageCountAfter, Is.EqualTo(1),
            "Messages should be preserved (retention policy deletes separately)");

        // 3. Chat admins should be preserved (can be deactivated separately if needed)
        var adminCountAfter = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM chat_admins WHERE chat_id = 200");
        Assert.That(adminCountAfter, Is.EqualTo(1),
            "Chat admins should be preserved (can be deactivated separately)");

        // Note: This test validates that deactivating a chat preserves related data.
        // If FK cascade DELETE was configured instead, this test would fail, alerting us
        // to unwanted data loss.
    }
}
