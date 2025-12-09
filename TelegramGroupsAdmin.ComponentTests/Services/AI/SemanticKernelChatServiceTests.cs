using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ComponentTests.Services.AI;

/// <summary>
/// Unit tests for SemanticKernelChatService
/// Focus: Test OUR logic for constructor validation and CreateResult method
/// </summary>
[TestFixture]
public class SemanticKernelChatServiceTests
{
    #region Constructor Validation Tests

    [Test]
    public void Constructor_OpenAIProviderWithoutApiKey_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-openai",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-openai",
            Model = "gpt-4o-mini",
            MaxTokens = 500,
            Temperature = 0.2
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, null, logger));

        Assert.That(ex!.Message, Does.Contain("OpenAI API key is required"));
    }

    [Test]
    public void Constructor_OpenAIProviderWithEmptyApiKey_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-openai",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-openai",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, "", logger));

        Assert.That(ex!.Message, Does.Contain("OpenAI API key is required"));
    }

    [Test]
    public void Constructor_AzureProviderWithoutApiKey_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-azure",
            Provider = AIProviderType.AzureOpenAI,
            Enabled = true,
            AzureEndpoint = "https://test.openai.azure.com",
            AzureApiVersion = "2024-10-21"
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-azure",
            Model = "gpt-4o-mini",
            AzureDeploymentName = "gpt-4o-mini-deployment"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, null, logger));

        Assert.That(ex!.Message, Does.Contain("Azure OpenAI API key is required"));
    }

    [Test]
    public void Constructor_AzureProviderWithoutEndpoint_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-azure",
            Provider = AIProviderType.AzureOpenAI,
            Enabled = true,
            AzureEndpoint = null,
            AzureApiVersion = "2024-10-21"
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-azure",
            Model = "gpt-4o-mini",
            AzureDeploymentName = "gpt-4o-mini-deployment"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, "test-api-key", logger));

        Assert.That(ex!.Message, Does.Contain("Azure endpoint is required"));
    }

    [Test]
    public void Constructor_AzureProviderWithoutDeploymentName_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-azure",
            Provider = AIProviderType.AzureOpenAI,
            Enabled = true,
            AzureEndpoint = "https://test.openai.azure.com",
            AzureApiVersion = "2024-10-21"
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-azure",
            Model = "gpt-4o-mini",
            AzureDeploymentName = null
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, "test-api-key", logger));

        Assert.That(ex!.Message, Does.Contain("Azure deployment name is required"));
    }

    [Test]
    public void Constructor_LocalOpenAIWithoutEndpoint_ThrowsArgumentException()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-local",
            Provider = AIProviderType.LocalOpenAI,
            Enabled = true,
            LocalEndpoint = null
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-local",
            Model = "llama3.2"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SemanticKernelChatService(connection, featureConfig, null, logger));

        Assert.That(ex!.Message, Does.Contain("Local endpoint is required"));
    }

    [Test]
    public void Constructor_ValidOpenAIConfig_CreatesSuccessfully()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-openai",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-openai",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
            new SemanticKernelChatService(connection, featureConfig, "test-api-key", logger));
    }

    [Test]
    public void Constructor_ValidAzureConfig_CreatesSuccessfully()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-azure",
            Provider = AIProviderType.AzureOpenAI,
            Enabled = true,
            AzureEndpoint = "https://test.openai.azure.com",
            AzureApiVersion = "2024-10-21"
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-azure",
            Model = "gpt-4o-mini",
            AzureDeploymentName = "gpt-4o-mini-deployment"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
            new SemanticKernelChatService(connection, featureConfig, "test-api-key", logger));
    }

    [Test]
    public void Constructor_ValidLocalOpenAIConfig_CreatesSuccessfully()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-local",
            Provider = AIProviderType.LocalOpenAI,
            Enabled = true,
            LocalEndpoint = "http://localhost:11434/v1",
            LocalRequiresApiKey = false
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-local",
            Model = "llama3.2"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
            new SemanticKernelChatService(connection, featureConfig, null, logger));
    }

    [Test]
    public void Constructor_LocalOpenAIWithKeylessProvider_CreatesSuccessfully()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-ollama",
            Provider = AIProviderType.LocalOpenAI,
            Enabled = true,
            LocalEndpoint = "http://localhost:11434/v1",
            LocalRequiresApiKey = false
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-ollama",
            Model = "llama3.2"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert - Should not throw even without API key
        Assert.DoesNotThrow(() =>
            new SemanticKernelChatService(connection, featureConfig, null, logger));
    }

    [Test]
    public void Constructor_LocalOpenAIRequiresKeyButHasKey_CreatesSuccessfully()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test-local-lmstudio",
            Provider = AIProviderType.LocalOpenAI,
            Enabled = true,
            LocalEndpoint = "http://localhost:1234/v1",
            LocalRequiresApiKey = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test-local-lmstudio",
            Model = "local-model"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();

        // Act & Assert - Should not throw with API key
        Assert.DoesNotThrow(() =>
            new SemanticKernelChatService(connection, featureConfig, "test-api-key", logger));
    }

    #endregion

    #region CreateResult Tests (requires making method internal)

    // NOTE: These tests require making CreateResult method internal with [InternalsVisibleTo]
    // They test the result creation logic without needing to mock Semantic Kernel

    [Test]
    public void CreateResult_EmptyContent_ReturnsNull()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();
        var service = new SemanticKernelChatService(connection, featureConfig, "test-key", logger);

        // Act - CreateResult is now internal, test with real ChatMessageContent
        var response = new ChatMessageContent(AuthorRole.Assistant, string.Empty);
        var result = service.CreateResult(response);

        // Assert
        Assert.That(result, Is.Null, "Empty content should return null");
    }

    [Test]
    public void CreateResult_NullContent_ReturnsNull()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();
        var service = new SemanticKernelChatService(connection, featureConfig, "test-key", logger);

        // Act - CreateResult is now internal, test with real ChatMessageContent
        var response = new ChatMessageContent(AuthorRole.Assistant, content: null);
        var result = service.CreateResult(response);

        // Assert
        Assert.That(result, Is.Null, "Null content should return null");
    }

    [Test]
    public void CreateResult_ValidContentWithNullMetadata_ReturnsResultWithNullTokenCounts()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();
        var service = new SemanticKernelChatService(connection, featureConfig, "test-key", logger);

        // Act - CreateResult is now internal, test with real ChatMessageContent (no metadata)
        var response = new ChatMessageContent(AuthorRole.Assistant, "Test response", "gpt-4o-mini");
        var result = service.CreateResult(response);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Content, Is.EqualTo("Test response"));
            Assert.That(result.Model, Is.EqualTo("gpt-4o-mini"));
            Assert.That(result.TotalTokens, Is.Null);
            Assert.That(result.PromptTokens, Is.Null);
            Assert.That(result.CompletionTokens, Is.Null);
        });
    }

    [Test]
    public void CreateResult_ValidContentWithNullModelId_UsesFallbackModelId()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();
        var service = new SemanticKernelChatService(connection, featureConfig, "test-key", logger);

        // Act - CreateResult is now internal, test with null modelId
        var response = new ChatMessageContent(AuthorRole.Assistant, "Test response", modelId: null);
        var result = service.CreateResult(response);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Model, Is.EqualTo("gpt-4o-mini"), "Should fall back to configured model");
        });
    }

    [Test]
    public void CreateResult_ValidContent_ReturnsResultWithContentPopulated()
    {
        // Arrange
        var connection = new AIConnection
        {
            Id = "test",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };

        var featureConfig = new AIFeatureConfig
        {
            ConnectionId = "test",
            Model = "gpt-4o-mini"
        };

        var logger = Substitute.For<ILogger<SemanticKernelChatService>>();
        var service = new SemanticKernelChatService(connection, featureConfig, "test-key", logger);

        // Act - CreateResult is now internal
        var response = new ChatMessageContent(AuthorRole.Assistant, "This is a valid response from the AI", "gpt-4o");
        var result = service.CreateResult(response);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Content, Is.EqualTo("This is a valid response from the AI"));
            Assert.That(result.Model, Is.EqualTo("gpt-4o"));
        });
    }

    #endregion
}
