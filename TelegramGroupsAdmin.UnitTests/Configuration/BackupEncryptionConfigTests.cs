using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Unit tests for BackupEncryptionConfig serialization.
/// Rows stored before the Algorithm and Iterations properties were removed still carry those
/// keys. They must keep loading with no migration, and the keys must not be written back.
/// </summary>
[TestFixture]
public class BackupEncryptionConfigTests
{
    // Shape of a stored row written before the two properties were removed
    private const string LegacyJson =
        """{"Enabled": true, "Algorithm": "AES-256-GCM", "CreatedAt": "2025-11-09T01:20:40.8708993+00:00", "Iterations": 100000, "LastRotatedAt": null}""";

    #region Deserialization - Legacy Keys

    [Test]
    public void Deserialize_LegacyJsonWithAlgorithmAndIterations_LoadsRemainingProperties()
    {
        // Default options, exactly as both production call sites deserialize
        var config = JsonSerializer.Deserialize<BackupEncryptionConfig>(LegacyJson);

        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config!.Enabled, Is.True);
            Assert.That(config.CreatedAt, Is.EqualTo(DateTimeOffset.Parse("2025-11-09T01:20:40.8708993+00:00")));
            Assert.That(config.LastRotatedAt, Is.Null);
        }
    }

    #endregion

    #region Serialization - Legacy Keys Are Not Written Back

    [Test]
    public void Serialize_AfterLoadingLegacyJson_DropsAlgorithmAndIterations()
    {
        var config = JsonSerializer.Deserialize<BackupEncryptionConfig>(LegacyJson)!;

        var json = JsonSerializer.Serialize(config);

        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.That(keys, Is.EquivalentTo(new[] { "Enabled", "CreatedAt", "LastRotatedAt" }));
    }

    #endregion
}
