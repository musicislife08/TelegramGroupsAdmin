using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for AIConnectionCard.razor
/// Tests the AI provider connection configuration card with Save/Delete actions.
/// </summary>
[TestFixture]
public class AIConnectionCardTests : MudBlazorTestContext
{
    /// <summary>
    /// Creates an AIConnection for testing.
    /// </summary>
    private static AIConnection CreateConnection(
        string id = "test-connection",
        AIProviderType provider = AIProviderType.OpenAI,
        bool enabled = true,
        string? azureEndpoint = null,
        string? azureApiVersion = null,
        string? localEndpoint = null,
        bool localRequiresApiKey = false)
    {
        return new AIConnection
        {
            Id = id,
            Provider = provider,
            Enabled = enabled,
            AzureEndpoint = azureEndpoint,
            AzureApiVersion = azureApiVersion,
            LocalEndpoint = localEndpoint,
            LocalRequiresApiKey = localRequiresApiKey
        };
    }

    #region Display Tests

    [Test]
    public void DisplaysConnectionId()
    {
        // Arrange
        var connection = CreateConnection(id: "my-openai");

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("my-openai"));
    }

    [Test]
    [TestCase(AIProviderType.OpenAI, "OpenAI")]
    [TestCase(AIProviderType.AzureOpenAI, "Azure OpenAI")]
    [TestCase(AIProviderType.LocalOpenAI, "Local")]
    public void DisplaysProviderName(AIProviderType provider, string expectedText)
    {
        // Arrange
        var connection = CreateConnection(provider: provider);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain(expectedText));
    }

    [Test]
    public void DisplaysEnabledStatus_WhenEnabled()
    {
        // Arrange
        var connection = CreateConnection(enabled: true);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - chip should show "Enabled"
        Assert.That(cut.Markup, Does.Contain("Enabled"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-success"));
    }

    [Test]
    public void DisplaysDisabledStatus_WhenDisabled()
    {
        // Arrange
        var connection = CreateConnection(enabled: false);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - chip should show "Disabled"
        Assert.That(cut.Markup, Does.Contain("Disabled"));
    }

    #endregion

    #region Provider-Specific Field Tests

    [Test]
    public void ShowsAzureFields_ForAzureProvider()
    {
        // Arrange
        var connection = CreateConnection(
            provider: AIProviderType.AzureOpenAI,
            azureEndpoint: "https://my-resource.openai.azure.com/",
            azureApiVersion: "2024-02-01");

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - Azure-specific fields should be visible
        Assert.That(cut.Markup, Does.Contain("Azure Endpoint"));
        Assert.That(cut.Markup, Does.Contain("API Version"));
    }

    [Test]
    public void HidesAzureFields_ForOpenAIProvider()
    {
        // Arrange
        var connection = CreateConnection(provider: AIProviderType.OpenAI);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - Azure-specific fields should NOT be visible
        Assert.That(cut.Markup, Does.Not.Contain("Azure Endpoint"));
        Assert.That(cut.Markup, Does.Not.Contain("API Version"));
    }

    [Test]
    public void ShowsLocalFields_ForLocalProvider()
    {
        // Arrange
        var connection = CreateConnection(
            provider: AIProviderType.LocalOpenAI,
            localEndpoint: "http://localhost:11434/v1");

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - Local-specific fields should be visible
        Assert.That(cut.Markup, Does.Contain("Endpoint URL"));
        Assert.That(cut.Markup, Does.Contain("Requires API Key"));
    }

    [Test]
    public void HidesLocalFields_ForOpenAIProvider()
    {
        // Arrange
        var connection = CreateConnection(provider: AIProviderType.OpenAI);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - Local-specific fields should NOT be visible
        Assert.That(cut.Markup, Does.Not.Contain("Requires API Key"));
    }

    #endregion

    #region API Key Field Tests

    [Test]
    public void DisplaysApiKeyField()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, "sk-test123"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("API Key"));
    }

    [Test]
    public void DisablesApiKeyField_WhenLocalAndNoKeyRequired()
    {
        // Arrange
        var connection = CreateConnection(
            provider: AIProviderType.LocalOpenAI,
            localRequiresApiKey: false);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - API key input should be disabled (MudBlazor applies mud-disabled class)
        Assert.That(cut.Markup, Does.Contain("mud-disabled"));
    }

    #endregion

    #region Status Chip Tests

    [Test]
    public void ShowsReadyStatus_WhenEnabledWithApiKey()
    {
        // Arrange
        var connection = CreateConnection(enabled: true);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, "sk-test123"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Ready"));
    }

    [Test]
    public void ShowsNeedsApiKeyStatus_WhenNoApiKey()
    {
        // Arrange
        var connection = CreateConnection(enabled: true);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Needs API Key"));
    }

    [Test]
    public void ShowsReadyStatus_ForLocalWithoutApiKey_WhenNotRequired()
    {
        // Arrange
        var connection = CreateConnection(
            provider: AIProviderType.LocalOpenAI,
            enabled: true,
            localRequiresApiKey: false);

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert - local without required key should still be "Ready"
        Assert.That(cut.Markup, Does.Contain("Ready"));
    }

    #endregion

    #region Button Tests

    [Test]
    public void HasSaveButton()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Save"));
    }

    [Test]
    public void HasDeleteButton()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Delete"));
    }

    #endregion

    #region Callback Tests

    [Test]
    public async Task InvokesOnSave_WhenSaveButtonClicked()
    {
        // Arrange
        (AIConnection Connection, string? ApiKey)? savedData = null;
        var connection = CreateConnection(id: "test-conn");

        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, "test-key")
            .Add(x => x.OnSave, EventCallback.Factory.Create<(AIConnection, string?)>(
                this, data => savedData = data)));

        // Act - find and click save button
        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Save"));
        await saveButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(savedData, Is.Not.Null);
        Assert.That(savedData!.Value.Connection.Id, Is.EqualTo("test-conn"));
    }

    [Test]
    public async Task InvokesOnDelete_WhenDeleteButtonClicked()
    {
        // Arrange
        AIConnection? deletedConnection = null;
        var connection = CreateConnection(id: "to-delete");

        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null)
            .Add(x => x.OnDelete, EventCallback.Factory.Create<AIConnection>(
                this, c => deletedConnection = c)));

        // Act - find and click delete button
        var deleteButton = cut.FindAll("button").First(b => b.TextContent.Contains("Delete"));
        await deleteButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(deletedConnection, Is.Not.Null);
        Assert.That(deletedConnection!.Id, Is.EqualTo("to-delete"));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasPaperContainer()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    [Test]
    public void HasDivider()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        var cut = Render<AIConnectionCard>(p => p
            .Add(x => x.Connection, connection)
            .Add(x => x.InitialApiKey, null));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-divider"));
    }

    #endregion
}
