using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Tests.Configuration;

/// <summary>
/// Unit tests for AIProviderConfig model
/// Tests default initialization, JSON serialization, and configuration structure
/// </summary>
[TestFixture]
public class AIProviderConfigTests
{
    #region Default Initialization Tests

    [Test]
    public void Constructor_InitializesEmptyConnectionsList()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Connections, Is.Not.Null);
        Assert.That(config.Connections, Is.Empty);
    }

    [Test]
    public void Constructor_InitializesAllFiveFeatures()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Features, Is.Not.Null);
            Assert.That(config.Features.Count, Is.EqualTo(5));
            Assert.That(config.Features.ContainsKey(AIFeatureType.SpamDetection), Is.True);
            Assert.That(config.Features.ContainsKey(AIFeatureType.Translation), Is.True);
            Assert.That(config.Features.ContainsKey(AIFeatureType.ImageAnalysis), Is.True);
            Assert.That(config.Features.ContainsKey(AIFeatureType.VideoAnalysis), Is.True);
            Assert.That(config.Features.ContainsKey(AIFeatureType.PromptBuilder), Is.True);
        });
    }

    #endregion

    #region AIFeatureConfig Default Tests

    [Test]
    public void AIFeatureConfig_DefaultModel_IsGpt4oMini()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o-mini"));
    }

    [Test]
    public void AIFeatureConfig_DefaultMaxTokens_Is500()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.Translation].MaxTokens, Is.EqualTo(500));
    }

    [Test]
    public void AIFeatureConfig_DefaultTemperature_Is0Point2()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.2));
    }

    [Test]
    public void AIFeatureConfig_DefaultConnectionId_IsNull()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.Translation].ConnectionId, Is.Null);
    }

    [Test]
    public void AIFeatureConfig_ImageAnalysis_RequiresVisionTrue()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.ImageAnalysis].RequiresVision, Is.True);
    }

    [Test]
    public void AIFeatureConfig_VideoAnalysis_RequiresVisionTrue()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.That(config.Features[AIFeatureType.VideoAnalysis].RequiresVision, Is.True);
    }

    [Test]
    public void AIFeatureConfig_NonVisionFeatures_RequiresVisionFalse()
    {
        // Act
        var config = new AIProviderConfig();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Features[AIFeatureType.SpamDetection].RequiresVision, Is.False);
            Assert.That(config.Features[AIFeatureType.Translation].RequiresVision, Is.False);
            Assert.That(config.Features[AIFeatureType.PromptBuilder].RequiresVision, Is.False);
        });
    }

    #endregion

    #region AIConnection Default Tests

    [Test]
    public void AIConnection_DefaultProvider_IsOpenAI()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.Provider, Is.EqualTo(AIProviderType.OpenAI));
    }

    [Test]
    public void AIConnection_DefaultEnabled_IsFalse()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.Enabled, Is.False);
    }

    [Test]
    public void AIConnection_DefaultAzureApiVersion_Is2024_10_21()
    {
        // Act
        var connection = new AIConnection();

        // Assert - Updated to 2024-10-21 (supported by Semantic Kernel 1.45.0)
        Assert.That(connection.AzureApiVersion, Is.EqualTo("2024-10-21"));
    }

    [Test]
    public void AIConnection_DefaultId_IsEmptyString()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.Id, Is.EqualTo(""));
    }

    [Test]
    public void AIConnection_DefaultLocalRequiresApiKey_IsFalse()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.LocalRequiresApiKey, Is.False);
    }

    [Test]
    public void AIConnection_DefaultAvailableModels_IsEmptyList()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.AvailableModels, Is.Not.Null);
        Assert.That(connection.AvailableModels, Is.Empty);
    }

    [Test]
    public void AIConnection_DefaultModelsLastFetched_IsNull()
    {
        // Act
        var connection = new AIConnection();

        // Assert
        Assert.That(connection.ModelsLastFetched, Is.Null);
    }

    #endregion

    #region JSON Serialization Tests

    [Test]
    public void SerializeAndDeserialize_PreservesAllProperties()
    {
        // Arrange
        var config = new AIProviderConfig
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
                        new AIModelInfo { Id = "gpt-4o", SizeBytes = null }
                    ],
                    ModelsLastFetched = DateTimeOffset.UtcNow
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai-prod",
                    Model = "gpt-4o",
                    MaxTokens = 1000,
                    Temperature = 0.5
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.That(deserialized, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Connections.Count, Is.EqualTo(1));
            Assert.That(deserialized.Connections[0].Id, Is.EqualTo("openai-prod"));
            Assert.That(deserialized.Connections[0].Provider, Is.EqualTo(AIProviderType.OpenAI));
            Assert.That(deserialized.Connections[0].Enabled, Is.True);
            Assert.That(deserialized.Connections[0].AvailableModels.Count, Is.EqualTo(1));
            Assert.That(deserialized.Connections[0].AvailableModels[0].Id, Is.EqualTo("gpt-4o"));
            Assert.That(deserialized.Features[AIFeatureType.SpamDetection].ConnectionId, Is.EqualTo("openai-prod"));
            Assert.That(deserialized.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
        });
    }

    [Test]
    public void SerializeAndDeserialize_NullConnectionId_PreservesNull()
    {
        // Arrange
        var config = new AIProviderConfig();

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.That(deserialized!.Features[AIFeatureType.SpamDetection].ConnectionId, Is.Null);
    }

    [Test]
    public void SerializeAndDeserialize_EnumValues_SerializeCorrectly()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Provider = AIProviderType.AzureOpenAI },
                new AIConnection { Provider = AIProviderType.LocalOpenAI }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Connections[0].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
            Assert.That(deserialized.Connections[1].Provider, Is.EqualTo(AIProviderType.LocalOpenAI));
        });
    }

    [Test]
    public void SerializeAndDeserialize_AIModelInfo_PreservesBothProperties()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    AvailableModels =
                    [
                        new AIModelInfo { Id = "gpt-4o", SizeBytes = null },
                        new AIModelInfo { Id = "llama3.2", SizeBytes = 7365960704 }
                    ]
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Connections[0].AvailableModels[0].Id, Is.EqualTo("gpt-4o"));
            Assert.That(deserialized.Connections[0].AvailableModels[0].SizeBytes, Is.Null);
            Assert.That(deserialized.Connections[0].AvailableModels[1].Id, Is.EqualTo("llama3.2"));
            Assert.That(deserialized.Connections[0].AvailableModels[1].SizeBytes, Is.EqualTo(7365960704));
        });
    }

    [Test]
    public void SerializeAndDeserialize_AzureProperties_PreserveCorrectly()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Provider = AIProviderType.AzureOpenAI,
                    AzureEndpoint = "https://my-resource.openai.azure.com",
                    AzureApiVersion = "2024-10-01"
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Connections[0].AzureEndpoint, Is.EqualTo("https://my-resource.openai.azure.com"));
            Assert.That(deserialized.Connections[0].AzureApiVersion, Is.EqualTo("2024-10-01"));
        });
    }

    [Test]
    public void SerializeAndDeserialize_LocalProperties_PreserveCorrectly()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Provider = AIProviderType.LocalOpenAI,
                    LocalEndpoint = "http://localhost:11434/v1",
                    LocalRequiresApiKey = true
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Connections[0].LocalEndpoint, Is.EqualTo("http://localhost:11434/v1"));
            Assert.That(deserialized.Connections[0].LocalRequiresApiKey, Is.True);
        });
    }

    [Test]
    public void SerializeAndDeserialize_AzureDeploymentName_PreservesCorrectly()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    AzureDeploymentName = "gpt-4o-mini-deployment"
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<AIProviderConfig>(json);

        // Assert
        Assert.That(deserialized!.Features[AIFeatureType.SpamDetection].AzureDeploymentName,
            Is.EqualTo("gpt-4o-mini-deployment"));
    }

    #endregion
}
