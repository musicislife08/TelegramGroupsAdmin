using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class AIProviderConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static AIProviderConfig BuildPopulatedModel() => new()
    {
        Connections =
        [
            new AIConnection
            {
                Id = "openai-prod",
                Provider = AIProviderType.OpenAI,
                Enabled = true,
                AvailableModels =
                [
                    new AIModelInfo { Id = "gpt-4o" },
                    new AIModelInfo { Id = "llama3.2", SizeBytes = 7365960704 }
                ],
                ModelsLastFetched = DateTimeOffset.UnixEpoch
            },
            new AIConnection
            {
                Id = "azure-prod",
                Provider = AIProviderType.AzureOpenAI,
                Enabled = false,
                AzureEndpoint = "https://my-resource.openai.azure.com"
            }
        ],
        Features = new()
        {
            [AIFeatureType.SpamDetection] = new() { ConnectionId = "openai-prod", Model = "gpt-4o", Temperature = 0.3f, MaxTokens = 600 },
            [AIFeatureType.Translation] = new() { ConnectionId = "openai-prod", Model = "gpt-4o-mini" },
            [AIFeatureType.ImageAnalysis] = new() { RequiresVision = true },
            [AIFeatureType.VideoAnalysis] = new() { RequiresVision = true },
            [AIFeatureType.PromptBuilder] = new(),
            [AIFeatureType.ProfileScan] = new() { RequiresVision = true, AzureDeploymentName = "vision-deploy" }
        }
    };

    [Test]
    public void ToData_ThenToModel_RoundTripsAllFields()
    {
        var original = BuildPopulatedModel();

        var roundTripped = original.ToData().ToModel();

        Assert.That(roundTripped.Connections, Has.Count.EqualTo(2));
        Assert.That(roundTripped.Connections[0].Id, Is.EqualTo("openai-prod"));
        Assert.That(roundTripped.Connections[0].Provider, Is.EqualTo(AIProviderType.OpenAI));
        Assert.That(roundTripped.Connections[0].AvailableModels, Has.Count.EqualTo(2));
        Assert.That(roundTripped.Connections[0].AvailableModels[1].SizeBytes, Is.EqualTo(7365960704));
        Assert.That(roundTripped.Connections[0].ModelsLastFetched, Is.EqualTo(DateTimeOffset.UnixEpoch));
        Assert.That(roundTripped.Connections[1].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
        Assert.That(roundTripped.Connections[1].AzureEndpoint, Is.EqualTo("https://my-resource.openai.azure.com"));

        Assert.That(roundTripped.Features, Has.Count.EqualTo(6));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.3f));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].MaxTokens, Is.EqualTo(600));
        Assert.That(roundTripped.Features[AIFeatureType.ProfileScan].RequiresVision, Is.True);
        Assert.That(roundTripped.Features[AIFeatureType.ProfileScan].AzureDeploymentName, Is.EqualTo("vision-deploy"));
    }

    [Test]
    public void ToData_SerializesFeatureKeysAsIntegers()
    {
        // The whole point of the DTO: feature keys persist as ints ("0".."5"), NOT enum
        // names. This is what makes a future AIFeatureType rename (#282) migration-free.
        var json = JsonSerializer.Serialize(BuildPopulatedModel().ToData(), JsonOptions);

        Assert.That(json, Does.Contain("\"0\":"));   // SpamDetection
        Assert.That(json, Does.Contain("\"5\":"));   // ProfileScan
        Assert.That(json, Does.Not.Contain("SpamDetection"));
        Assert.That(json, Does.Not.Contain("ProfileScan"));
    }

    [Test]
    [TestCase(AIProviderType.OpenAI, 0)]
    [TestCase(AIProviderType.AzureOpenAI, 1)]
    [TestCase(AIProviderType.OpenAICompatible, 2)]
    [TestCase(AIProviderType.OpenRouter, 3)]
    [TestCase(AIProviderType.Anthropic, 4)]
    public void ToData_PinsProviderEnumToIntValue_AndRoundTrips(AIProviderType provider, int expectedStored)
    {
        // The mapping casts (int)Provider on the way down and (AIProviderType)int on the way
        // back. Pinning each numeric value catches an accidental enum-ordinal shift that would
        // silently remap stored connections (e.g. an OpenRouter connection read back as Anthropic).
        var model = new AIProviderConfig
        {
            Connections = [new AIConnection { Id = "c", Provider = provider, Enabled = true }]
        };

        var data = model.ToData();
        Assert.That(data.Connections[0].Provider, Is.EqualTo(expectedStored));

        var back = data.ToModel();
        Assert.That(back.Connections[0].Provider, Is.EqualTo(provider));
    }

    [Test]
    public void IntKeyedJson_RoundTripsThroughDtoToModel()
    {
        // New stored format (int keys) reads back through the DTO to the domain model.
        var stored = JsonSerializer.Serialize(BuildPopulatedModel().ToData(), JsonOptions);

        var model = JsonSerializer.Deserialize<AIProviderConfigData>(stored, JsonOptions)!.ToModel();

        Assert.That(model.Features, Has.Count.EqualTo(6));
        Assert.That(model.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
        Assert.That(model.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.3f));
        Assert.That(model.Features[AIFeatureType.ProfileScan].RequiresVision, Is.True);
        Assert.That(model.Connections[1].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
    }
}
