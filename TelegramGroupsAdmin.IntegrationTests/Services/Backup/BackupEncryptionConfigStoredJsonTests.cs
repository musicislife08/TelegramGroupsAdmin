using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Backup;

/// <summary>
/// A backup encryption config stored before <c>Algorithm</c> and <c>Iterations</c> were removed
/// from the model must keep loading with no migration, and must shed those keys the next time
/// the passphrase is rotated. Anchor: the canonical global config row (<c>configs</c>,
/// <c>chat_id = 0</c>), whose <c>backup_encryption_config</c> JSON deliberately keeps both keys.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BackupEncryptionConfigStoredJsonTests
{
    private const string LegacyKeyCountSql =
        """
        SELECT count(*) FROM configs
        WHERE chat_id = 0
          AND backup_encryption_config ? 'Algorithm'
          AND backup_encryption_config ? 'Iterations'
        """;

    private MigrationTestHelper _helper = null!;
    private NpgsqlDataSource _dataSource = null!;
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
        await _dataSource.DisposeAsync();
        _helper.Dispose();
    }

    [Test]
    public async Task GetEncryptionConfigAsync_StoredJsonWithLegacyKeys_Loads()
    {
        // Guard the precondition: the stored row still carries the removed keys.
        Assert.That(await _helper.ExecuteScalarAsync<long>(LegacyKeyCountSql), Is.EqualTo(1),
            "canonical global config must carry the legacy Algorithm and Iterations keys");
        var storedCreatedAt = await _helper.ExecuteScalarAsync<string>(
            "SELECT backup_encryption_config ->> 'CreatedAt' FROM configs WHERE chat_id = 0");

        var config = await _configService.GetEncryptionConfigAsync();

        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config!.Enabled, Is.True);
            // Proves the stored row was read, not a default-constructed config.
            Assert.That(config.CreatedAt, Is.EqualTo(DateTimeOffset.Parse(storedCreatedAt!)));
        }
    }

    [Test]
    public async Task UpdateEncryptionConfigAsync_StoredJsonWithLegacyKeys_DropsThemAndKeepsTheRest()
    {
        // Guard the precondition: the stored row still carries the removed keys.
        Assert.That(await _helper.ExecuteScalarAsync<long>(LegacyKeyCountSql), Is.EqualTo(1),
            "canonical global config must carry the legacy Algorithm and Iterations keys");
        var storedCreatedAt = await _helper.ExecuteScalarAsync<string>(
            "SELECT backup_encryption_config ->> 'CreatedAt' FROM configs WHERE chat_id = 0");

        var protection = Substitute.For<IDataProtectionService>();
        protection.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");
        var passphraseService = new PassphraseManagementService(
            _dataSource,
            protection,
            _configService,
            Substitute.For<IJobScheduler>(),
            NullLogger<PassphraseManagementService>.Instance);

        // Act - the update path deserializes the stored config and writes it back
        await passphraseService.UpdateEncryptionConfigAsync("rotated-test-passphrase");

        var remainingKeys = await _helper.ExecuteScalarAsync<string>(
            """
            SELECT string_agg(key, ',' ORDER BY key)
            FROM configs, jsonb_object_keys(backup_encryption_config) AS key
            WHERE chat_id = 0
            """);
        var config = await _configService.GetEncryptionConfigAsync();

        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingKeys, Is.EqualTo("CreatedAt,Enabled,LastRotatedAt"));
            Assert.That(config!.Enabled, Is.True);
            Assert.That(config.CreatedAt, Is.EqualTo(DateTimeOffset.Parse(storedCreatedAt!)));
            Assert.That(config.LastRotatedAt, Is.Not.Null);
        }
    }
}
