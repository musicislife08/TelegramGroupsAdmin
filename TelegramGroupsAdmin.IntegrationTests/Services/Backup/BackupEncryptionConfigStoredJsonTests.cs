using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Backup;

/// <summary>
/// A backup encryption config stored before <c>Algorithm</c> and <c>Iterations</c> were removed
/// from the model must keep loading with no migration, must shed those keys the next time the
/// passphrase is updated, and must never get them back from a fresh setup. Anchor: the canonical
/// global config row (<see cref="GoldenDatasetConstants.SystemConfig.GlobalChatId"/>), whose
/// <c>backup_encryption_config</c> JSON deliberately keeps both keys.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BackupEncryptionConfigStoredJsonTests
{
    private const long GlobalChatId = GoldenDatasetConstants.SystemConfig.GlobalChatId;

    private MigrationTestHelper? _helper;
    private NpgsqlDataSource? _dataSource;
    private BackupConfigurationService _configService = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        _dataSource = NpgsqlDataSource.Create(_helper.ConnectionString);
        _configService = new BackupConfigurationService(_dataSource, NullLogger<BackupConfigurationService>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        // Null-safe so a failed SetUp still drops the clone and reports its own error
        if (_dataSource != null)
            await _dataSource.DisposeAsync();
        _helper?.Dispose();
    }

    [Test]
    public async Task GetEncryptionConfigAsync_StoredJsonWithLegacyKeys_Loads()
    {
        var storedCreatedAt = await GuardLegacyRowAndReadCreatedAtAsync();

        var config = await _configService.GetEncryptionConfigAsync();

        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config!.Enabled, Is.True);
            // Proves the stored row was read, not a default-constructed config.
            Assert.That(config.CreatedAt, Is.EqualTo(storedCreatedAt));
        }
    }

    [Test]
    public async Task UpdateEncryptionConfigAsync_StoredJsonWithLegacyKeys_DropsThemAndKeepsTheRest()
    {
        var storedCreatedAt = await GuardLegacyRowAndReadCreatedAtAsync();

        // Act - the update path deserializes the stored config and writes it back
        await CreatePassphraseService().UpdateEncryptionConfigAsync("rotated-test-passphrase");

        var config = await _configService.GetEncryptionConfigAsync();
        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CountRowsWithLegacyKeyAsync(), Is.Zero, "the update must not write the legacy keys back");
            Assert.That(config!.Enabled, Is.True);
            Assert.That(config.CreatedAt, Is.EqualTo(storedCreatedAt));
            Assert.That(config.LastRotatedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task SaveEncryptionConfigAsync_OverStoredJsonWithLegacyKeys_WritesConfigWithoutThem()
    {
        var storedCreatedAt = await GuardLegacyRowAndReadCreatedAtAsync();

        // Act - first-time setup builds a brand-new config and replaces the stored one
        await CreatePassphraseService().SaveEncryptionConfigAsync("initial-test-passphrase");

        var config = await _configService.GetEncryptionConfigAsync();
        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CountRowsWithLegacyKeyAsync(), Is.Zero, "a new config must not carry the legacy keys");
            Assert.That(config!.Enabled, Is.True);
            // A new CreatedAt proves the stored config was replaced, not left in place.
            Assert.That(config.CreatedAt, Is.GreaterThan(storedCreatedAt));
            Assert.That(config.LastRotatedAt, Is.Null);
        }
    }

    /// <summary>
    /// Guards the precondition (the canonical row still carries both removed keys) and returns the
    /// row's stored <c>CreatedAt</c>, read at runtime so the tests survive canonical edits.
    /// </summary>
    private async Task<DateTimeOffset> GuardLegacyRowAndReadCreatedAtAsync()
    {
        var rowsWithBothKeys = await _helper!.ExecuteScalarAsync<long>(
            $"""
            SELECT count(*) FROM configs
            WHERE chat_id = {GlobalChatId}
              AND backup_encryption_config ? 'Algorithm'
              AND backup_encryption_config ? 'Iterations'
            """);
        Assert.That(rowsWithBothKeys, Is.EqualTo(1),
            "canonical global config must carry the legacy Algorithm and Iterations keys");

        var storedCreatedAt = await _helper.ExecuteScalarAsync<string>(
            $"SELECT backup_encryption_config ->> 'CreatedAt' FROM configs WHERE chat_id = {GlobalChatId}");
        return DateTimeOffset.Parse(storedCreatedAt!);
    }

    private Task<long> CountRowsWithLegacyKeyAsync() =>
        _helper!.ExecuteScalarAsync<long>(
            $"""
            SELECT count(*) FROM configs
            WHERE chat_id = {GlobalChatId}
              AND (backup_encryption_config ? 'Algorithm' OR backup_encryption_config ? 'Iterations')
            """);

    private PassphraseManagementService CreatePassphraseService()
    {
        var protection = Substitute.For<IDataProtectionService>();
        protection.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");

        return new PassphraseManagementService(
            _dataSource!,
            protection,
            _configService,
            Substitute.For<IJobScheduler>(),
            NullLogger<PassphraseManagementService>.Instance);
    }
}
