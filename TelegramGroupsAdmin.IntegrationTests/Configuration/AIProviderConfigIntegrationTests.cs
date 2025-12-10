using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for AIProviderConfig and related API key storage.
/// Uses real PostgreSQL via Testcontainers to validate:
///
/// 1. GetAIProviderConfigAsync - Returns null when not set, deserializes complex JSONB correctly
/// 2. SaveAIProviderConfigAsync - Creates/updates JSONB column with nested structures
/// 3. AIConnectionKeys encryption - Verifies keys are encrypted in database
/// 4. Round-trip preservation - Complex objects (connections, features, models) survive save/load
/// </summary>
[TestFixture]
public class AIProviderConfigIntegrationTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ISystemConfigRepository? _configRepo;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys
        var keyDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}"));
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(keyDirectory);

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Register repository
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Create repository instance
        var scope = _serviceProvider.CreateScope();
        _configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region GetAIProviderConfigAsync Tests

    [Test]
    public async Task GetAIProviderConfigAsync_WhenNoConfigExists_ShouldReturnNull()
    {
        // Act
        var config = await _configRepo!.GetAIProviderConfigAsync();

        // Assert
        Assert.That(config, Is.Null);
    }

    [Test]
    public async Task GetAIProviderConfigAsync_AfterSave_ShouldReturnSavedConfig()
    {
        // Arrange
        var configToSave = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "openai-main",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "openai-main",
                    Model = "gpt-4o-mini",
                    MaxTokens = 500,
                    Temperature = 0.2
                }
            }
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(configToSave);
        var retrievedConfig = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrievedConfig, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrievedConfig!.Connections, Has.Count.EqualTo(1));
            Assert.That(retrievedConfig.Connections[0].Id, Is.EqualTo("openai-main"));
            Assert.That(retrievedConfig.Connections[0].Provider, Is.EqualTo(AIProviderType.OpenAI));
            Assert.That(retrievedConfig.Connections[0].Enabled, Is.True);
        });
    }

    #endregion

    #region SaveAIProviderConfigAsync Tests

    [Test]
    public async Task SaveAIProviderConfigAsync_ShouldCreateNewRecord_WhenNoneExists()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "local-ollama",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:11434/v1",
                    LocalRequiresApiKey = false
                }
            ]
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);

        // Assert - Verify by reading back
        var retrieved = await _configRepo.GetAIProviderConfigAsync();
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Connections[0].LocalEndpoint, Is.EqualTo("http://localhost:11434/v1"));
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_ShouldUpdateExistingRecord()
    {
        // Arrange - Save initial config
        var initialConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "initial",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                }
            ]
        };
        await _configRepo!.SaveAIProviderConfigAsync(initialConfig);

        // Act - Update config with different connection
        var updatedConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "updated",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = false,
                    AzureEndpoint = "https://test.openai.azure.com",
                    AzureApiVersion = "2024-10-21"
                }
            ]
        };
        await _configRepo.SaveAIProviderConfigAsync(updatedConfig);

        // Assert
        var retrieved = await _configRepo.GetAIProviderConfigAsync();
        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Connections, Has.Count.EqualTo(1));
            Assert.That(retrieved.Connections[0].Id, Is.EqualTo("updated"));
            Assert.That(retrieved.Connections[0].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
        });
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_WithMultipleConnections_ShouldPreserveAll()
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
                },
                new AIConnection
                {
                    Id = "azure",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = false,
                    AzureEndpoint = "https://test.openai.azure.com",
                    AzureApiVersion = "2024-10-21"
                },
                new AIConnection
                {
                    Id = "local",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:11434/v1"
                }
            ]
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Connections, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(retrieved.Connections.Select(c => c.Id), Contains.Item("openai"));
            Assert.That(retrieved.Connections.Select(c => c.Id), Contains.Item("azure"));
            Assert.That(retrieved.Connections.Select(c => c.Id), Contains.Item("local"));
        });
    }

    #endregion

    #region Feature Configuration Tests

    [Test]
    public async Task SaveAIProviderConfigAsync_WithAllFeatures_ShouldPreserveAllSettings()
    {
        // Arrange - Config with all 5 feature types
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "main", Provider = AIProviderType.OpenAI, Enabled = true }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "main",
                    Model = "gpt-4o-mini",
                    MaxTokens = 500,
                    Temperature = 0.2
                },
                [AIFeatureType.Translation] = new AIFeatureConfig
                {
                    ConnectionId = "main",
                    Model = "gpt-4o",
                    MaxTokens = 1000,
                    Temperature = 0.3
                },
                [AIFeatureType.ImageAnalysis] = new AIFeatureConfig
                {
                    ConnectionId = "main",
                    Model = "gpt-4o",
                    MaxTokens = 500,
                    Temperature = 0.1
                },
                [AIFeatureType.VideoAnalysis] = new AIFeatureConfig
                {
                    ConnectionId = "main",
                    Model = "gpt-4o",
                    MaxTokens = 500,
                    Temperature = 0.1
                },
                [AIFeatureType.PromptBuilder] = new AIFeatureConfig
                {
                    ConnectionId = "main",
                    Model = "gpt-4o",
                    MaxTokens = 2000,
                    Temperature = 0.5
                }
            }
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Features, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(retrieved.Features[AIFeatureType.SpamDetection].MaxTokens, Is.EqualTo(500));
            Assert.That(retrieved.Features[AIFeatureType.Translation].MaxTokens, Is.EqualTo(1000));
            Assert.That(retrieved.Features[AIFeatureType.PromptBuilder].MaxTokens, Is.EqualTo(2000));
            Assert.That(retrieved.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.2));
        });
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_WithAzureDeploymentName_ShouldPreserve()
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
                    AzureEndpoint = "https://myresource.openai.azure.com",
                    AzureApiVersion = "2024-10-21"
                }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = "azure",
                    Model = "gpt-4o-mini",
                    AzureDeploymentName = "my-gpt4o-mini-deployment"
                }
            }
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Features[AIFeatureType.SpamDetection].AzureDeploymentName,
            Is.EqualTo("my-gpt4o-mini-deployment"));
    }

    #endregion

    #region AvailableModels Tests

    [Test]
    public async Task SaveAIProviderConfigAsync_WithAvailableModels_ShouldPreserveList()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "local",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:11434/v1",
                    AvailableModels =
                    [
                        new AIModelInfo { Id = "llama3.2:latest", SizeBytes = 2048000000 },
                        new AIModelInfo { Id = "codellama:13b", SizeBytes = 7365960000 },
                        new AIModelInfo { Id = "mistral:7b", SizeBytes = 4100000000 }
                    ]
                }
            ]
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        var models = retrieved!.Connections[0].AvailableModels;
        Assert.That(models, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(models.Select(m => m.Id), Contains.Item("llama3.2:latest"));
            Assert.That(models.Select(m => m.Id), Contains.Item("codellama:13b"));
            Assert.That(models.First(m => m.Id == "llama3.2:latest").SizeBytes, Is.EqualTo(2048000000));
        });
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_WithEmptyAvailableModels_ShouldPreserveEmptyList()
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
                    Enabled = true,
                    AvailableModels = [] // Empty list
                }
            ]
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Connections[0].AvailableModels, Is.Empty);
    }

    #endregion

    #region APIKeysConfig Encryption Tests

    [Test]
    public async Task SaveApiKeysAsync_WithAIConnectionKeys_ShouldEncryptInDatabase()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("openai", "sk-test-key-12345");
        apiKeys.SetAIConnectionKey("azure", "azure-key-67890");

        // Act
        await _configRepo!.SaveApiKeysAsync(apiKeys);

        // Assert - Verify encrypted in database (column is api_keys, encrypted by Data Protection)
        var storedValue = await _testHelper!.ExecuteScalarAsync<string>(
            "SELECT api_keys FROM configs WHERE chat_id = 0");
        Assert.That(storedValue, Is.Not.Null);
        Assert.That(storedValue, Does.Not.Contain("sk-test-key"), "API key should be encrypted");
        Assert.That(storedValue, Does.Not.Contain("azure-key"), "API key should be encrypted");
    }

    [Test]
    public async Task GetApiKeysAsync_WithAIConnectionKeys_ShouldDecryptCorrectly()
    {
        // Arrange
        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("openai", "sk-test-key-12345");
        apiKeys.SetAIConnectionKey("local", "local-api-key-xyz");
        await _configRepo!.SaveApiKeysAsync(apiKeys);

        // Act
        var retrieved = await _configRepo.GetApiKeysAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.GetAIConnectionKey("openai"), Is.EqualTo("sk-test-key-12345"));
            Assert.That(retrieved.GetAIConnectionKey("local"), Is.EqualTo("local-api-key-xyz"));
            Assert.That(retrieved.GetAIConnectionKey("nonexistent"), Is.Null);
        });
    }

    [Test]
    public async Task SaveApiKeysAsync_UpdateAIConnectionKey_ShouldUpdateCorrectly()
    {
        // Arrange - Save initial keys
        var initialKeys = new ApiKeysConfig();
        initialKeys.SetAIConnectionKey("openai", "initial-key");
        await _configRepo!.SaveApiKeysAsync(initialKeys);

        // Act - Update the key
        var updatedKeys = new ApiKeysConfig();
        updatedKeys.SetAIConnectionKey("openai", "updated-key");
        await _configRepo.SaveApiKeysAsync(updatedKeys);

        // Assert
        var retrieved = await _configRepo.GetApiKeysAsync();
        Assert.That(retrieved!.GetAIConnectionKey("openai"), Is.EqualTo("updated-key"));
    }

    [Test]
    public async Task SaveApiKeysAsync_RemoveAIConnectionKey_ShouldRemoveFromDictionary()
    {
        // Arrange - Save keys with multiple connections
        var keys = new ApiKeysConfig();
        keys.SetAIConnectionKey("openai", "key1");
        keys.SetAIConnectionKey("azure", "key2");
        await _configRepo!.SaveApiKeysAsync(keys);

        // Act - Save with one key removed (empty string removes it)
        keys.SetAIConnectionKey("openai", ""); // This should remove the key
        await _configRepo.SaveApiKeysAsync(keys);

        // Assert
        var retrieved = await _configRepo.GetApiKeysAsync();
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.GetAIConnectionKey("openai"), Is.Null, "Empty key should be removed");
            Assert.That(retrieved.GetAIConnectionKey("azure"), Is.EqualTo("key2"), "Other keys should remain");
        });
    }

    #endregion

    #region Config Isolation Tests

    [Test]
    public async Task SaveAIProviderConfigAsync_ShouldNotAffectApiKeys()
    {
        // Arrange - Save API keys first
        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("openai", "test-api-key");
        await _configRepo!.SaveApiKeysAsync(apiKeys);

        // Act - Save AI provider config
        var aiConfig = new AIProviderConfig
        {
            Connections = [new AIConnection { Id = "openai", Provider = AIProviderType.OpenAI, Enabled = true }]
        };
        await _configRepo.SaveAIProviderConfigAsync(aiConfig);

        // Assert - API keys should be unchanged
        var retrievedKeys = await _configRepo.GetApiKeysAsync();
        Assert.That(retrievedKeys!.GetAIConnectionKey("openai"), Is.EqualTo("test-api-key"));
    }

    [Test]
    public async Task SaveApiKeysAsync_ShouldNotAffectAIProviderConfig()
    {
        // Arrange - Save AI provider config first
        var aiConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "local",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:11434"
                }
            ]
        };
        await _configRepo!.SaveAIProviderConfigAsync(aiConfig);

        // Act - Save API keys
        var apiKeys = new ApiKeysConfig();
        apiKeys.SetAIConnectionKey("local", "local-key");
        await _configRepo.SaveApiKeysAsync(apiKeys);

        // Assert - AI provider config should be unchanged
        var retrievedConfig = await _configRepo.GetAIProviderConfigAsync();
        Assert.That(retrievedConfig, Is.Not.Null);
        Assert.That(retrievedConfig!.Connections[0].LocalEndpoint, Is.EqualTo("http://localhost:11434"));
    }

    #endregion

    #region JSONB Serialization Edge Cases

    [Test]
    public async Task SaveAIProviderConfigAsync_WithNullConnectionId_ShouldPreserve()
    {
        // Arrange - Feature with null ConnectionId (disabled feature)
        var config = new AIProviderConfig
        {
            Connections = [new AIConnection { Id = "main", Provider = AIProviderType.OpenAI, Enabled = true }],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = null, // Not configured
                    Model = null! // Intentionally null to test JSONB null handling
                }
            }
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Features[AIFeatureType.SpamDetection].ConnectionId, Is.Null);
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_WithSpecialCharactersInEndpoint_ShouldPreserve()
    {
        // Arrange - Endpoint with special characters (like query params)
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = "custom",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = true,
                    LocalEndpoint = "http://localhost:8080/v1?api_version=2024-01-01&timeout=30"
                }
            ]
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved!.Connections[0].LocalEndpoint,
            Is.EqualTo("http://localhost:8080/v1?api_version=2024-01-01&timeout=30"));
    }

    [Test]
    public async Task SaveAIProviderConfigAsync_WithEmptyConnections_ShouldPreserveEmptyList()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = [], // No connections
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>()
        };

        // Act
        await _configRepo!.SaveAIProviderConfigAsync(config);
        var retrieved = await _configRepo.GetAIProviderConfigAsync();

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Connections, Is.Empty);
    }

    #endregion
}
