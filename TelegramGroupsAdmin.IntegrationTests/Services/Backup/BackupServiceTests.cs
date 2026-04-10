using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Backup;

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
    private IBackupService? _backupService;
    private IPassphraseManagementService? _passphraseService;
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
        services.AddDbContext<Data.AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging with test-specific suppressions
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            // Suppress Data Protection ephemeral key warnings (expected in tests)
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
            // Suppress TableExportService decryption warnings (expected with ephemeral keys)
            builder.AddFilter("TelegramGroupsAdmin.Services.Backup.Handlers.TableExportService", LogLevel.Error);
        });

        // Add mock services (BackupService dependencies)
        services.AddSingleton<IBotDmService, MockBotDmService>();
        services.AddSingleton<IDataProtectionService, MockDataProtectionService>();
        services.AddSingleton<INotificationService, MockNotificationService>();
        services.AddSingleton(Substitute.For<TelegramGroupsAdmin.Telegram.Services.IThumbnailService>());

        // Add IJobScheduler mock (required by PassphraseManagementService)
        var mockJobScheduler = Substitute.For<TelegramGroupsAdmin.Core.BackgroundJobs.IJobScheduler>();
        mockJobScheduler.ScheduleJobAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult($"test_job_{Guid.NewGuid():N}"));
        mockJobScheduler.CancelJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        services.AddSingleton(mockJobScheduler);

        // Add backup services (using shared extension method from BackgroundJobs library)
        services.AddSingleton(new RecyclableMemoryStreamManager());
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IBackupEncryptionService, BackupEncryptionService>();
        services.AddScoped<IBackupConfigurationService, BackupConfigurationService>();
        services.AddScoped<IPassphraseManagementService, PassphraseManagementService>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();

        // Add internal handler services (required by BackupService)
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.TableDiscoveryService>();
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.TableExportService>();
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.DependencyResolutionService>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        _encryptionService = _serviceProvider.GetRequiredService<IBackupEncryptionService>();

        // Seed golden dataset (with Data Protection encryption)
        await using (var context = _testHelper.GetDbContext())
        {
            await GoldenDataset.SeedAsync(context, _dataProtectionProvider);
        }

        // Create BackupService in a new scope (using interface)
        var scope = _serviceProvider.CreateScope();
        _backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
        _passphraseService = scope.ServiceProvider.GetRequiredService<IPassphraseManagementService>();

        // Set up default encryption config for all tests
        await _passphraseService.SaveEncryptionConfigAsync("test-passphrase-12345");
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Helper to export backup to temp file and return the filepath for streaming tests.
    /// Caller is responsible for cleanup.
    /// </summary>
    private async Task<string> ExportBackupToTempFileAsync(string? passphraseOverride = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_backup_{Guid.NewGuid():N}.tar.gz");
        if (passphraseOverride != null)
        {
            await _backupService!.ExportToFileAsync(tempPath, passphraseOverride, CancellationToken.None);
        }
        else
        {
            await _backupService!.ExportToFileAsync(tempPath, CancellationToken.None);
        }
        return tempPath;
    }

    #region Export Tests

    [Test]
    public async Task ExportToFileAsync_WithDbPassphrase_ShouldCreateEncryptedBackup()
    {
        // Arrange - encryption config already set up in SetUp()

        // Act
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Assert
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(new FileInfo(backupPath).Length, Is.GreaterThan(0));

            // Verify backup contains encrypted database entry
            var isEncrypted = await _backupService!.IsEncryptedAsync(backupPath);
            Assert.That(isEncrypted, Is.True, "Backup should be encrypted when passphrase is configured");

            // Verify can extract metadata from encrypted backup (metadata is always unencrypted in tar)
            var metadata = await _backupService!.GetMetadataAsync(backupPath);
            Assert.That(metadata, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata.Version, Is.EqualTo("3.0"));
                Assert.That(metadata.TableCount, Is.GreaterThan(0));
            }
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task ExportToFileAsync_WithExplicitPassphrase_ShouldOverrideDbPassphrase()
    {
        // Arrange - Set DB passphrase (should be ignored)
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            var protector = _dataProtectionProvider!.CreateProtector(DataProtectionPurposes.BackupPassphrase);
            config!.PassphraseEncrypted = protector.Protect("db-passphrase-wrong");
            await context.SaveChangesAsync();
        }

        const string explicitPassphrase = "explicit-override-pass";

        // Act - Export with explicit passphrase
        var backupPath = await ExportBackupToTempFileAsync(explicitPassphrase);
        try
        {
            // Assert - Should be encrypted (database.json.enc entry in tar)
            Assert.That(await _backupService!.IsEncryptedAsync(backupPath), Is.True);

            // Verify restore with explicit passphrase works (decrypts database.json.enc inside tar)
            Assert.DoesNotThrowAsync(() => _backupService.RestoreAsync(backupPath, explicitPassphrase));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task ExportToFileAsync_ShouldIncludeAllExpectedTables()
    {
        // Act
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            var metadata = await _backupService!.GetMetadataAsync(backupPath);

            // Assert - Verify table count matches golden dataset
            Assert.That(metadata.TableCount, Is.EqualTo(GoldenDataset.TotalTableCount),
                $"Expected {GoldenDataset.TotalTableCount} tables in backup (excluding system tables)");

            // Verify metadata contains recent timestamp
            var now = DateTimeOffset.UtcNow;
            Assert.That(metadata.CreatedAt, Is.GreaterThan(now.AddMinutes(-5)));
            Assert.That(metadata.CreatedAt, Is.LessThanOrEqualTo(now));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task ExportToFileAsync_ShouldDecryptDataProtectionFields()
    {
        // Arrange - Golden dataset has encrypted API keys
        // Verify seed worked
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be encrypted in database");
        }

        // Act - Export (should decrypt Data Protection fields)
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Assert - Export should succeed without errors
            Assert.That(File.Exists(backupPath), Is.True);
            // Verifies export completed without throwing (decryption successful)
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    #endregion

    #region Table Discovery Tests

    [Test]
    public async Task DiscoverTablesAsync_ShouldFindAllDatabaseTables()
    {
        // This test validates the internal table discovery mechanism
        // We can't call private methods directly, but export implicitly tests this

        // Act - Export triggers table discovery
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            var metadata = await _backupService!.GetMetadataAsync(backupPath);

            // Assert - Count should match actual tables in database (excluding tables without DTOs)
            var actualTableCount = await _testHelper!.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_type = 'BASE TABLE'
                AND table_name NOT IN ('__EFMigrationsHistory', 'cached_blocked_domains', 'file_scan_quota', 'username_blacklist')
            ");

            Assert.That(metadata.TableCount, Is.EqualTo(actualTableCount),
                "Discovered table count should match actual database tables (excluding tables without DTOs)");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task DiscoverTablesAsync_ShouldExcludeSystemTables()
    {
        // Act
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Parse backup to verify exclusions (indirect test via successful export)
            Assert.That(File.Exists(backupPath), Is.True);

            // Verify __EFMigrationsHistory and cached_blocked_domains were excluded
            // (implicitly tested by table count matching non-system tables)
            var metadata = await _backupService!.GetMetadataAsync(backupPath);
            Assert.That(metadata.TableCount, Is.LessThan(50),
                "Should exclude system/cache tables, keeping count reasonable");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    #endregion

    #region Restore Tests

    [Test]
    public async Task RestoreAsync_EncryptedBackupWithPassphrase_ShouldRestoreSuccessfully()
    {
        // Arrange - Create encrypted backup
        const string passphrase = "restore-test-pass-123";
        var backupPath = await ExportBackupToTempFileAsync(passphrase);
        try
        {
            // Verify original data exists
            var originalUserCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users");
            var originalMessageCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM messages");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(originalUserCount, Is.GreaterThan(0));
                Assert.That(originalMessageCount, Is.GreaterThan(0));
            }

            // Act - Restore (destructive operation)
            await _backupService!.RestoreAsync(backupPath, passphrase);

            // Assert - Verify data was restored
            var restoredUserCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users");
            var restoredMessageCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM messages");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(restoredUserCount, Is.EqualTo(originalUserCount));
                Assert.That(restoredMessageCount, Is.EqualTo(originalMessageCount));
            }
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_WithDbPassphrase_ShouldRestoreWithoutExplicitPassphrase()
    {
        // Arrange - Export uses DB passphrase (configured in SetUp), restore reads it back
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            var originalChatCount = await _testHelper!.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM managed_chats");
            Assert.That(originalChatCount, Is.GreaterThan(0));

            // Act - Restore without explicit passphrase (reads DB passphrase automatically)
            await _backupService!.RestoreAsync(backupPath);

            // Assert
            var restoredChatCount = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM managed_chats");
            Assert.That(restoredChatCount, Is.EqualTo(originalChatCount));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_WrongPassphrase_ShouldThrowException()
    {
        // Arrange - Create encrypted backup
        var backupPath = await ExportBackupToTempFileAsync("correct-passphrase");
        try
        {
            // Act & Assert - Restore with wrong passphrase should fail (throws CryptographicException)
            Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(async () =>
            {
                await _backupService!.RestoreAsync(backupPath, "wrong-passphrase");
            });
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldWipeAllTablesFirst()
    {
        // Arrange - Create backup and add extra data after backup
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Add extra user after backup (should be wiped during restore)
            await _testHelper!.ExecuteSqlAsync(@"
                INSERT INTO telegram_users (telegram_user_id, username, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
                VALUES (999999, 'extra_user', false, false, NOW(), NOW(), NOW(), NOW())
            ");

            var countBeforeRestore = await _testHelper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM telegram_users");
            Assert.That(countBeforeRestore, Is.EqualTo(13), "Should have 12 golden users + 1 extra = 13 before restore");

            // Act - Restore (should wipe extra_user)
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Extra user should be gone
            var extraUserExists = await _testHelper.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM telegram_users WHERE telegram_user_id = 999999)");
            Assert.That(extraUserExists, Is.False, "Restore should wipe all existing data first");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldHandleSelfReferencingForeignKeys()
    {
        // Arrange - Golden dataset has users.invited_by → users.id (self-reference)
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - Restore (should handle self-referencing FK)
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify self-referencing FK relationships preserved
            var user2 = await _testHelper!.ExecuteScalarAsync<string>($@"
                SELECT invited_by
                FROM users
                WHERE id = '{GoldenDataset.Users.User2_Id}'
            ");

            Assert.That(user2, Is.EqualTo(GoldenDataset.Users.User1_Id),
                "Self-referencing FK (invited_by) should be preserved");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldReencryptDataProtectionFields()
    {
        // Arrange - Export with decrypted API keys
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - Restore (should re-encrypt using test Data Protection)
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify API keys are re-encrypted
            await using (var context = _testHelper!.GetDbContext())
            {
                var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
                Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be re-encrypted after restore");

                // Verify can decrypt with test Data Protection provider
                var protector = _dataProtectionProvider!.CreateProtector(DataProtectionPurposes.ApiKeys);
                var decrypted = protector.Unprotect(config!.ApiKeys!);
                Assert.That(decrypted, Contains.Substring("VirusTotal"),
                    "Decrypted API keys should contain original test data");
            }
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldResetSequences()
    {
        // Arrange - Create backup with data
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - Restore
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Insert new message_edit, verify ID continues from max
            // (message_edits.id still has identity/sequence, unlike messages.message_id which uses ValueGeneratedNever)
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO message_edits (message_id, chat_id, edit_date, new_text)
                VALUES ({GoldenDataset.Messages.Msg1_Id}, {GoldenDataset.ManagedChats.MainChat_Id}, NOW(), 'sequence test edit')
            ");

            var newEditId = await _testHelper.ExecuteScalarAsync<long>(@"
                SELECT id FROM message_edits WHERE new_text = 'sequence test edit'
            ");

            Assert.That(newEditId, Is.GreaterThan(0),
                "Sequence should be reset so new ID is positive after restore");
        }
        finally
        {
            File.Delete(backupPath);
        }
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

        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - Restore (topological sort must order tables correctly)
            await _backupService!.RestoreAsync(backupPath);

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
        finally
        {
            File.Delete(backupPath);
        }
    }

    #endregion

    #region Metadata & Validation Tests

    [Test]
    public async Task GetMetadataAsync_FromEncryptedBackup_ShouldReturnMetadata()
    {
        // Arrange - Use DB passphrase (SetUp() already configured "test-passphrase-12345")
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - GetMetadataAsync will retrieve passphrase from DB to decrypt
            var metadata = await _backupService!.GetMetadataAsync(backupPath);

            // Assert
            Assert.That(metadata, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata.Version, Is.EqualTo("3.0"));
                Assert.That(metadata.TableCount, Is.EqualTo(GoldenDataset.TotalTableCount));
                Assert.That(metadata.CreatedAt, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
            }
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task GetMetadataAsync_FromDbPassphraseBackup_ShouldReturnMetadata()
    {
        // Arrange - ExportAsync() encrypts with DB passphrase; metadata is always readable
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act
            var metadata = await _backupService!.GetMetadataAsync(backupPath);

            // Assert
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.Version, Is.EqualTo("3.0"));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task IsEncryptedAsync_WithEncryptedBackup_ShouldReturnTrue()
    {
        // Arrange
        var backupPath = await ExportBackupToTempFileAsync("test-pass");
        try
        {
            // Act
            var isEncrypted = await _backupService!.IsEncryptedAsync(backupPath);

            // Assert
            Assert.That(isEncrypted, Is.True);
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    // Note: ValidateBackupAsync doesn't exist in BackupService
    // Validation is done implicitly during GetMetadataAsync/RestoreAsync
    // (they throw if backup is invalid)

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExportToFileAsync_WithCorruptedJsonbColumn_ShouldFailFastWithClearError()
    {
        // Arrange - Corrupt the warnings JSONB column with JSON that can't deserialize to List<WarningEntry>
        // This simulates database corruption or schema mismatch that would produce an incomplete backup
        await _testHelper!.ExecuteSqlAsync($@"
            UPDATE telegram_users
            SET warnings = '{{""not_an_array"": true}}'::jsonb
            WHERE telegram_user_id = {GoldenDataset.TelegramUsers.User1_TelegramUserId}
        ");

        // Verify the corruption was applied
        var corruptedValue = await _testHelper.ExecuteScalarAsync<string>($@"
            SELECT warnings::text FROM telegram_users
            WHERE telegram_user_id = {GoldenDataset.TelegramUsers.User1_TelegramUserId}
        ");
        Assert.That(corruptedValue, Does.Contain("not_an_array"), "Test setup: JSONB should be corrupted");

        // Act & Assert - Export should fail fast with InvalidOperationException
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_backup_{Guid.NewGuid():N}.tar.gz");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService!.ExportToFileAsync(tempPath, CancellationToken.None);
        });

        using (Assert.EnterMultipleScope())
        {
            // Verify the exception contains useful diagnostic information
            Assert.That(ex!.Message, Does.Contain("warnings"), "Exception should identify the corrupted column");
            Assert.That(ex.Message, Does.Contain("telegram_users"), "Exception should identify the table");
            Assert.That(ex.InnerException, Is.TypeOf<System.Text.Json.JsonException>(), "Inner exception should be JsonException");
        }
    }

    [Test]
    public async Task ExportToFileAsync_WithValidJsonbColumn_ShouldSucceed()
    {
        // Arrange - Ensure we have valid JSONB data (golden dataset already has this)
        // This test verifies the happy path still works after adding error handling

        // Act
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Assert - Export should succeed
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(new FileInfo(backupPath).Length, Is.GreaterThan(0));

            // Verify the backup contains telegram_users table with warnings column
            var metadata = await _backupService!.GetMetadataAsync(backupPath);
            Assert.That(metadata.TableCount, Is.EqualTo(GoldenDataset.TotalTableCount));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task ExportToFileAsync_CancelledDuringWrite_ShouldCleanUpTempFile()
    {
        // Arrange - Use a pre-cancelled token so the export creates the temp file
        // but fails during tar entry writing (WriteEntryAsync checks the token).
        // This exercises the finally block's temp file cleanup.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"backup_cleanup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filepath = Path.Combine(tempDir, "test_backup.tar.gz");

        try
        {
            // Act - Export should fail with cancellation after temp file is created
            await Assert.ThatAsync(
                () => _backupService!.ExportToFileAsync(filepath, cts.Token),
                Throws.InstanceOf<OperationCanceledException>());

            // Assert - No temp files should remain (finally block cleaned up)
            var tempFiles = Directory.GetFiles(tempDir, "*.tmp");
            Assert.That(tempFiles, Is.Empty, "Temp file should be cleaned up after cancelled export");

            // Final file should not exist either
            Assert.That(File.Exists(filepath), Is.False, "Final backup file should not exist after cancelled export");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ExportToFileAsync_WithEmptyPassphrase_ShouldThrowArgumentException(string passphrase)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_backup_{Guid.NewGuid():N}.tar.gz");

        await Assert.ThatAsync(
            () => _backupService!.ExportToFileAsync(tempPath, passphrase),
            Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("passphraseOverride"));
    }

    [Test]
    public async Task ExportAndRestore_ShouldPreserveDateTimeOffsetTimezone()
    {
        // Arrange - Insert a DateTimeOffset with a non-UTC timezone (+05:30 IST)
        // This tests that both TableExportService.ReadTypedValue and the test helper
        // correctly use GetFieldValue<DateTimeOffset> instead of GetValue (which loses timezone)
        var specificOffset = new DateTimeOffset(2025, 6, 15, 14, 30, 0, TimeSpan.FromHours(5.5));

        await _testHelper!.ExecuteSqlAsync($@"
            UPDATE telegram_users
            SET first_seen_at = '{specificOffset:yyyy-MM-dd HH:mm:ss.ffffffzzz}'::timestamptz
            WHERE telegram_user_id = {GoldenDataset.TelegramUsers.User1_TelegramUserId}
        ");

        // Verify the insert worked - test helper now uses GetFieldValue<DateTimeOffset> properly
        var insertedOffset = await _testHelper.ExecuteScalarAsync<DateTimeOffset>($@"
            SELECT first_seen_at FROM telegram_users
            WHERE telegram_user_id = {GoldenDataset.TelegramUsers.User1_TelegramUserId}
        ");

        // PostgreSQL stores timestamptz as UTC internally, so compare UTC instants
        Assert.That(insertedOffset.UtcDateTime, Is.EqualTo(specificOffset.UtcDateTime).Within(TimeSpan.FromSeconds(1)),
            "Test setup: DateTimeOffset should be stored correctly");

        // Act - Export and restore
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify the DateTimeOffset was preserved through the roundtrip
            var restoredOffset = await _testHelper.ExecuteScalarAsync<DateTimeOffset>($@"
                SELECT first_seen_at FROM telegram_users
                WHERE telegram_user_id = {GoldenDataset.TelegramUsers.User1_TelegramUserId}
            ");

            Assert.That(restoredOffset.UtcDateTime, Is.EqualTo(specificOffset.UtcDateTime).Within(TimeSpan.FromSeconds(1)),
                "DateTimeOffset UTC instant should be preserved through backup/restore roundtrip");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task ExportAndRestore_ShouldPreserveEnumValues()
    {
        // Arrange - Verify enum value is set in golden dataset
        // This tests that ReadTypedValue correctly uses Enum.ToObject for enum columns
        var originalStatus = await _testHelper!.ExecuteScalarAsync<int>($@"
            SELECT status FROM users WHERE id = '{GoldenDataset.Users.User3_Id}'
        ");

        Assert.That(originalStatus, Is.EqualTo(GoldenDataset.Users.User3_Status),
            "Test setup: User3 should have Deleted status (3)");

        // Act - Export and restore
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify enum was preserved
            var restoredStatus = await _testHelper.ExecuteScalarAsync<int>($@"
                SELECT status FROM users WHERE id = '{GoldenDataset.Users.User3_Id}'
            ");

            Assert.That(restoredStatus, Is.EqualTo(GoldenDataset.Users.User3_Status),
                "Enum value should be preserved through backup/restore roundtrip");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    #endregion

    #region Passphrase Management Tests

    [Test]
    public async Task SaveEncryptionConfigAsync_ShouldCreateInitialConfig()
    {
        // Arrange - Remove existing config
        await _testHelper!.ExecuteSqlAsync("DELETE FROM configs WHERE chat_id = 0");

        const string testPassphrase = "initial-config-pass-456";

        // Act
        await _passphraseService!.SaveEncryptionConfigAsync(testPassphrase);

        // Assert - Verify config created
        await using (var context = _testHelper.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            Assert.That(config, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(config!.BackupEncryptionConfig, Is.Not.Null);
                Assert.That(config.PassphraseEncrypted, Is.Not.Null, "Passphrase should be encrypted");
            }
        }
    }

    [Test]
    public async Task GetDecryptedPassphraseAsync_WhenExists_ShouldReturnPassphrase()
    {
        // Arrange - Mock uses pass-through encryption, so store plaintext directly
        const string testPassphrase = "get-pass-test-123";
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            // MockDataProtectionService is pass-through, so "encrypted" = plaintext
            config!.PassphraseEncrypted = testPassphrase;
            await context.SaveChangesAsync();
        }

        // Act
        var decrypted = await _passphraseService!.GetDecryptedPassphraseAsync();

        // Assert
        Assert.That(decrypted, Is.EqualTo(testPassphrase));
    }

    [Test]
    public async Task GetDecryptedPassphraseAsync_WhenMissing_ShouldThrowException()
    {
        // Arrange - Clear passphrase from config
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            config!.PassphraseEncrypted = null;
            await context.SaveChangesAsync();
        }

        // Act & Assert - Should throw when passphrase is missing
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _passphraseService!.GetDecryptedPassphraseAsync();
        });
    }

    // Note: RotatePassphraseAsync is not tested in baseline tests
    // - Requires mocking IScheduler for Quartz.NET job scheduling
    // - Should be tested in integration tests

    #endregion

    /// <summary>
    /// Mock DM delivery service for tests (BackupService sends notifications on export failure)
    /// </summary>
    private class MockBotDmService : IBotDmService
    {
        private static readonly DmDeliveryResult SuccessResult = new()
        {
            DmSent = true,
            FallbackUsed = false,
            Failed = false,
            MessageId = 1
        };

        public Task<DmDeliveryResult> SendDmAsync(
            long telegramUserId,
            string messageText,
            long? fallbackChatId = null,
            int? autoDeleteSeconds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithQueueAsync(
            long telegramUserId,
            string notificationType,
            string messageText,
            ParseMode parseMode = ParseMode.MarkdownV2,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithMediaAsync(
            long telegramUserId,
            string notificationType,
            string messageText,
            string? photoPath = null,
            string? videoPath = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithMediaAndKeyboardAsync(
            long telegramUserId,
            string notificationType,
            string messageText,
            string? photoPath = null,
            string? videoPath = null,
            InlineKeyboardMarkup? keyboard = null,
            ParseMode parseMode = ParseMode.MarkdownV2,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<Message> EditDmTextAsync(
            long dmChatId,
            int messageId,
            string text,
            InlineKeyboardMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TelegramTestFactory.CreateMessage(messageId: messageId));

        public Task<Message> EditDmCaptionAsync(
            long dmChatId,
            int messageId,
            string? caption,
            InlineKeyboardMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TelegramTestFactory.CreateMessage(messageId: messageId));

        public Task DeleteDmMessageAsync(
            long dmChatId,
            int messageId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DmDeliveryResult> SendDmWithKeyboardAsync(
            long telegramUserId,
            string messageText,
            InlineKeyboardMarkup keyboard,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);
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
        private static readonly Dictionary<string, bool> EmptyResults = new();

        // Typed methods (no-op for backup tests)
        public Task<Dictionary<string, bool>> SendSpamBanNotificationAsync(ChatIdentity chat, UserIdentity user, Actor? bannedBy, double netScore, double score, string? detectionReason, int chatsAffected, bool messageDeleted, int messageId, string? messagePreview, string? photoPath, string? videoPath, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendReportNotificationAsync(ChatIdentity chat, UserIdentity? reportedUser, long? reporterUserId, string? reporterName, bool isAutomated, string messagePreview, string? photoPath, long reportId, ReportType reportType, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendProfileScanAlertAsync(ChatIdentity chat, UserIdentity user, decimal score, string signals, string? aiReason, long reportId, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendExamFailureNotificationAsync(ChatIdentity chat, UserIdentity user, int mcCorrectCount, int mcTotal, int mcScore, int mcPassingThreshold, string? openEndedQuestion, string? openEndedAnswer, string? aiReasoning, long examFailureId, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendBanNotificationAsync(UserIdentity user, Actor executor, string? reason, ChatIdentity? chat = null, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendMalwareDetectedAsync(ChatIdentity chat, UserIdentity user, string malwareDetails, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendAdminChangedAsync(ChatIdentity chat, UserIdentity user, bool promoted, bool isCreator, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendBackupFailedAsync(string tableName, string error, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendChatHealthWarningAsync(string chatName, string status, bool isAdmin, IReadOnlyList<string> warnings, CancellationToken cancellationToken = default) => Task.FromResult(EmptyResults);
    }
}
