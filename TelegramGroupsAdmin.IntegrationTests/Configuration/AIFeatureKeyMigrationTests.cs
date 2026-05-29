using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Verifies the RemapAIFeatureConfigKeysToInt migration's data conversion: a configs row
/// stored by the OLD code path (AIFeatureType keys serialized as enum NAMES) is rewritten so
/// the features object is keyed by the enum's integer values, and survives deserialization
/// through the int-keyed AIProviderConfigData DTO.
/// </summary>
[TestFixture]
public class AIFeatureKeyMigrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Intentionally mirrors 20260529065637_RemapAIFeatureConfigKeysToInt.Up verbatim.
    // This is a deliberate independent-verification copy, NOT accidental drift: EF migrations
    // must stay self-contained and immutable (a migration that referenced shared/mutable SQL
    // could have its historical meaning change retroactively), so the test keeps its own copy
    // rather than extracting a shared constant. If that migration's Up SQL ever changes, this
    // copy must be updated by hand to match.
    private const string UpSql = """
        UPDATE configs
        SET ai_provider_config = jsonb_set(
            ai_provider_config,
            '{features}',
            (
                SELECT COALESCE(jsonb_object_agg(
                    CASE elem.key
                        WHEN 'SpamDetection' THEN '0'
                        WHEN 'Translation'   THEN '1'
                        WHEN 'ImageAnalysis' THEN '2'
                        WHEN 'VideoAnalysis' THEN '3'
                        WHEN 'PromptBuilder' THEN '4'
                        WHEN 'ProfileScan'   THEN '5'
                        ELSE elem.key
                    END, elem.value), '{}'::jsonb)
                FROM jsonb_each(ai_provider_config -> 'features') AS elem
            ))
        WHERE ai_provider_config IS NOT NULL
          AND ai_provider_config ? 'features';
        """;

    private MigrationTestHelper? _testHelper;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
    }

    [Test]
    public async Task UpMigration_RewritesNameKeyedFeaturesToIntKeys()
    {
        // Arrange: seed the global config row with OLD name-keyed JSON.
        const string oldJson =
            """{"connections":[],"features":{"SpamDetection":{"model":"gpt-4o","maxTokens":600,"temperature":0.2,"requiresVision":false,"connectionId":null,"azureDeploymentName":null},"ProfileScan":{"model":"gpt-4o-mini","maxTokens":500,"temperature":0.2,"requiresVision":true,"connectionId":null,"azureDeploymentName":null}}}""";

        await _testHelper!.ExecuteSqlAsync(
            $"""
             INSERT INTO configs (chat_id, ai_provider_config, created_at, updated_at)
             VALUES (0, '{oldJson}'::jsonb, NOW(), NOW());
             """);

        // Act: run the migration's Up SQL.
        await _testHelper.ExecuteSqlAsync(UpSql);

        // Assert: keys are now ints, not names, and a value survived.
        var hasKey0 = await _testHelper.ExecuteScalarAsync<bool>(
            "SELECT (ai_provider_config -> 'features') ? '0' FROM configs WHERE chat_id = 0");
        var hasKey5 = await _testHelper.ExecuteScalarAsync<bool>(
            "SELECT (ai_provider_config -> 'features') ? '5' FROM configs WHERE chat_id = 0");
        var hasSpamDetection = await _testHelper.ExecuteScalarAsync<bool>(
            "SELECT (ai_provider_config -> 'features') ? 'SpamDetection' FROM configs WHERE chat_id = 0");
        var hasProfileScan = await _testHelper.ExecuteScalarAsync<bool>(
            "SELECT (ai_provider_config -> 'features') ? 'ProfileScan' FROM configs WHERE chat_id = 0");
        var maxTokens = await _testHelper.ExecuteScalarAsync<int>(
            "SELECT (ai_provider_config #>> '{features,0,maxTokens}')::int FROM configs WHERE chat_id = 0");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hasKey0, Is.True, "SpamDetection should have been remapped to key \"0\"");
            Assert.That(hasKey5, Is.True, "ProfileScan should have been remapped to key \"5\"");
            Assert.That(hasSpamDetection, Is.False, "Name key \"SpamDetection\" should be gone");
            Assert.That(hasProfileScan, Is.False, "Name key \"ProfileScan\" should be gone");
            Assert.That(maxTokens, Is.EqualTo(600), "SpamDetection.maxTokens value should survive the remap");
        }

        // And: the rewritten JSON now deserializes through the int-keyed DTO to the domain model.
        var storedJson = await _testHelper.ExecuteScalarAsync<string>(
            "SELECT ai_provider_config::text FROM configs WHERE chat_id = 0");
        Assert.That(storedJson, Is.Not.Null);

        var model = JsonSerializer.Deserialize<AIProviderConfigData>(storedJson!, JsonOptions)!.ToModel();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Features, Has.Count.EqualTo(2));
            Assert.That(model.Features[AIFeatureType.SpamDetection].MaxTokens, Is.EqualTo(600));
            Assert.That(model.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
            Assert.That(model.Features[AIFeatureType.ProfileScan].RequiresVision, Is.True);
        }
    }
}
