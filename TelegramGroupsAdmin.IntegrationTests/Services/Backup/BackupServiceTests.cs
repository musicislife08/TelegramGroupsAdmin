using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
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
/// Tests use the canonical golden dataset (cloned per-test via PG TEMPLATE) to ensure
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

    // Canonical anchor IDs (canonical telegram user IDs are in [9_000_000_000_000, 10_000_000_000_000))
    // Top MainChat ham author (@unhelpfulgrab, "Squeak Degree", is_banned=false, 24 messages)
    private const long CanonicalTopHamAuthorId = 9921676191756L;

    // Canonical message with edit history in MainChat (has a message_edits row)
    private const int CanonicalMessageWithEditsId = 212340;

    // Canonical MainChat chat_id (15-digit synthetic, -100 prefix)
    private const long CanonicalMainChatId = -100026957614982L;

    // Tables with DTOs that BackupService can export (excludes __EFMigrationsHistory,
    // file_scan_quota, ticker.*). Updated 2026-04-09: +file_scan_results (FileScanResultDto rename).
    // Updated 2026-05-27: +username_blacklist (UsernameBlacklistEntryDto now discovered via [Table] attribute).
    // Updated 2026-09-25: +ban_celebration_subscribers (DM ban celebrations).
    // Updated 2026-09-27: -3 legacy label/media-sample tables (DropLegacyVerdictColumns).
    private const int ExpectedBackupTableCount = 41;

    // Canonical synthetic username_blacklist row ('archived_pattern', disabled) and the key
    // RestoreAsync_ShouldWipeAllTablesFirst moves it to after taking the backup.
    private const long CanonicalBlacklistEntryId = 999005;
    private const long MovedBlacklistEntryId = 999905;

    [SetUp]
    public async Task SetUp()
    {
        // Clone golden_template — full canonical dataset, ~50-150ms vs ~250-550ms migrations
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        // Use the session-shared DataProtection provider so encrypted-column ciphertext
        // written into golden_template (by LoadCanonicalAsync) can be decrypted here.
        _dataProtectionProvider = PostgresFixture.SharedDataProtectionProvider;
        _serviceProvider = BuildBackupServiceProvider(_testHelper.ConnectionString, _dataProtectionProvider);
        _encryptionService = _serviceProvider.GetRequiredService<IBackupEncryptionService>();

        // Create BackupService and PassphraseManagementService in a new scope (using interface)
        var scope = _serviceProvider.CreateScope();
        _backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
        _passphraseService = scope.ServiceProvider.GetRequiredService<IPassphraseManagementService>();

        // Set up default encryption config for all tests
        await _passphraseService.SaveEncryptionConfigAsync("test-passphrase-12345");
    }

    /// <summary>
    /// Builds a fully-wired BackupService DI container for the given connection string and
    /// Data Protection key ring. Extracted so a test can stand up a *second* stack with a
    /// different key ring (simulating a cross-machine restore onto a different key ring).
    /// </summary>
    private static ServiceProvider BuildBackupServiceProvider(string connectionString, IDataProtectionProvider dataProtectionProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dataProtectionProvider);

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory (required by TelegramUserRepository / TelegramSessionRepository)
        services.AddDbContextFactory<Data.AppDbContext>((_, options) =>
        {
            options.UseNpgsql(connectionString);
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
        services.AddSingleton<IAdminNotificationService, MockNotificationService>();
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
        services.AddSingleton<BackupFileLock>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IBackupEncryptionService, BackupEncryptionService>();
        services.AddScoped<IBackupArchiveRotator, BackupArchiveRotator>();
        services.AddScoped<IBackupConfigurationService, BackupConfigurationService>();
        services.AddScoped<IPassphraseManagementService, PassphraseManagementService>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();

        // Add internal handler services (required by BackupService)
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.TableDiscoveryService>();
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.TableExportService>();
        services.AddScoped<TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.DependencyResolutionService>();

        // Add repositories used as write-SUTs in arrange steps
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<ITelegramSessionRepository, TelegramSessionRepository>();

        return services.BuildServiceProvider();
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
                Assert.That(metadata.Version, Is.EqualTo("3.1"));
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
            Assert.That(metadata.TableCount, Is.EqualTo(ExpectedBackupTableCount),
                $"Expected {ExpectedBackupTableCount} tables in backup (excluding system tables)");

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
        // Arrange - Canonical golden_template has api_keys populated (non-NULL) under SharedDataProtectionProvider.
        // Verify canonical seed worked.
        await using (var context = _testHelper!.GetDbContext())
        {
            var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
            Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be encrypted in database");
        }

        // Act - Export (should decrypt Data Protection fields using SharedDataProtectionProvider)
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
                AND table_name NOT IN ('__EFMigrationsHistory', 'cached_blocked_domains', 'file_scan_quota')
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
        // Arrange - Create backup, then diverge from it
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Move a canonical row to a key the backup does not contain (an in-place edit of the
            // synthetic username_blacklist row 999005, which nothing references). A restore that
            // merged instead of wiping would keep the moved row next to the restored one.
            await _testHelper!.ExecuteSqlAsync(
                $"UPDATE username_blacklist SET id = {MovedBlacklistEntryId} WHERE id = {CanonicalBlacklistEntryId}");
            var movedBeforeRestore = await _testHelper.ExecuteScalarAsync<bool>(
                $"SELECT EXISTS(SELECT 1 FROM username_blacklist WHERE id = {MovedBlacklistEntryId})");
            Assert.That(movedBeforeRestore, Is.True, "Precondition: the row was moved after the backup");

            // Act - Restore
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Only the backup's row remains
            var movedAfterRestore = await _testHelper.ExecuteScalarAsync<bool>(
                $"SELECT EXISTS(SELECT 1 FROM username_blacklist WHERE id = {MovedBlacklistEntryId})");
            var originalAfterRestore = await _testHelper.ExecuteScalarAsync<bool>(
                $"SELECT EXISTS(SELECT 1 FROM username_blacklist WHERE id = {CanonicalBlacklistEntryId})");
            Assert.That(movedAfterRestore, Is.False, "Restore should wipe all existing data first");
            Assert.That(originalAfterRestore, Is.True, "Restore should bring back the backup's row");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_ShouldHandleSelfReferencingForeignKeys()
    {
        // Arrange - Canonical dataset has users.invited_by → users.id (self-reference).
        // Admin (User2_Id = 921637d5) is invited by Owner (User1_Id = b388ee38) in canonical.
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            // Act - Restore (should handle self-referencing FK)
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify self-referencing FK relationships preserved
            var user2 = await _testHelper!.ExecuteScalarAsync<string>($@"
                SELECT invited_by
                FROM users
                WHERE id = '{GoldenDatasetConstants.WebUsers.AdminId}'
            ");

            Assert.That(user2, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerId),
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
            // Act - Restore (should re-encrypt using SharedDataProtectionProvider)
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify API keys are re-encrypted
            await using (var context = _testHelper!.GetDbContext())
            {
                var config = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0);
                Assert.That(config?.ApiKeys, Is.Not.Null, "API keys should be re-encrypted after restore");

                // Verify can decrypt with SharedDataProtectionProvider
                var protector = _dataProtectionProvider!.CreateProtector(DataProtectionPurposes.ApiKeys);
                var decrypted = protector.Unprotect(config!.ApiKeys!);
                Assert.That(decrypted, Contains.Substring("openai"),
                    "Decrypted API keys should contain original canonical test data");
            }
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    [Test]
    public async Task RestoreAsync_OntoDifferentKeyRing_ShouldReEncryptTelegramSessionFields()
    {
        // Reproduces #517: telegram_sessions.session_data (byte[]) and phone_number (string) are
        // encrypted out-of-band by TelegramSessionRepository under the TelegramSession purpose.
        // Without [ProtectedData] on the DTO (and byte[] support in the backup handlers), export
        // ships the SOURCE key-ring ciphertext verbatim and restore inserts it verbatim, so a
        // restore onto a DIFFERENT key ring leaves the row undecryptable → CryptographicException.

        // Arrange - seed an active session encrypted under the SOURCE key ring (Shared provider).
        var sourceSessionRepo = _serviceProvider!.GetRequiredService<ITelegramSessionRepository>();
        var originalSessionData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03 };
        const string originalPhoneNumber = "+15555550199";
        var webUserId = GoldenDatasetConstants.WebUsers.OwnerId;

        await sourceSessionRepo.CreateSessionAsync(new TelegramSession
        {
            WebUserId = webUserId,
            TelegramUserId = 9921676191756L,
            DisplayName = "Cross-Machine Restore Test",
            PhoneNumber = originalPhoneNumber,
            SessionData = originalSessionData,
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        // Export under the source key ring. Explicit passphrase so the envelope crypto doesn't
        // depend on the DB passphrase (which is itself Data-Protection key-ring-bound).
        const string passphrase = "cross-machine-pass-123";
        var backupPath = await ExportBackupToTempFileAsync(passphrase);

        // Build a SECOND stack on a DIFFERENT key ring — the "target machine".
        var targetKeyRing = new EphemeralDataProtectionProvider();
        await using var targetProvider = BuildBackupServiceProvider(_testHelper!.ConnectionString, targetKeyRing);
        var targetBackupService = targetProvider.GetRequiredService<IBackupService>();
        var targetSessionRepo = targetProvider.GetRequiredService<ITelegramSessionRepository>();

        try
        {
            // Act - restore onto the target key ring, then read the session back under that ring.
            await targetBackupService.RestoreAsync(backupPath, passphrase);
            var restored = await targetSessionRepo.GetActiveSessionAsync(webUserId, CancellationToken.None);

            // Assert - both encrypted columns decrypt cleanly under the target key ring.
            Assert.That(restored, Is.Not.Null, "Active session should survive the restore");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored!.SessionData, Is.EqualTo(originalSessionData),
                    "byte[] session_data should be re-keyed to the target ring and decrypt to the original bytes");
                Assert.That(restored.PhoneNumber, Is.EqualTo(originalPhoneNumber),
                    "string phone_number should be re-keyed to the target ring and decrypt to the original value");
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

            // Assert - Insert new message_edit, verify ID continues from max.
            // Uses canonical message 212340 in MainChat (-100026957614982) which has an existing
            // message_edits row (id 337) — confirms the sequence was reset to max(id) post-restore.
            // (message_edits.id still has identity/sequence, unlike messages.message_id which uses ValueGeneratedNever)
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO message_edits (message_id, chat_id, edit_date, new_text)
                VALUES ({CanonicalMessageWithEditsId}, {CanonicalMainChatId}, NOW(), 'sequence test edit')
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
        // Arrange - Canonical dataset has complex FK relationships:
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
                Assert.That(metadata.Version, Is.EqualTo("3.1"));
                Assert.That(metadata.TableCount, Is.EqualTo(ExpectedBackupTableCount));
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
            Assert.That(metadata.Version, Is.EqualTo("3.1"));
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
        // This simulates database corruption or schema mismatch that would produce an incomplete backup.
        // Uses canonical top-ham-author (CanonicalTopHamAuthorId = 9921676191756).
        await _testHelper!.ExecuteSqlAsync($@"
            UPDATE telegram_users
            SET warnings = '{{""not_an_array"": true}}'::jsonb
            WHERE telegram_user_id = {CanonicalTopHamAuthorId}
        ");

        // Verify the corruption was applied
        var corruptedValue = await _testHelper.ExecuteScalarAsync<string>($@"
            SELECT warnings::text FROM telegram_users
            WHERE telegram_user_id = {CanonicalTopHamAuthorId}
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
        // Arrange - Ensure we have valid JSONB data (canonical dataset already has this)
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
            Assert.That(metadata.TableCount, Is.EqualTo(ExpectedBackupTableCount));
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
        // Arrange - Update a DateTimeOffset with a non-UTC timezone (+05:30 IST) on a canonical
        // telegram_user row. Uses CanonicalTopHamAuthorId (9921676191756) which exists in canonical.
        var specificOffset = new DateTimeOffset(2025, 6, 15, 14, 30, 0, TimeSpan.FromHours(5.5));

        await _testHelper!.ExecuteSqlAsync($@"
            UPDATE telegram_users
            SET first_seen_at = '{specificOffset:yyyy-MM-dd HH:mm:ss.ffffffzzz}'::timestamptz
            WHERE telegram_user_id = {CanonicalTopHamAuthorId}
        ");

        // Verify the update worked - test helper uses GetFieldValue<DateTimeOffset> properly
        var insertedOffset = await _testHelper.ExecuteScalarAsync<DateTimeOffset>($@"
            SELECT first_seen_at FROM telegram_users
            WHERE telegram_user_id = {CanonicalTopHamAuthorId}
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
                WHERE telegram_user_id = {CanonicalTopHamAuthorId}
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
        // Arrange - Verify enum value is set in canonical dataset.
        // DeletedAdminId (a8dc8371) = deleted@example.com, status=3 (Deleted) — canonical fixture.
        var originalStatus = await _testHelper!.ExecuteScalarAsync<int>($@"
            SELECT status FROM users WHERE id = '{GoldenDatasetConstants.WebUsers.DeletedAdminId}'
        ");

        Assert.That(originalStatus, Is.EqualTo(GoldenDatasetConstants.WebUsers.DeletedAdminStatus),
            "Test setup: Deleted Admin fixture should have Deleted status (3)");

        // Act - Export and restore
        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            await _backupService!.RestoreAsync(backupPath);

            // Assert - Verify enum was preserved
            var restoredStatus = await _testHelper.ExecuteScalarAsync<int>($@"
                SELECT status FROM users WHERE id = '{GoldenDatasetConstants.WebUsers.DeletedAdminId}'
            ");

            Assert.That(restoredStatus, Is.EqualTo(GoldenDatasetConstants.WebUsers.DeletedAdminStatus),
                "Enum value should be preserved through backup/restore roundtrip");
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    /// <summary>
    /// messages.media_features is a polymorphic jsonb contract: jsonb moves "type" after "hash" on read,
    /// and the restore insert must keep the "type" discriminator, or the restored row cannot be read.
    /// Anchor: <see cref="GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId"/> (canonical edit 2026-09-27).
    /// </summary>
    [Test]
    public async Task ExportAndRestore_ShouldPreserveMediaFeatures()
    {
        byte[] originalHash;
        await using (var context = _testHelper!.GetDbContext())
        {
            // guard the canonical edit
            var original = await context.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId
                && m.ChatId == GoldenDatasetConstants.Chats.MainChatId);
            Assert.That(original.MediaFeatures, Is.TypeOf<Data.Models.PhotoFeaturesDto>());
            originalHash = ((Data.Models.PhotoFeaturesDto)original.MediaFeatures!).Hash;
        }

        var backupPath = await ExportBackupToTempFileAsync();
        try
        {
            await _backupService!.RestoreAsync(backupPath);

            await using var context = _testHelper.GetDbContext();
            var restored = await context.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId
                && m.ChatId == GoldenDatasetConstants.Chats.MainChatId);
            Assert.That(restored.MediaFeatures, Is.TypeOf<Data.Models.PhotoFeaturesDto>()
                .With.Property(nameof(Data.Models.PhotoFeaturesDto.Hash)).EqualTo(originalHash));
        }
        finally
        {
            File.Delete(backupPath);
        }
    }

    #endregion

    #region Backup Format Migration Tests

    /// <summary>
    /// A 3.0 backup (legacy detection_results columns, training_labels, media sample tables) restores
    /// into the 3.1 schema: rows get source/classification, is_spam is generated (never written),
    /// labels fold into decisions and the identity sequence clears the new ids.
    /// The 3.0 backup is built from the exported canonical backup: its detection_results are replaced by
    /// synthetic 3.0-shaped rows on canonical messages (backup fixtures are infrastructure data).
    /// </summary>
    [Test]
    public async Task RestoreAsync_Version30Backup_MigratesVerdictsIntoTheCurrentSchema()
    {
        var exportedPath = await ExportBackupToTempFileAsync();
        var legacyPath = Path.Combine(Path.GetTempPath(), $"test_backup_v30_{Guid.NewGuid():N}.tar.gz");
        try
        {
            var messages = await WriteVersion30BackupAsync(exportedPath, legacyPath);

            await _backupService!.RestoreAsync(legacyPath);

            await using var context = _testHelper!.GetDbContext();
            var rows = await context.DetectionResults.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
            var sequenceValue = await _testHelper.ExecuteScalarAsync<long>(
                "SELECT last_value FROM pg_sequences WHERE sequencename = pg_get_serial_sequence('detection_results', 'id')::regclass::text");

            DetectionResultRow Only(int index) => rows.Where(r => r.MessageId == messages[index].MessageId && r.ChatId == messages[index].ChatId)
                .Select(r => new DetectionResultRow(r.Source, r.Classification, r.IsSpam, r.SystemIdentifier, r.TelegramUserId)).Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(rows, Has.Count.EqualTo(6), "3 legacy rows + 1 TrainingExclude + 2 folded labels (the matched ham label is not duplicated)");
                Assert.That(Only(0), Is.EqualTo(new DetectionResultRow(0, 2, true, "auto_detection", null)), "trained content scan above threshold → ImplicitSpam");
                Assert.That(Only(1), Is.EqualTo(new DetectionResultRow(12, 1, false, null, CanonicalTopHamAuthorId)),
                    "WebMarkHam; the legacy is_spam = true is ignored, is_spam is generated from classification");
                Assert.That(rows.Where(r => r.MessageId == messages[2].MessageId && r.ChatId == messages[2].ChatId)
                        .Select(r => new DetectionResultRow(r.Source, r.Classification, r.IsSpam, r.SystemIdentifier, r.TelegramUserId)),
                    Is.EquivalentTo(new[]
                    {
                        new DetectionResultRow(18, 0, true, "tg-spam-import", null),
                        new DetectionResultRow(17, 4, true, "tg-spam-import", null)
                    }), "unused import keeps its actor on the TrainingExclude backfill");
                Assert.That(Only(3), Is.EqualTo(new DetectionResultRow(99, 1, false, "unknown", null)), "unattributed ham label satisfies the actor CHECK");
                Assert.That(Only(4), Is.EqualTo(new DetectionResultRow(10, 0, true, "auto_detection", null)), "unattributed spam label → AutoBan");
                Assert.That(sequenceValue, Is.GreaterThanOrEqualTo(rows.Max(r => r.Id)), "identity sequence reset past the migrated ids");
            }
        }
        finally
        {
            File.Delete(exportedPath);
            File.Delete(legacyPath);
        }
    }

    private sealed record DetectionResultRow(int Source, int Classification, bool IsSpam, string? SystemIdentifier, long? TelegramUserId);

    /// <summary>
    /// Rewrites an exported (current-format, encrypted) backup as a 3.0 backup with an unencrypted database.json:
    /// version 3.0, legacy detection_results rows on the first five canonical MainChat messages, training_labels
    /// and the retired media sample tables. Returns the five messages used.
    /// </summary>
    private async Task<List<(int MessageId, long ChatId)>> WriteVersion30BackupAsync(string exportedPath, string legacyPath)
    {
        JsonObject? metadata = null;
        JsonObject? data = null;
        await using (var input = File.OpenRead(exportedPath))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            using var tar = new TarReader(gzip);
            while (await tar.GetNextEntryAsync() is { DataStream: not null } entry)
            {
                using var buffer = new MemoryStream();
                await entry.DataStream.CopyToAsync(buffer);
                if (entry.Name == "metadata.json")
                    metadata = JsonNode.Parse(buffer.ToArray())!.AsObject();
                else if (entry.Name == "database.json.enc")
                    data = JsonNode.Parse(_encryptionService!.DecryptBackup(buffer.ToArray(), "test-passphrase-12345"))!.AsObject();
            }
        }

        Assert.That(metadata, Is.Not.Null);
        Assert.That(data, Is.Not.Null);

        // MainChat messages: chat 0 would turn a manual row into a TrainingDataPage row.
        var messages = data!["messages"]!.AsArray()
            .Select(m => (MessageId: m!["message_id"]!.GetValue<int>(), ChatId: m["chat_id"]!.GetValue<long>()))
            .Where(m => m.ChatId == CanonicalMainChatId)
            .Take(5)
            .ToList();
        Assert.That(messages, Has.Count.EqualTo(5));

        JsonObject Legacy(long id, int index, string detectionSource, bool isSpam, double netScore, bool usedForTraining,
            string reason, long? telegramUserId, string? systemIdentifier, string? checks) => new()
        {
            ["id"] = id,
            ["message_id"] = messages[index].MessageId,
            ["chat_id"] = messages[index].ChatId,
            ["detected_at"] = "2026-01-10T12:00:00.5+00:00",
            ["detection_source"] = detectionSource,
            ["detection_method"] = "Legacy",
            ["is_spam"] = isSpam,
            ["score"] = Math.Abs(netScore),
            ["net_score"] = netScore,
            ["used_for_training"] = usedForTraining,
            ["reason"] = reason,
            ["web_user_id"] = null,
            ["telegram_user_id"] = telegramUserId,
            ["system_identifier"] = systemIdentifier,
            ["check_results_json"] = checks,
            ["edit_version"] = 0
        };

        JsonObject Label(int index, int label, long? labeledBy) => new()
        {
            ["message_id"] = messages[index].MessageId,
            ["chat_id"] = messages[index].ChatId,
            ["label"] = label,
            ["labeled_by_user_id"] = labeledBy,
            ["labeled_at"] = "2026-01-11T00:00:00+00:00",
            ["reason"] = null,
            ["audit_log_id"] = null
        };

        data["detection_results"] = new JsonArray(
            Legacy(1001, 0, "auto", true, 3.0, true, "Spam detected", null, "auto_detection",
                """{"Checks": [{"Score": 3.0, "CheckName": 2, "Abstained": false}]}"""),
            Legacy(1002, 1, "manual", true, -5.0, true, "Manually marked as ham (not spam) by admin", CanonicalTopHamAuthorId, null, null),
            Legacy(1003, 2, "tg-spam-import", true, -1.0, false, "Imported (label - spam)", null, "tg-spam-import", null));
        data["training_labels"] = new JsonArray(
            Label(1, 1, CanonicalTopHamAuthorId),
            Label(3, 1, null),
            Label(4, 0, null));
        data["image_training_samples"] = new JsonArray();
        data["video_training_samples"] = new JsonArray();

        var tables = metadata!["tables"]!.AsArray();
        foreach (var legacyTable in new[] { "training_labels", "image_training_samples", "video_training_samples" })
            tables.Add(legacyTable);
        metadata["table_count"] = tables.Count;
        metadata["version"] = "3.0";

        await using (var output = File.Create(legacyPath))
        await using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        await using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "metadata.json")
            {
                DataStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(metadata))
            });
            await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "database.json")
            {
                DataStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(data))
            });
        }

        return messages;
    }

    #endregion

    #region Passphrase Rotation Job Tests

    /// <summary>
    /// A backup made before a passphrase rotation must still restore after it, with the new
    /// passphrase the rotation stored. The file must also still be a readable archive: restore,
    /// metadata and the encrypted check all open it as gzip.
    /// </summary>
    [Test]
    public async Task RotatePassphraseJob_ThenRestore_BackupMadeBeforeRotationStillRestores()
    {
        // Arrange - one backup, encrypted with the passphrase configured in SetUp, in its own directory
        const string newPassphrase = "rotated-passphrase-67890";
        var backupDirectory = Directory.CreateTempSubdirectory("tga_rotation_proof_").FullName;
        var backupPath = Path.Combine(backupDirectory, "backup_before_rotation.tar.gz");
        try
        {
            await _backupService!.ExportToFileAsync(backupPath, CancellationToken.None);
            Assert.That(await _backupService.IsEncryptedAsync(backupPath), Is.True, "precondition: the backup is encrypted");
            Assert.That(await ReadFirstBytesAsync(backupPath, 2), Is.EqualTo(GzipMagic), "precondition: the backup is a gzip archive");

            var dataProtection = _serviceProvider!.GetRequiredService<IDataProtectionService>();
            var job = new TelegramGroupsAdmin.BackgroundJobs.Jobs.RotateBackupPassphraseJob(
                _serviceProvider!.GetRequiredService<IBackupArchiveRotator>(),
                _passphraseService!,
                dataProtection,
                _serviceProvider!.GetRequiredService<BackupFileLock>(),
                _serviceProvider!.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramGroupsAdmin.BackgroundJobs.Jobs.RotateBackupPassphraseJob>.Instance,
                new TelegramGroupsAdmin.BackgroundJobs.Metrics.JobMetrics());

            var payload = new TelegramGroupsAdmin.Core.JobPayloads.RotateBackupPassphrasePayload(
                dataProtection.Protect(newPassphrase), backupDirectory, GoldenDatasetConstants.WebUsers.OwnerId);
            var jobDataMap = new Quartz.JobDataMap
            {
                { TelegramGroupsAdmin.Core.BackgroundJobs.JobDataKeys.PayloadJson, JsonSerializer.Serialize(payload) }
            };
            var context = Substitute.For<Quartz.IJobExecutionContext>();
            context.MergedJobDataMap.Returns(jobDataMap);
            context.CancellationToken.Returns(CancellationToken.None);

            // Act - rotate
            await job.Execute(context);

            // Assert - the stored passphrase is the new one, and the pre-rotation backup still works with it
            Assert.That(await _passphraseService!.GetDecryptedPassphraseAsync(), Is.EqualTo(newPassphrase),
                "the rotation should have stored the new passphrase");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(await ReadFirstBytesAsync(backupPath, 2), Is.EqualTo(GzipMagic),
                    "after rotation the backup must still be a gzip archive");
                Assert.That(async () => await _backupService.GetMetadataAsync(backupPath), Throws.Nothing,
                    "metadata must still be readable after rotation");
                Assert.That(async () => await _backupService.RestoreAsync(backupPath), Throws.Nothing,
                    "the backup must restore with the rotated passphrase stored in the database");
            }
        }
        finally
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }

    private static readonly byte[] GzipMagic = [0x1f, 0x8b];

    private static async Task<byte[]> ReadFirstBytesAsync(string path, int count)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer);
        return buffer;
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
            UserIdentity user,
            string messageText,
            long? fallbackChatId = null,
            int? autoDeleteSeconds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<Message> EditDmTextAsync(
            long dmChatId,
            int messageId,
            string text,
            InlineKeyboardMarkup? replyMarkup = null,
            IReadOnlyList<MessageEntity>? entities = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TelegramTestFactory.CreateMessage(messageId: messageId));

        public Task<Message> EditDmCaptionAsync(
            long dmChatId,
            int messageId,
            string? caption,
            InlineKeyboardMarkup? replyMarkup = null,
            IReadOnlyList<MessageEntity>? captionEntities = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TelegramTestFactory.CreateMessage(messageId: messageId));

        public Task DeleteDmMessageAsync(
            long dmChatId,
            int messageId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DmDeliveryResult> SendDmWithKeyboardAsync(
            UserIdentity user,
            string messageText,
            InlineKeyboardMarkup keyboard,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithEntitiesAsync(
            UserIdentity user,
            string notificationType,
            string text,
            IReadOnlyList<MessageEntity> entities,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmAsync(
            UserIdentity user,
            TelegramMessage message,
            long? fallbackChatId = null,
            int? autoDeleteSeconds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithKeyboardAsync(
            UserIdentity user,
            TelegramMessage message,
            InlineKeyboardMarkup keyboard,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithMediaAndKeyboardEntitiesAsync(
            UserIdentity user,
            string notificationType,
            string text,
            IReadOnlyList<MessageEntity> entities,
            string? photoPath = null,
            string? videoPath = null,
            InlineKeyboardMarkup? keyboard = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessResult);

        public Task<DmDeliveryResult> SendDmWithAnimationEntitiesAsync(
            UserIdentity user,
            TelegramMessage caption,
            string? fileId,
            string? filePath,
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
    private class MockNotificationService : IAdminNotificationService
    {
        private static readonly Dictionary<string, bool> EmptyResults = new();

        // Typed methods (no-op for backup tests)
        public Task<Dictionary<string, bool>> SendSpamBanNotificationAsync(ChatIdentity chat, UserIdentity user, Actor? bannedBy, double netScore, double score, string? detectionReason, int chatsAffected, bool messageDeleted, int messageId, string? messagePreview, string? photoPath, string? videoPath, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendReportNotificationAsync(ChatIdentity chat, UserIdentity reportedUser, Actor reporter, string messagePreview, string? photoPath, long reportId, ReportType reportType, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendProfileScanAlertAsync(ChatIdentity chat, UserIdentity user, decimal score, string signals, string? aiReason, long reportId, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendExamFailureNotificationAsync(ChatIdentity chat, UserIdentity user, int mcCorrectCount, int mcTotal, int mcScore, int mcPassingThreshold, string? openEndedQuestion, string? openEndedAnswer, string? aiReasoning, long examResultId, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendExamPassNotificationAsync(ChatIdentity chat, UserIdentity user, int mcCorrectCount, int mcTotal, int mcScore, int mcPassingThreshold, string? openEndedQuestion, string? openEndedAnswer, string? aiReasoning, long examResultId, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendBanNotificationAsync(UserIdentity user, Actor executor, string? reason, ChatIdentity? chat = null, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendMalwareDetectedAsync(ChatIdentity chat, UserIdentity user, string malwareDetails, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendAdminChangedAsync(ChatIdentity chat, UserIdentity user, bool promoted, bool isCreator, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendBackupFailedAsync(string tableName, string error, CancellationToken ct = default) => Task.FromResult(EmptyResults);
        public Task<Dictionary<string, bool>> SendChatHealthWarningAsync(string chatName, string status, bool isAdmin, IReadOnlyList<string> warnings, CancellationToken cancellationToken = default) => Task.FromResult(EmptyResults);
    }
}
