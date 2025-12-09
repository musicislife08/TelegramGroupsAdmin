using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ComponentTests.Services.AI;

/// <summary>
/// Unit tests for AIServiceFactory
/// Focus: Test OUR logic for configuration validation and service creation
/// Mocks: ISystemConfigRepository, IHttpClientFactory
/// </summary>
[TestFixture]
public class AIServiceFactoryTests
{
    private ISystemConfigRepository _mockConfigRepo = null!;
    private IHttpClientFactory _mockHttpClientFactory = null!;
    private ILogger<AIServiceFactory> _mockLogger = null!;
    private ILoggerFactory _mockLoggerFactory = null!;
    private AIServiceFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
        _mockLogger = Substitute.For<ILogger<AIServiceFactory>>();
        _mockLoggerFactory = Substitute.For<ILoggerFactory>();

        // Setup LoggerFactory to return a logger for SemanticKernelChatService
        _mockLoggerFactory.CreateLogger(Arg.Any<string>())
            .Returns(Substitute.For<ILogger<SemanticKernelChatService>>());

        _factory = new AIServiceFactory(
            _mockConfigRepo,
            _mockHttpClientFactory,
            _mockLogger,
            _mockLoggerFactory);
    }

    [TearDown]
    public void TearDown()
    {
        (_mockLoggerFactory as IDisposable)?.Dispose();
    }

    #region GetChatServiceAsync Tests

    [Test]
    public async Task GetChatServiceAsync_ConfigIsNull_ReturnsNull()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_FeatureNotInConfig_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = [],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>()
            // SpamDetection not configured
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_FeatureConnectionIdIsNull_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = [],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = null, // Not configured
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_ConnectionNotFoundById_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "other-connection", Enabled = true }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "non-existent-connection",
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_ConnectionDisabled_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "test-connection",
                    Provider = AIProviderType.OpenAI,
                    Enabled = false // Disabled
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "test-connection",
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_ApiKeyMissingForOpenAI_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "openai",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai",
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // No API key configured
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns((ApiKeysConfig?)null);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_ApiKeyMissingForAzure_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "azure",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = true,
                    AzureEndpoint = "https://test.openai.azure.com"
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "azure",
                    Model = "gpt-4o-mini",
                    AzureDeploymentName = "deployment-1"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // No API key
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(new ApiKeysConfig());

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_LocalOpenAIWithoutKeyAndNoRequirement_ReturnsService()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "ollama",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:11434/v1",
                    LocalRequiresApiKey = false // Keyless
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "ollama",
                    Model = "llama3.2"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(new ApiKeysConfig());

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IChatService>());
    }

    [Test]
    public async Task GetChatServiceAsync_LocalOpenAIRequiresKeyButNoKey_ReturnsNull()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "local-studio",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:1234/v1",
                    LocalRequiresApiKey = true // Requires key
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "local-studio",
                    Model = "local-model"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // No API key
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(new ApiKeysConfig());

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetChatServiceAsync_ValidOpenAIConfig_ReturnsService()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "openai",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai",
                    Model = "gpt-4o-mini"
                }
            }
        };

        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("openai", "test-api-key");

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IChatService>());
    }

    [Test]
    public async Task GetChatServiceAsync_ValidAzureConfig_ReturnsService()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "azure",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = true,
                    AzureEndpoint = "https://test.openai.azure.com",
                    AzureApiVersion = "2024-10-21"
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "azure",
                    Model = "gpt-4o-mini",
                    AzureDeploymentName = "gpt-4o-mini-deployment"
                }
            }
        };

        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("azure", "test-api-key");

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IChatService>());
    }

    [Test]
    public async Task GetChatServiceAsync_ServiceCreationThrows_ReturnsNull()
    {
        // Arrange - Invalid Azure config (missing deployment name)
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "azure-bad",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = true,
                    AzureEndpoint = "https://test.openai.azure.com"
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "azure-bad",
                    Model = "gpt-4o-mini",
                    AzureDeploymentName = null // Missing - will cause exception
                }
            }
        };

        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("azure-bad", "test-api-key");

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _factory.GetChatServiceAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetFeatureStatusAsync Tests

    [Test]
    public async Task GetFeatureStatusAsync_ConfigIsNull_ReturnsAllFalseStatus()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.False);
            Assert.That(result.ConnectionEnabled, Is.False);
            Assert.That(result.RequiresVision, Is.False);
            Assert.That(result.ConnectionId, Is.Null);
            Assert.That(result.ModelName, Is.Null);
        });
    }

    [Test]
    public async Task GetFeatureStatusAsync_FeatureNotConfigured_ReturnsPartialStatus()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = [],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = null, // Not configured
                    RequiresVision = true
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.False);
            Assert.That(result.ConnectionEnabled, Is.False);
            Assert.That(result.RequiresVision, Is.True);
            Assert.That(result.ConnectionId, Is.Null);
            Assert.That(result.ModelName, Is.Null);
        });
    }

    [Test]
    public async Task GetFeatureStatusAsync_ConnectionNotFound_ReturnsIsConfiguredFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = [],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "missing-connection",
                    Model = "gpt-4o-mini",
                    RequiresVision = false
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.False);
            Assert.That(result.ConnectionEnabled, Is.False);
            Assert.That(result.RequiresVision, Is.False);
            Assert.That(result.ConnectionId, Is.EqualTo("missing-connection"));
            Assert.That(result.ModelName, Is.EqualTo("gpt-4o-mini"));
        });
    }

    [Test]
    public async Task GetFeatureStatusAsync_AzureProvider_UsesAzureDeploymentNameForModel()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "azure",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = true
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "azure",
                    Model = "gpt-4o-mini",
                    AzureDeploymentName = "my-deployment-name"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.True);
            Assert.That(result.ConnectionEnabled, Is.True);
            Assert.That(result.ConnectionId, Is.EqualTo("azure"));
            Assert.That(result.ModelName, Is.EqualTo("my-deployment-name")); // Uses deployment name
        });
    }

    [Test]
    public async Task GetFeatureStatusAsync_NonAzureProvider_UsesModelForModelName()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "openai",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai",
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.True);
            Assert.That(result.ConnectionEnabled, Is.True);
            Assert.That(result.ConnectionId, Is.EqualTo("openai"));
            Assert.That(result.ModelName, Is.EqualTo("gpt-4o-mini")); // Uses model name
        });
    }

    [Test]
    public async Task GetFeatureStatusAsync_DisabledConnection_ReturnsConnectionEnabledFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "openai",
                    Provider = AIProviderType.OpenAI,
                    Enabled = false // Disabled
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai",
                    Model = "gpt-4o-mini"
                }
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _factory.GetFeatureStatusAsync(AIFeatureType.SpamDetection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsConfigured, Is.True);
            Assert.That(result.ConnectionEnabled, Is.False);
        });
    }

    #endregion
}
