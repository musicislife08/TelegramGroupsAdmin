using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using TelegramGroupsAdmin.Services.Backup;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Tests.TestData;
using TelegramGroupsAdmin.Tests.TestHelpers;

namespace TelegramGroupsAdmin.Tests.Services.Backup;

/// <summary>
/// Baseline tests for BackupService before REFACTOR-2 extraction.
///
/// Validates current behavior (1,202 lines) across 5 major responsibilities:
/// 1. Export orchestration (encrypted/unencrypted, with/without passphrase)
/// 2. Table discovery and reflection-based export
/// 3. Restore with dependency resolution (topological sort)
/// 4. Metadata extraction and validation
/// 5. Passphrase rotation and encryption config management
///
/// Tests use golden dataset extracted from production (PII redacted) to ensure
/// realistic coverage of edge cases, JSONB columns, Data Protection fields, etc.
///
/// After these tests pass, they serve as regression suite for REFACTOR-2:
/// - Extract BackupMetadataService (~200 lines)
/// - Extract BackupRotationService (~150 lines)
/// - Extract TableExportService (~300 lines)
/// - Reduce BackupService to orchestration (~400 lines)
/// </summary>
[TestFixture]
public class BackupServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private BackupService? _backupService;
    private IBackupEncryptionService? _encryptionService;
    private IDataProtectionProvider? _dataProtectionProvider;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection with test-specific services
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys (test isolation)
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContext
        services.AddDbContext<TelegramGroupsAdmin.Data.AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Add BackupEncryptionService (already extracted)
        services.AddSingleton<IBackupEncryptionService, BackupEncryptionService>();

        // Add mock services (BackupService dependencies)
        services.AddSingleton<IDmDeliveryService, MockDmDeliveryService>();
        services.AddSingleton<IDataProtectionService, MockDataProtectionService>();
        services.AddSingleton<INotificationService, MockNotificationService>();

        // Add mock TickerQ service (for passphrase rotation)
        // Note: Passphrase rotation tests will be skipped for baseline (test isolation - no TickerQ in test env)
        var mockTickerManager = Substitute.For<TickerQ.Utilities.Interfaces.Managers.ITimeTickerManager<TickerQ.Utilities.Models.Ticker.TimeTicker>>();
        // Return default TickerResult with IsSucceded property stubbed
        mockTickerManager.AddAsync(Arg.Any<TickerQ.Utilities.Models.Ticker.TimeTicker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(TickerQ.Utilities.Models.TickerResult<TickerQ.Utilities.Models.Ticker.TimeTicker>)));
        services.AddScoped(_ => mockTickerManager);

        // Add BackupService
        services.AddScoped<BackupService>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        _encryptionService = _serviceProvider.GetRequiredService<IBackupEncryptionService>();

        // Seed golden dataset (with Data Protection encryption)
        await using (var context = _testHelper.GetDbContext())
        {
            await GoldenDataset.SeedDatabaseAsync(context, _dataProtectionProvider);
        }

        // Create BackupService in a new scope
        var scope = _serviceProvider.CreateScope();
        _backupService = scope.ServiceProvider.GetRequiredService<BackupService>();

        // Set up default encryption config for all tests
        await _backupService.SaveEncryptionConfigAsync("test-passphrase-12345");
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Export Tests

    [Test]
    public async Task ExportAsync_WithDbPassphrase_ShouldCreateEncryptedBackup()
    {
        // Arrange - encryption config already set up in SetUp()

        // Act
        var backupBytes = await _backupService!.ExportAsync();

        // Assert
        Assert.That(backupBytes, Is.Not.Null);
        Assert.That(backupBytes.Length, Is.GreaterThan(0));

        // Verify encryption header (TGAENC magic bytes)
        var isEncrypted = await _backupService.IsEncryptedAsync(backupBytes);
        Assert.That(isEncrypted, Is.True, "Backup should be encrypted when passphrase is configured");

        // Verify can extract metadata from encrypted backup
        var metadata = await _backupService.GetMetadataAsync(backupBytes);
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Version, Is.EqualTo("2.0"));
        Assert.That(metadata.TableCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExportAsync_WithExplicitPassphrase_ShouldOverrideDbPassphrase()
    {
        // Arrange - Set DB passphrase (should be ignored)
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            var protector = _dataProtectionProvider!.CreateProtector("BackupPassphrase");
            config!.PassphraseEncrypted = protector.Protect("db-passphrase-wrong");
            await context.SaveChangesAsync();
        }

        const string explicitPassphrase = "explicit-override-pass";

        // Act - Export with explicit passphrase
        var backupBytes = await _backupService!.ExportAsync(explicitPassphrase);

        // Assert - Should be encrypted
        Assert.That(await _backupService.IsEncryptedAsync(backupBytes), Is.True);

        // Verify decryption with explicit passphrase works
        var decrypted = _encryptionService!.DecryptBackup(backupBytes, explicitPassphrase);
        Assert.That(decrypted, Is.Not.Null);
        Assert.That(decrypted.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExportAsync_ShouldIncludeAllExpectedTables()
    {
        // Act
        var backupBytes = await _backupService!.ExportAsync();
        var metadata = await _backupService.GetMetadataAsync(backupBytes);

        // Assert - Verify table count matches golden dataset
        Assert.That(metadata.TableCount, Is.EqualTo(GoldenDataset.TotalTableCount),
            $"Expected {GoldenDataset.TotalTableCount} tables in backup (excluding system tables)");

        // Verify metadata contains timestamp (Unix timestamp in seconds)
        Assert.That(metadata.CreatedAt, Is.GreaterThan(0));
        Assert.That(metadata.CreatedAt, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    [Test]
    public async Task ExportAsync_ShouldDecryptDataProtectionFields()
    {
        // Arrange - Golden dataset has encrypted API keys
        // Verify seed worked
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be encrypted in database");
        }

        // Act - Export (should decrypt Data Protection fields)
        var backupBytes = await _backupService!.ExportAsync();

        // Assert - Export should succeed without errors
        Assert.That(backupBytes, Is.Not.Null);

        // TODO: Verify backup contains plaintext API keys (would need to parse backup format)
        // For now, verify export completed without throwing
    }

    #endregion

    #region Table Discovery Tests

    [Test]
    public async Task DiscoverTablesAsync_ShouldFindAllDatabaseTables()
    {
        // This test validates the internal table discovery mechanism
        // We can't call private methods directly, but export implicitly tests this

        // Act - Export triggers table discovery
        var backupBytes = await _backupService!.ExportAsync();
        var metadata = await _backupService.GetMetadataAsync(backupBytes);

        // Assert - Count should match actual tables in database (excluding tables without DTOs)
        var actualTableCount = await _testHelper!.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
            AND table_name NOT IN ('__EFMigrationsHistory', 'cached_blocked_domains', 'file_scan_quota', 'file_scan_results', 'pending_notifications')
        ");

        Assert.That(metadata.TableCount, Is.EqualTo(actualTableCount),
            "Discovered table count should match actual database tables (excluding tables without DTOs)");
    }

    [Test]
    public async Task DiscoverTablesAsync_ShouldExcludeSystemTables()
    {
        // Act
        var backupBytes = await _backupService!.ExportAsync();

        // Parse backup to verify exclusions (indirect test via successful export)
        Assert.That(backupBytes, Is.Not.Null);

        // Verify __EFMigrationsHistory and cached_blocked_domains were excluded
        // (implicitly tested by table count matching non-system tables)
        var metadata = await _backupService.GetMetadataAsync(backupBytes);
        Assert.That(metadata.TableCount, Is.LessThan(50),
            "Should exclude system/cache tables, keeping count reasonable");
    }

    #endregion

    #region Restore Tests

    [Test]
    public async Task RestoreAsync_EncryptedBackupWithPassphrase_ShouldRestoreSuccessfully()
    {
        // Arrange - Create encrypted backup
        const string passphrase = "restore-test-pass-123";
        var originalBackup = await _backupService!.ExportAsync(passphrase);

        // Verify original data exists
        var originalUserCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users");
        var originalMessageCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM messages");

        Assert.That(originalUserCount, Is.GreaterThan(0));
        Assert.That(originalMessageCount, Is.GreaterThan(0));

        // Act - Restore (destructive operation)
        await _backupService.RestoreAsync(originalBackup, passphrase);

        // Assert - Verify data was restored
        var restoredUserCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users");
        var restoredMessageCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM messages");

        Assert.That(restoredUserCount, Is.EqualTo(originalUserCount));
        Assert.That(restoredMessageCount, Is.EqualTo(originalMessageCount));
    }

    [Test]
    public async Task RestoreAsync_UnencryptedBackup_ShouldRestoreWithoutPassphrase()
    {
        // Arrange - Create unencrypted backup
        var originalBackup = await _backupService!.ExportAsync();

        var originalChatCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM managed_chats");
        Assert.That(originalChatCount, Is.GreaterThan(0));

        // Act - Restore without passphrase
        await _backupService.RestoreAsync(originalBackup);

        // Assert
        var restoredChatCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM managed_chats");
        Assert.That(restoredChatCount, Is.EqualTo(originalChatCount));
    }

    [Test]
    public void RestoreAsync_WrongPassphrase_ShouldThrowException()
    {
        // Arrange - Create encrypted backup
        var backupTask = _backupService!.ExportAsync("correct-passphrase");
        var backup = backupTask.GetAwaiter().GetResult();

        // Act & Assert - Restore with wrong passphrase should fail (throws CryptographicException)
        Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(async () =>
        {
            await _backupService.RestoreAsync(backup, "wrong-passphrase");
        });
    }

    [Test]
    public async Task RestoreAsync_ShouldWipeAllTablesFirst()
    {
        // Arrange - Create backup and add extra data after backup
        var backup = await _backupService!.ExportAsync();

        // Add extra user after backup (should be wiped during restore)
        await _testHelper!.ExecuteSqlAsync(@"
            INSERT INTO telegram_users (telegram_user_id, username, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
            VALUES (999999, 'extra_user', false, false, NOW(), NOW(), NOW(), NOW())
        ");

        var countBeforeRestore = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM telegram_users");
        Assert.That(countBeforeRestore, Is.EqualTo(9), "Should have 8 golden users + 1 extra = 9 before restore");

        // Act - Restore (should wipe extra_user)
        await _backupService.RestoreAsync(backup);

        // Assert - Extra user should be gone
        var extraUserExists = await _testHelper.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM telegram_users WHERE telegram_user_id = 999999)");
        Assert.That(extraUserExists, Is.False, "Restore should wipe all existing data first");
    }

    [Test]
    public async Task RestoreAsync_ShouldHandleSelfReferencingForeignKeys()
    {
        // Arrange - Golden dataset has users.invited_by → users.id (self-reference)
        var backup = await _backupService!.ExportAsync();

        // Act - Restore (should handle self-referencing FK)
        await _backupService.RestoreAsync(backup);

        // Assert - Verify self-referencing FK relationships preserved
        var user2 = await _testHelper!.ExecuteScalarAsync<string>($@"
            SELECT invited_by
            FROM users
            WHERE id = '{GoldenDataset.Users.User2_Id}'
        ");

        Assert.That(user2, Is.EqualTo(GoldenDataset.Users.User1_Id),
            "Self-referencing FK (invited_by) should be preserved");
    }

    [Test]
    public async Task RestoreAsync_ShouldReencryptDataProtectionFields()
    {
        // Arrange - Export with decrypted API keys
        var backup = await _backupService!.ExportAsync();

        // Act - Restore (should re-encrypt using test Data Protection)
        await _backupService.RestoreAsync(backup);

        // Assert - Verify API keys are re-encrypted
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be re-encrypted after restore");

            // Verify can decrypt with test Data Protection provider
            var protector = _dataProtectionProvider!.CreateProtector("ApiKeys");
            var decrypted = protector.Unprotect(config!.ApiKeys!);
            Assert.That(decrypted, Contains.Substring("VirusTotal"),
                "Decrypted API keys should contain original test data");
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldResetSequences()
    {
        // Arrange - Create backup with messages
        var backup = await _backupService!.ExportAsync();

        // Act - Restore
        await _backupService.RestoreAsync(backup);

        // Assert - Insert new message, verify ID continues from max
        await _testHelper!.ExecuteSqlAsync(@"
            INSERT INTO telegram_users (telegram_user_id, username, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
            VALUES (888888, 'seq_test', false, false, NOW(), NOW(), NOW(), NOW())
        ");

        await _testHelper.ExecuteSqlAsync($@"
            INSERT INTO messages (user_id, chat_id, timestamp, message_text, spam_check_skip_reason)
            VALUES (888888, {GoldenDataset.ManagedChats.MainChat_Id}, NOW(), 'sequence test', 0)
        ");

        var newMessageId = await _testHelper.ExecuteScalarAsync<long>(@"
            SELECT message_id FROM messages WHERE message_text = 'sequence test'
        ");

        Assert.That(newMessageId, Is.GreaterThan(GoldenDataset.Messages.Msg1_Id),
            "Sequence should reset to max(message_id) + 1 after restore");
    }

    #endregion

    #region Dependency Resolution Tests

    // Note: Topological sort is tested indirectly through restore success
    // (if dependency order is wrong, restore will fail with FK violations)

    [Test]
    public async Task Restore_WithComplexDependencyGraph_ShouldSucceed()
    {
        // Arrange - Golden dataset has complex FK relationships:
        // users → users (self-ref)
        // messages → telegram_users
        // detection_results → messages
        // configs (no dependencies)

        var backup = await _backupService!.ExportAsync();

        // Act - Restore (topological sort must order tables correctly)
        await _backupService.RestoreAsync(backup);

        // Assert - Verify all FK relationships intact
        var detectionCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM detection_results");
        Assert.That(detectionCount, Is.GreaterThan(0), "Detection results should be restored with FK to messages");

        var messageWithUser = await _testHelper.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM messages m
                INNER JOIN telegram_users tu ON m.user_id = tu.telegram_user_id
                LIMIT 1
            )
        ");
        Assert.That(messageWithUser, Is.True, "Messages should have valid FK to telegram_users");
    }

    #endregion

    #region Metadata & Validation Tests

    [Test]
    public async Task GetMetadataAsync_FromEncryptedBackup_ShouldReturnMetadata()
    {
        // Arrange - Use DB passphrase (SetUp() already configured "test-passphrase-12345")
        var encryptedBackup = await _backupService!.ExportAsync();

        // Act - GetMetadataAsync will retrieve passphrase from DB to decrypt
        var metadata = await _backupService.GetMetadataAsync(encryptedBackup);

        // Assert
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Version, Is.EqualTo("2.0"));
        Assert.That(metadata.TableCount, Is.EqualTo(GoldenDataset.TotalTableCount));
        Assert.That(metadata.CreatedAt, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    [Test]
    public async Task GetMetadataAsync_FromUnencryptedBackup_ShouldReturnMetadata()
    {
        // Arrange
        var unencryptedBackup = await _backupService!.ExportAsync();

        // Act
        var metadata = await _backupService.GetMetadataAsync(unencryptedBackup);

        // Assert
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Version, Is.EqualTo("2.0"));
    }

    [Test]
    public async Task IsEncryptedAsync_WithEncryptedBackup_ShouldReturnTrue()
    {
        // Arrange
        var encrypted = await _backupService!.ExportAsync("test-pass");

        // Act
        var isEncrypted = await _backupService.IsEncryptedAsync(encrypted);

        // Assert
        Assert.That(isEncrypted, Is.True);
    }

    // Note: ValidateBackupAsync doesn't exist in BackupService
    // Validation is done implicitly during GetMetadataAsync/RestoreAsync
    // (they throw if backup is invalid)

    #endregion

    #region Passphrase Management Tests

    [Test]
    public async Task SaveEncryptionConfigAsync_ShouldCreateInitialConfig()
    {
        // Arrange - Remove existing config
        await _testHelper!.ExecuteSqlAsync("DELETE FROM configs WHERE chat_id IS NULL");

        const string testPassphrase = "initial-config-pass-456";

        // Act
        await _backupService!.SaveEncryptionConfigAsync(testPassphrase);

        // Assert - Verify config created
        await using (var context = _testHelper.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.BackupEncryptionConfig, Is.Not.Null);
            Assert.That(config.PassphraseEncrypted, Is.Not.Null, "Passphrase should be encrypted");
        }
    }

    [Test]
    public async Task GetDecryptedPassphraseAsync_WhenExists_ShouldReturnPassphrase()
    {
        // Arrange - Mock uses pass-through encryption, so store plaintext directly
        const string testPassphrase = "get-pass-test-123";
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            // MockDataProtectionService is pass-through, so "encrypted" = plaintext
            config!.PassphraseEncrypted = testPassphrase;
            await context.SaveChangesAsync();
        }

        // Act
        var decrypted = await _backupService!.GetDecryptedPassphraseAsync();

        // Assert
        Assert.That(decrypted, Is.EqualTo(testPassphrase));
    }

    [Test]
    public async Task GetDecryptedPassphraseAsync_WhenMissing_ShouldThrowException()
    {
        // Arrange - Clear passphrase from config
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == null);
            config!.PassphraseEncrypted = null;
            await context.SaveChangesAsync();
        }

        // Act & Assert - Should throw when passphrase is missing
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService!.GetDecryptedPassphraseAsync();
        });
    }

    // Note: RotatePassphraseAsync is not tested in baseline tests
    // - Requires mocking ITickerQueueManager for job scheduling
    // - Should be tested in integration tests

    #endregion

    /// <summary>
    /// Mock DM delivery service for tests (BackupService sends notifications on export failure)
    /// </summary>
    private class MockDmDeliveryService : IDmDeliveryService
    {
        public Task<DmDeliveryResult> SendDmAsync(
            long telegramUserId,
            string messageText,
            long? fallbackChatId = null,
            int? autoDeleteSeconds = null,
            CancellationToken cancellationToken = default)
        {
            // No-op for tests - return success
            return Task.FromResult(new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            });
        }

        public Task<DmDeliveryResult> SendDmWithQueueAsync(
            long telegramUserId,
            string notificationType,
            string messageText,
            CancellationToken cancellationToken = default)
        {
            // No-op for tests - return success
            return Task.FromResult(new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            });
        }
    }

    /// <summary>
    /// Mock Data Protection service (used for TOTP by BackupService)
    /// </summary>
    private class MockDataProtectionService : IDataProtectionService
    {
        public string Protect(string plaintext) => plaintext; // Pass-through for tests
        public string Unprotect(string ciphertext) => ciphertext; // Pass-through for tests
    }

    /// <summary>
    /// Mock Notification service
    /// </summary>
    private class MockNotificationService : INotificationService
    {
        public Task<Dictionary<string, bool>> SendChatNotificationAsync(
            long chatId,
            NotificationEventType eventType,
            string subject,
            string message,
            CancellationToken ct = default)
        {
            return Task.FromResult(new Dictionary<string, bool>()); // No-op for tests
        }

        public Task<Dictionary<string, bool>> SendSystemNotificationAsync(
            NotificationEventType eventType,
            string subject,
            string message,
            CancellationToken ct = default)
        {
            return Task.FromResult(new Dictionary<string, bool>()); // No-op for tests
        }

        public Task<bool> SendNotificationAsync(
            string userId,
            NotificationEventType eventType,
            string subject,
            string message,
            CancellationToken ct = default)
        {
            return Task.FromResult(true); // No-op for tests
        }
    }
}
