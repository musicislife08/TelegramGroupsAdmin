using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.Tests.Services;

/// <summary>
/// Unit tests for AIProviderMigrationService
/// Tests migration logic from legacy OpenAI config to multi-provider format
/// Uses NSubstitute to mock dependencies - no database required
/// </summary>
[TestFixture]
public class AIProviderMigrationServiceTests
{
    private IServiceScopeFactory _mockScopeFactory = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private ISystemConfigRepository _mockConfigRepo = null!;
    private ILogger<AIProviderMigrationService> _mockLogger = null!;
    private AIProviderMigrationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();

        // Setup scope factory to return mock scope
        _mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockScope.ServiceProvider.Returns(_mockServiceProvider);
        _mockServiceProvider.GetService(typeof(ISystemConfigRepository)).Returns(_mockConfigRepo);

        _mockLogger = Substitute.For<ILogger<AIProviderMigrationService>>();
        _service = new AIProviderMigrationService(_mockScopeFactory, _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    #region No Existing Config + No API Keys Tests

    [Test]
    public async Task StartAsync_WithNoConfigAndNoKeys_CreatesEmptyConfig()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(new ApiKeysConfig());

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Connections, Is.Empty, "Should have no connections when no API keys exist");
            Assert.That(savedConfig.Features.Count, Is.EqualTo(5), "Should initialize all 5 feature types");
            Assert.That(savedConfig.Features[AIFeatureType.SpamDetection].ConnectionId, Is.Null, "Features should have null ConnectionId");
        });
    }

    #endregion

    #region No Existing Config + OpenAI Key Tests

    [Test]
    public async Task StartAsync_WithNoConfigButOpenAIKey_CreatesOpenAIConnection()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Connections.Count, Is.EqualTo(1));
            Assert.That(savedConfig.Connections[0].Id, Is.EqualTo("openai"));
            Assert.That(savedConfig.Connections[0].Provider, Is.EqualTo(AIProviderType.OpenAI));
            Assert.That(savedConfig.Connections[0].Enabled, Is.True);
        });
    }

    [Test]
    public async Task StartAsync_WithOpenAIKey_AssignsToAllFeatures()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Features[AIFeatureType.SpamDetection].ConnectionId, Is.EqualTo("openai"));
            Assert.That(savedConfig.Features[AIFeatureType.Translation].ConnectionId, Is.EqualTo("openai"));
            Assert.That(savedConfig.Features[AIFeatureType.ImageAnalysis].ConnectionId, Is.EqualTo("openai"));
            Assert.That(savedConfig.Features[AIFeatureType.VideoAnalysis].ConnectionId, Is.EqualTo("openai"));
            Assert.That(savedConfig.Features[AIFeatureType.PromptBuilder].ConnectionId, Is.EqualTo("openai"));
        });
    }

    [Test]
    public async Task StartAsync_WithOldOpenAIConfig_UsesModelFromOldConfig()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };
        var oldConfig = new OpenAIConfig
        {
            Model = "gpt-4o",
            MaxTokens = 1000,
            Temperature = 0.5
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns(oldConfig);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
            Assert.That(savedConfig.Features[AIFeatureType.SpamDetection].MaxTokens, Is.EqualTo(1000));
            Assert.That(savedConfig.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.5));
        });
    }

    [Test]
    public async Task StartAsync_WithNullModelInOldConfig_UsesDefault()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };
        var oldConfig = new OpenAIConfig
        {
            Model = null
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns(oldConfig);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig!.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o-mini"));
    }

    #endregion

    #region No Existing Config + Azure Key Tests

    [Test]
    public async Task StartAsync_WithAzureKey_CreatesDisabledAzureConnection()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["azure-openai"] = "azure-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Connections.Count, Is.EqualTo(1));
            Assert.That(savedConfig.Connections[0].Id, Is.EqualTo("azure-openai"));
            Assert.That(savedConfig.Connections[0].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
            Assert.That(savedConfig.Connections[0].Enabled, Is.False, "Azure should be disabled by default");
        });
    }

    [Test]
    public async Task StartAsync_WithOnlyAzureKey_FeaturesHaveNullConnectionId()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["azure-openai"] = "azure-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig!.Features[AIFeatureType.SpamDetection].ConnectionId, Is.Null,
            "Features should not be assigned to disabled Azure connection");
    }

    #endregion

    #region No Existing Config + Local AI Key Tests

    [Test]
    public async Task StartAsync_WithLocalAIKey_CreatesDisabledLocalConnection()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["local-ai"] = "local-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Connections.Count, Is.EqualTo(1));
            Assert.That(savedConfig.Connections[0].Id, Is.EqualTo("local-ai"));
            Assert.That(savedConfig.Connections[0].Provider, Is.EqualTo(AIProviderType.LocalOpenAI));
            Assert.That(savedConfig.Connections[0].Enabled, Is.False, "Local AI should be disabled by default");
            Assert.That(savedConfig.Connections[0].LocalRequiresApiKey, Is.True);
        });
    }

    #endregion

    #region Multiple Keys Tests

    [Test]
    public async Task StartAsync_WithAllThreeKeys_CreatesThreeConnections()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key",
                ["azure-openai"] = "azure-test-key",
                ["local-ai"] = "local-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Connections.Count, Is.EqualTo(3));
            Assert.That(savedConfig.Connections.Any(c => c.Id == "openai" && c.Enabled), Is.True);
            Assert.That(savedConfig.Connections.Any(c => c.Id == "azure-openai" && !c.Enabled), Is.True);
            Assert.That(savedConfig.Connections.Any(c => c.Id == "local-ai" && !c.Enabled), Is.True);
        });
    }

    [Test]
    public async Task StartAsync_WithOpenAIAndAzure_OpenAIIsPrimary()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key",
                ["azure-openai"] = "azure-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig!.Features[AIFeatureType.SpamDetection].ConnectionId, Is.EqualTo("openai"),
            "OpenAI should be the primary connection when multiple keys exist");
    }

    #endregion

    #region Existing Config Tests

    [Test]
    public async Task StartAsync_WithExistingConfig_SkipsMigration()
    {
        // Arrange
        var existingConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "existing", Enabled = true }
            ]
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(existingConfig);

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        await _mockConfigRepo.DidNotReceive().SaveAIProviderConfigAsync(Arg.Any<AIProviderConfig>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Legacy API Key Migration Tests

    [Test]
    public async Task StartAsync_MigratesLegacyKeysBeforeConfigMigration()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            OpenAI = "sk-legacy-key" // Legacy property
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        ApiKeysConfig? savedApiKeys = null;
        await _mockConfigRepo.SaveApiKeysAsync(Arg.Do<ApiKeysConfig>(k => savedApiKeys = k), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        await _mockConfigRepo.Received(1).SaveApiKeysAsync(Arg.Any<ApiKeysConfig>(), Arg.Any<CancellationToken>());
        Assert.That(savedApiKeys, Is.Not.Null);
        Assert.That(savedApiKeys!.AIConnectionKeys.ContainsKey("openai"), Is.True);
    }

    [Test]
    public async Task StartAsync_WithNoLegacyKeys_DoesNotSaveApiKeys()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        await _mockConfigRepo.DidNotReceive().SaveApiKeysAsync(Arg.Any<ApiKeysConfig>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public async Task StartAsync_WhenRepositoryThrows_DoesNotThrow()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns<AIProviderConfig?>(x => throw new InvalidOperationException("Database error"));

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await _service.StartAsync(CancellationToken.None));
    }

    [Test]
    public async Task StartAsync_WhenRepositoryThrows_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Database error");
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns<AIProviderConfig?>(x => throw exception);

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error during AI provider config migration")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Empty/Whitespace API Key Tests

    [Test]
    public async Task StartAsync_WithEmptyStringKeys_CreatesNoConnections()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = ""
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig!.Connections, Is.Empty, "Empty string keys should not create connections");
    }

    #endregion

    #region StopAsync Tests

    [Test]
    public async Task StopAsync_CompletesSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await _service.StopAsync(CancellationToken.None));
    }

    #endregion

    #region Feature Initialization Tests

    [Test]
    public async Task StartAsync_InitializesAllFiveFeatureTypes()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.That(savedConfig!.Features.Keys, Is.EquivalentTo(new[]
        {
            AIFeatureType.SpamDetection,
            AIFeatureType.Translation,
            AIFeatureType.ImageAnalysis,
            AIFeatureType.VideoAnalysis,
            AIFeatureType.PromptBuilder
        }));
    }

    [Test]
    public async Task StartAsync_ImageAndVideoAnalysis_HaveRequiresVisionTrue()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Features[AIFeatureType.ImageAnalysis].RequiresVision, Is.True);
            Assert.That(savedConfig.Features[AIFeatureType.VideoAnalysis].RequiresVision, Is.True);
        });
    }

    [Test]
    public async Task StartAsync_PromptBuilder_HasHigherMaxTokensAndTemperature()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);
        _mockConfigRepo.GetOpenAIConfigAsync(Arg.Any<CancellationToken>())
            .Returns((OpenAIConfig?)null);

        AIProviderConfig? savedConfig = null;
        await _mockConfigRepo.SaveAIProviderConfigAsync(Arg.Do<AIProviderConfig>(c => savedConfig = c), Arg.Any<CancellationToken>());

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(savedConfig!.Features[AIFeatureType.PromptBuilder].MaxTokens, Is.EqualTo(1000));
            Assert.That(savedConfig.Features[AIFeatureType.PromptBuilder].Temperature, Is.EqualTo(0.3));
        });
    }

    #endregion
}
