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
        string model = "gpt-4o-mini",
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
        List<AIConnection> connections = [];

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
        List<AIConnection> connections = [];

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
        List<AIConnection> connections = [];

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
        List<AIConnection> connections = [];

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
        List<AIConnection> connections = [CreateConnection()];

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
        List<AIConnection> connections =
        [
            CreateConnection(id: "openai-prod", enabled: true),
            CreateConnection(id: "openai-dev", enabled: false) // disabled shouldn't show
        ];

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
        List<AIConnection> connections = [CreateConnection()];

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
        List<AIConnection> connections = [CreateConnection()];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [connection];

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
        List<AIConnection> connections = [CreateConnection()];

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

    #region Test Result Clearing Behavior (#137)

    [Test]
    public async Task SaveButton_DisabledInitially_BeforeTest()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        List<AIConnection> connections = [connection];

        // Act
        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Assert - Save button should be disabled (no test result yet)
        var saveButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Save"));
        Assert.That(saveButton, Is.Not.Null, "Save button should exist");
        Assert.That(saveButton!.HasAttribute("disabled"), Is.True, "Save button should be disabled before test");
    }

    [Test]
    public async Task SaveButton_EnabledAfterSuccessfulTest()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        List<AIConnection> connections = [connection];

        _mockTestService.TestFeatureAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(FeatureTestResult.Ok("Test passed"));

        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Act - Click Test button
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Test"));
        await cut.InvokeAsync(() => testButton.Click());

        // Assert - Save button should be enabled after successful test
        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Save"));
        Assert.That(saveButton.HasAttribute("disabled"), Is.False, "Save button should be enabled after successful test");
    }

    [Test]
    public async Task SaveButton_DisabledAfterFailedTest()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        List<AIConnection> connections = [connection];

        _mockTestService.TestFeatureAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(FeatureTestResult.Fail("Connection failed", "API error"));

        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Act - Click Test button (test will fail)
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Test"));
        await cut.InvokeAsync(() => testButton.Click());

        // Assert - Save button should still be disabled after failed test
        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Save"));
        Assert.That(saveButton.HasAttribute("disabled"), Is.True, "Save button should be disabled after failed test");
    }

    [Test]
    public async Task ShowsPassedChip_AfterSuccessfulTest()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        List<AIConnection> connections = [connection];

        _mockTestService.TestFeatureAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(FeatureTestResult.Ok("Test passed"));

        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Act - Click Test button
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Test"));
        await cut.InvokeAsync(() => testButton.Click());

        // Assert - Should show "Passed" chip
        Assert.That(cut.Markup, Does.Contain("Passed"));
    }

    [Test]
    public async Task ShowsFailedChip_AfterFailedTest()
    {
        // Arrange
        var connection = CreateConnection();
        var config = CreateFeatureConfig(connectionId: connection.Id, model: "gpt-4o");
        List<AIConnection> connections = [connection];

        _mockTestService.TestFeatureAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(FeatureTestResult.Fail("Connection failed"));

        var cut = Render<AIFeatureCard>(p => p
            .Add(x => x.FeatureType, AIFeatureType.SpamDetection)
            .Add(x => x.FeatureConfig, config)
            .Add(x => x.Connections, connections)
            .Add(x => x.TestService, _mockTestService));

        // Act - Click Test button
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Test"));
        await cut.InvokeAsync(() => testButton.Click());

        // Assert - Should show "Failed" chip
        Assert.That(cut.Markup, Does.Contain("Failed"));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasPaperContainer()
    {
        // Arrange
        var config = CreateFeatureConfig();
        List<AIConnection> connections = [];

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
        List<AIConnection> connections = [];

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
