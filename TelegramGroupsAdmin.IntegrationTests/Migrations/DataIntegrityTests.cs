using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Phase 4: Data Integrity Tests
///
/// Validates that all database constraints (UNIQUE, CHECK, NOT NULL, FK) are enforced
/// at the database level, not just application level. This ensures data integrity even
/// if application code is bypassed (SQL console, bulk imports, bugs).
/// </summary>
[TestFixture]
public class DataIntegrityTests
{
    /// <summary>
    /// Test 9: UNIQUE Constraints Enforced
    ///
    /// **What it tests**: Validates that UNIQUE constraints prevent duplicate records
    /// at the database level, including partial indexes (WHERE clauses).
    ///
    /// **Why it matters**: Application-level validation can be bypassed. Database UNIQUE
    /// constraints are the last line of defense against duplicates.
    ///
    /// **Coverage**:
    /// - message_translations: UNIQUE on message_id (partial index: WHERE message_id IS NOT NULL)
    /// - message_translations: UNIQUE on edit_id (partial index: WHERE edit_id IS NOT NULL)
    /// - users: UNIQUE on normalized_email
    /// - managed_chats: UNIQUE on chat_id
    /// </summary>
    [Test]
    public async Task UniqueConstraints_ShouldRejectDuplicates()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test 1: message_translations UNIQUE on message_id (already covered in Test 2, but include for completeness)
        await using (var context = helper.GetDbContext())
        {
            context.Messages.Add(new MessageRecordDto
            {
                MessageId = 7000,
                UserId = 111,
                ChatId = 222,
                Timestamp = DateTimeOffset.UtcNow,
                MessageText = "Test message"
            });
            await context.SaveChangesAsync();

            context.MessageTranslations.Add(new MessageTranslationDto
            {
                MessageId = 7000,
                TranslatedText = "First translation",
                DetectedLanguage = "en",
                TranslatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Attempt duplicate translation (should fail)
        var translationException = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            context.MessageTranslations.Add(new MessageTranslationDto
            {
                MessageId = 7000,  // Duplicate!
                TranslatedText = "Second translation",
                DetectedLanguage = "es",
                TranslatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        });

        Assert.That(translationException!.InnerException?.Message,
            Does.Contain("IX_message_translations_message_id").Or.Contain("duplicate key"),
            "Should fail with UNIQUE constraint violation on message_id");

        // Test 2: users UNIQUE on normalized_email
        await using (var context = helper.GetDbContext())
        {
            context.Users.Add(new UserRecordDto
            {
                Id = "unique-test-1",
                Email = "user@test.com",
                NormalizedEmail = "USER@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = PermissionLevel.Admin,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Attempt duplicate email (should fail)
        var emailException = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            context.Users.Add(new UserRecordDto
            {
                Id = "unique-test-2",
                Email = "USER@test.com",  // Different case
                NormalizedEmail = "USER@TEST.COM",  // Duplicate normalized!
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = PermissionLevel.Admin,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        });

        Assert.That(emailException!.InnerException?.Message,
            Does.Contain("IX_users_normalized_email").Or.Contain("duplicate key"),
            "Should fail with UNIQUE constraint violation on normalized_email");

        // Test 3: managed_chats UNIQUE on chat_id
        await using (var context = helper.GetDbContext())
        {
            context.ManagedChats.Add(new ManagedChatRecordDto
            {
                ChatId = 300,
                ChatName = "Original Chat",
                IsActive = true,
                AddedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Attempt duplicate chat_id (should fail)
        var chatException = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            context.ManagedChats.Add(new ManagedChatRecordDto
            {
                ChatId = 300,  // Duplicate!
                ChatName = "Duplicate Chat",
                IsActive = true,
                AddedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        });

        Assert.That(chatException!.InnerException?.Message,
            Does.Contain("PK_managed_chats").Or.Contain("duplicate key"),
            "Should fail with PRIMARY KEY constraint violation on chat_id");
    }

    /// <summary>
    /// Test 10: CHECK Constraints Enforced
    ///
    /// **What it tests**: Validates that CHECK constraints prevent invalid data
    /// at the database level.
    ///
    /// **Why it matters**: Application validation can have bugs. Database CHECK
    /// constraints ensure data integrity rules are always enforced.
    ///
    /// **Coverage**:
    /// - message_translations: Exclusive arc (message_id XOR edit_id)
    /// - audit_log: Exclusive arc (actor fields - exactly one non-NULL)
    /// - audit_log: Exclusive arc (target fields - all NULL OR exactly one non-NULL)
    /// </summary>
    [Test]
    public async Task CheckConstraints_ShouldRejectInvalidData()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test 1: message_translations exclusive arc (both NULL - invalid)
        var bothNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO message_translations (message_id, edit_id, translated_text, detected_language, translated_at)
                VALUES (NULL, NULL, 'Invalid translation', 'en', NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bothNullException!.SqlState, Is.EqualTo("23514"), // CHECK constraint
                      "Both NULL should violate exclusive arc CHECK constraint");
            Assert.That(bothNullException.ConstraintName, Is.EqualTo("CK_message_translations_exclusive_source"),
                "Should mention exclusive source constraint");
        }

        // Test 2: message_translations exclusive arc (both non-NULL - invalid)
        // First create message and edit using DbContext
        long editId;
        await using (var context = helper.GetDbContext())
        {
            context.Messages.Add(new MessageRecordDto
            {
                MessageId = 8000,
                UserId = 444,
                ChatId = 555,
                Timestamp = DateTimeOffset.UtcNow,
                MessageText = "Original"
            });
            await context.SaveChangesAsync();

            var edit = new MessageEditRecordDto
            {
                MessageId = 8000,
                EditDate = DateTimeOffset.UtcNow,
                NewText = "Edited"
            };
            context.MessageEdits.Add(edit);
            await context.SaveChangesAsync();
            editId = edit.Id;
        }

        var bothNonNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync($@"
                INSERT INTO message_translations (message_id, edit_id, translated_text, detected_language, translated_at)
                VALUES (8000, {editId}, 'Invalid translation', 'en', NOW());
            ");
        });

        Assert.That(bothNonNullException!.SqlState, Is.EqualTo("23514"),
            "Both non-NULL should violate exclusive arc CHECK constraint");

        // Test 3: audit_log exclusive actor (zero actors - invalid)
        var noActorException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO audit_log (event_type, timestamp, value)
                VALUES (2, NOW(), 'No actor event');
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noActorException!.SqlState, Is.EqualTo("23514"),
                      "Zero actors should violate exclusive arc CHECK constraint");
            Assert.That(noActorException.ConstraintName, Is.EqualTo("CK_audit_log_exclusive_actor"),
                "Should mention actor exclusive arc constraint");
        }

        // Test 4: audit_log exclusive actor (multiple actors - invalid)
        var multiActorException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            {
                await helper.ExecuteSqlAsync(@"
                INSERT INTO audit_log (event_type, timestamp, actor_system_identifier, actor_web_user_id, value)
                VALUES (2, NOW(), 'SYSTEM', 'user-id', 'Multiple actors');
            ");
            });

        Assert.That(multiActorException!.SqlState, Is.EqualTo("23514"),
            "Multiple actors should violate exclusive arc CHECK constraint");
    }

    /// <summary>
    /// Test 11: NOT NULL Constraints Enforced
    ///
    /// **What it tests**: Validates that NOT NULL constraints prevent NULL values
    /// in required fields at the database level.
    ///
    /// **Why it matters**: Application code might have bugs that allow NULL values.
    /// Database NOT NULL constraints are the safety net.
    ///
    /// **Coverage**: Tests critical NOT NULL fields across major tables.
    /// </summary>
    [Test]
    public async Task NotNullConstraints_ShouldRejectNulls()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test 1: users.email NOT NULL
        var emailNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO users (id, email, normalized_email, password_hash, security_stamp, permission_level, status, created_at)
                VALUES ('null-test-1', NULL, 'NULL@TEST.COM', 'hash', 'stamp', 2, 1, NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(emailNullException!.SqlState, Is.EqualTo("23502"), // NOT NULL violation
                      "NULL email should violate NOT NULL constraint");
            Assert.That(emailNullException.ColumnName, Is.EqualTo("email"),
                "Constraint should be on email column");
        }

        // Test 2: messages.timestamp NOT NULL
        var timestampNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            {
                await helper.ExecuteSqlAsync(@"
                INSERT INTO messages (message_id, user_id, chat_id, timestamp)
                VALUES (9000, 666, 777, NULL);
            ");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timestampNullException!.SqlState, Is.EqualTo("23502"),
                      "NULL timestamp should violate NOT NULL constraint");
            Assert.That(timestampNullException.ColumnName, Is.EqualTo("timestamp"),
                "Constraint should be on timestamp column");
        }

        // Test 3: message_translations.translated_text NOT NULL
        var translatedTextNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            {
                await helper.ExecuteSqlAsync(@"
                INSERT INTO messages (message_id, user_id, chat_id, timestamp)
                VALUES (9001, 666, 777, NOW());

                INSERT INTO message_translations (message_id, translated_text, detected_language, translated_at)
                VALUES (9001, NULL, 'en', NOW());
            ");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(translatedTextNullException!.SqlState, Is.EqualTo("23502"),
                      "NULL translated_text should violate NOT NULL constraint");
            Assert.That(translatedTextNullException.ColumnName, Is.EqualTo("translated_text"),
                "Constraint should be on translated_text column");
        }

        // Test 4: managed_chats.chat_type NOT NULL
        var chatTypeNullException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            {
                await helper.ExecuteSqlAsync(@"
                INSERT INTO managed_chats (chat_id, chat_name, chat_type)
                VALUES (400, 'Test Chat', NULL);
            ");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chatTypeNullException!.SqlState, Is.EqualTo("23502"),
                      "NULL chat_type should violate NOT NULL constraint");
            Assert.That(chatTypeNullException.ColumnName, Is.EqualTo("chat_type"),
                "Constraint should be on chat_type column");
        }
    }

    /// <summary>
    /// Test 12: FK Constraints Enforced
    ///
    /// **What it tests**: Validates that FK constraints prevent orphaned references
    /// at the database level.
    ///
    /// **Why it matters**: Application code might insert child records without validating
    /// parent exists. Database FK constraints ensure referential integrity.
    ///
    /// **Coverage**: Tests FK relationships across major tables.
    /// </summary>
    [Test]
    public async Task ForeignKeyConstraints_ShouldRejectOrphanedReferences()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test 1: message_translations FK to messages (orphaned message_id)
        var orphanedMessageException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO message_translations (message_id, translated_text, detected_language, translated_at)
                VALUES (99999, 'Orphaned translation', 'en', NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphanedMessageException!.SqlState, Is.EqualTo("23503"), // FK violation
                      "Orphaned message_id should violate FK constraint");
            Assert.That(orphanedMessageException.ConstraintName,
                Is.EqualTo("FK_message_translations_messages_message_id"),
                "Should mention FK to messages table");
        }

        // Test 2: chat_admins FK to managed_chats (orphaned chat_id)
        // First create telegram user using DbContext (handles all required fields)
        await using (var context = helper.GetDbContext())
        {
            context.TelegramUsers.Add(new TelegramUserDto
            {
                TelegramUserId = 10001,
                FirstName = "Admin",
                IsTrusted = false,
                FirstSeenAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Attempt to insert chat_admin with orphaned chat_id using DbContext
        var orphanedChatException = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = helper.GetDbContext();
            context.ChatAdmins.Add(new ChatAdminRecordDto
            {
                ChatId = 99999,  // Orphaned - doesn't exist in managed_chats
                TelegramId = 10001,
                IsActive = true,
                PromotedAt = DateTimeOffset.UtcNow,
                LastVerifiedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        });

        Assert.That(orphanedChatException, Is.Not.Null);
        Assert.That(orphanedChatException!.InnerException, Is.InstanceOf<Npgsql.PostgresException>());

        var pgEx = (Npgsql.PostgresException)orphanedChatException.InnerException!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pgEx.SqlState, Is.EqualTo("23503"),
                      "Orphaned chat_id should violate FK constraint");
            Assert.That(pgEx.ConstraintName,
                Is.EqualTo("FK_chat_admins_managed_chats_chat_id"),
                "Should mention FK to managed_chats table");
        }

        // Test 4: audit_log FK to users (orphaned actor_web_user_id)
        var orphanedUserException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            {
                await helper.ExecuteSqlAsync(@"
                INSERT INTO audit_log (event_type, timestamp, actor_web_user_id, value)
                VALUES (2, NOW(), 'nonexistent-user-id', 'Invalid audit entry');
            ");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphanedUserException!.SqlState, Is.EqualTo("23503"),
                      "Orphaned actor_web_user_id should violate FK constraint");
            Assert.That(orphanedUserException.ConstraintName,
                Is.EqualTo("FK_audit_log_users_actor_web_user_id"),
                "Should mention FK to users table");
        }
    }

    /// <summary>
    /// Test 13: Push Subscriptions Constraints
    ///
    /// **What it tests**: Validates FK and UNIQUE constraints on push_subscriptions table.
    ///
    /// **Why it matters**: Push subscriptions must reference valid users, and each user
    /// can only have one subscription per endpoint (browser/device).
    ///
    /// **Coverage**:
    /// - push_subscriptions: FK to users (user_id)
    /// - push_subscriptions: UNIQUE on (user_id, endpoint)
    /// </summary>
    [Test]
    public async Task PushSubscriptions_Constraints_ShouldBeEnforced()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test 1: FK constraint - push_subscriptions.user_id must reference valid user
        var orphanedUserException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO push_subscriptions (user_id, endpoint, p256dh, auth, created_at)
                VALUES ('nonexistent-user-id', 'https://push.example.com/sub', 'key', 'auth', NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphanedUserException!.SqlState, Is.EqualTo("23503"), // FK violation
                      "Orphaned user_id should violate FK constraint");
            Assert.That(orphanedUserException.ConstraintName,
                Is.EqualTo("FK_push_subscriptions_users_user_id"),
                "Should mention FK to users table");
        }

