using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.ContentDetection;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for AIFeatureCard.razor
/// Tests the AI feature configuration card with Test/Save workflow.
/// </summary>
[TestFixture]
public class AIFeatureCardTests : MudBlazorTestContext
{
    private IFeatureTestService _mockTestService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockTestService = Substitute.For<IFeatureTestService>();
    }

    /// <summary>
    /// Creates an AIFeatureConfig for testing.
    /// </summary>
    private static AIFeatureConfig CreateFeatureConfig(
        string? connectionId = null,
        string? model = null,
        string? azureDeploymentName = null,
        int maxTokens = 500,
        double temperature = 0.7)
    {
        var config = new AIFeatureConfig
        {
            ConnectionId = connectionId,
            Model = model,
            MaxTokens = maxTokens,
            Temperature = temperature
        };
        if (azureDeploymentName != null)
            config.AzureDeploymentName = azureDeploymentName;
        return config;
    }

    /// <summary>
    /// Creates an AIConnection for testing.
    /// </summary>
    private static AIConnection CreateConnection(
        string id = "test-connection",
        AIProviderType provider = AIProviderType.OpenAI,
        bool enabled = true)
    {
        return new AIConnection
        {
            Id = id,
            Provider = provider,
            Enabled = enabled,
            AvailableModels = [new AIModelInfo { Id = "gpt-4o-mini" }]
        };
    }

    #region Display Tests

    [Test]
    [TestCase(AIFeatureType.SpamDetection, "Spam Detection")]
    [TestCase(AIFeatureType.Translation, "Translation")]
    [TestCase(AIFeatureType.ImageAnalysis, "Image Analysis")]
    [TestCase(AIFeatureType.VideoAnalysis, "Video Analysis")]
    [TestCase(AIFeatureType.PromptBuilder, "Prompt Builder")]
    public void DisplaysFeatureName(AIFeatureType featureType, string expectedName)
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, featureType)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain(expectedName));
    }

    [Test]
    [TestCase(AIFeatureType.SpamDetection, "AI-powered text spam")]
    [TestCase(AIFeatureType.Translation, "translation")]
    [TestCase(AIFeatureType.ImageAnalysis, "Vision API")]
    public void DisplaysFeatureDescription(AIFeatureType featureType, string expectedText)
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, featureType)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain(expectedText));
    }

    [Test]
    [TestCase(AIFeatureType.ImageAnalysis)]
    [TestCase(AIFeatureType.VideoAnalysis)]
    public void ShowsVisionChip_ForVisionFeatures(AIFeatureType featureType)
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, featureType)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Vision"));
    }

    [Test]
    [TestCase(AIFeatureType.SpamDetection)]
    [TestCase(AIFeatureType.Translation)]
    [TestCase(AIFeatureType.PromptBuilder)]
    public void HidesVisionChip_ForNonVisionFeatures(AIFeatureType featureType)
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, featureType)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert - should not contain "Vision" chip (look for visibility icon which is unique to vision chip)
        Assert.That(cut.Markup, Does.Not.Contain("Visibility"));
    }

    #endregion

    #region Connection Selector Tests

    [Test]
    public void ShowsDisabledOption_InConnectionSelector()
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection> { CreateConnection() };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Disabled"));
    }

    [Test]
    public void ShowsConnectionSelector_WithHelperText()
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>
        {
            CreateConnection(id: "openai-prod", enabled: true),
            CreateConnection(id: "openai-dev", enabled: false) // disabled shouldn't show
        };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert - MudSelect dropdown exists with connection label
        // Note: MudSelect options render in a popover, not the main markup
        Assert.That(cut.Markup, Does.Contain("Connection"));
        Assert.That(cut.Markup, Does.Contain("Select an enabled connection"));
    }

    #endregion

    #region Disabled State Tests

    [Test]
    public void ShowsDisabledAlert_WhenNoConnectionSelected()
    {
        // Arrange
        var config = CreateFeatureConfig(connectionId: null);
        var connections = new List<AIConnection> { CreateConnection() };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Feature disabled"));
    }

    [Test]
    public void HidesModelSelection_WhenNoConnectionSelected()
    {
        // Arrange
        var config = CreateFeatureConfig(connectionId: null);
        var connections = new List<AIConnection> { CreateConnection() };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert - Model field should not be visible
        Assert.That(cut.Markup, Does.Not.Contain("Max Tokens"));
    }

    #endregion

    #region Model Selection Tests

    [Test]
    public void ShowsModelDropdown_WhenConnectionHasModels()
    {
        // Arrange
        var connection = CreateConnection();
        connection.AvailableModels = [
            new AIModelInfo { Id = "gpt-4o" },
            new AIModelInfo { Id = "gpt-4o-mini" }
        ];
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Model"));
    }

    [Test]
    public void ShowsAzureDeploymentField_ForAzureConnection()
    {
        // Arrange
        var connection = CreateConnection(provider: AIProviderType.AzureOpenAI);
        var config = CreateFeatureConfig(connectionId: connection.Id);
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Deployment Name"));
    }

    #endregion

    #region Parameter Fields Tests

    [Test]
    public void ShowsMaxTokensField_WhenConnectionSelected()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Max Tokens"));
    }

    [Test]
    public void ShowsTemperatureField_WhenConnectionSelected()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Temperature"));
    }

    #endregion

    #region Button Tests

    [Test]
    public void HasTestButton_WhenConnectionSelected()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Test"));
    }

    [Test]
    public void HasSaveButton_WhenConnectionSelected()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        var connections = new List<AIConnection> { connection };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Save"));
    }

    [Test]
    public void HidesButtons_WhenNoConnectionSelected()
    {
        // Arrange
        var config = CreateFeatureConfig(connectionId: null);
        var connections = new List<AIConnection> { CreateConnection() };

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert - Test/Save buttons should not be visible when disabled
        var buttons = cut.FindAll("button");
        Assert.That(buttons.Any(b => b.TextContent.Contains("Test")), Is.False);
        Assert.That(buttons.Any(b => b.TextContent.Contains("Save")), Is.False);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasPaperContainer()
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    [Test]
    public void HasDivider()
    {
        // Arrange
        var config = CreateFeatureConfig();
        var connections = new List<AIConnection>();

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-divider"));
    }

    #endregion
}