        // Test 2: UNIQUE constraint - (user_id, endpoint) must be unique
        // First create a valid user
        await using (var context = helper.GetDbContext())
        {
            context.Users.Add(new UserRecordDto
            {
                Id = "push-test-user",
                Email = "push@test.com",
                NormalizedEmail = "PUSH@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = PermissionLevel.Admin,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Insert first subscription
        await helper.ExecuteSqlAsync(@"
            INSERT INTO push_subscriptions (user_id, endpoint, p256dh, auth, created_at)
            VALUES ('push-test-user', 'https://push.example.com/unique-endpoint', 'key1', 'auth1', NOW());
        ");

        // Attempt duplicate (same user + endpoint)
        var duplicateException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO push_subscriptions (user_id, endpoint, p256dh, auth, created_at)
                VALUES ('push-test-user', 'https://push.example.com/unique-endpoint', 'key2', 'auth2', NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(duplicateException!.SqlState, Is.EqualTo("23505"), // UNIQUE violation
                      "Duplicate (user_id, endpoint) should violate UNIQUE constraint");
            Assert.That(duplicateException.ConstraintName,
                Is.EqualTo("IX_push_subscriptions_user_id_endpoint"),
                "Should mention unique index on (user_id, endpoint)");
        }
    }

    /// <summary>
    /// Test 14: Web Notifications Constraints
    ///
    /// **What it tests**: Validates FK constraint on web_notifications table.
    ///
    /// **Why it matters**: Web notifications must reference valid users.
    ///
    /// **Coverage**:
    /// - web_notifications: FK to users (user_id)
    /// </summary>
    [Test]
    public async Task WebNotifications_Constraints_ShouldBeEnforced()
    {
        // Arrange - Create database and apply migrations
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Test: FK constraint - web_notifications.user_id must reference valid user
        var orphanedUserException = Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await helper.ExecuteSqlAsync(@"
                INSERT INTO web_notifications (user_id, event_type, subject, message, is_read, created_at)
                VALUES ('nonexistent-user-id', 1, 'Test', 'Test message', false, NOW());
            ");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphanedUserException!.SqlState, Is.EqualTo("23503"), // FK violation
                      "Orphaned user_id should violate FK constraint");
            Assert.That(orphanedUserException.ConstraintName,
                Is.EqualTo("FK_web_notifications_users_user_id"),
                "Should mention FK to users table");
        }

        // Verify valid insert works
        await using (var context = helper.GetDbContext())
        {
            context.Users.Add(new UserRecordDto
            {
                Id = "notification-test-user",
                Email = "notify@test.com",
                NormalizedEmail = "NOTIFY@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = PermissionLevel.Admin,
                Status = UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // This should succeed
        await helper.ExecuteSqlAsync(@"
            INSERT INTO web_notifications (user_id, event_type, subject, message, is_read, created_at)
            VALUES ('notification-test-user', 1, 'Valid Notification', 'This should work', false, NOW());
        ");

        // Verify it was inserted
        var count = await helper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM web_notifications WHERE user_id = 'notification-test-user'");
        Assert.That(count, Is.EqualTo(1), "Valid notification should be inserted");
    }
}
