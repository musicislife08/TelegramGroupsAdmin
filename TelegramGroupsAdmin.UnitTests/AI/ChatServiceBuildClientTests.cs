using System.Reflection;
using Microsoft.Extensions.AI;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.UnitTests.AI;

/// <summary>
/// Tests for ChatService.BuildClient - the private provider-switch that constructs an
/// IChatClient per provider. Covers the validation guards (which throw) and that each
/// provider branch constructs a client. The happy-path client behavior is not asserted
/// here: construction is offline, but exercising the built client would require a network
/// call to the provider.
/// </summary>
[TestFixture]
public class ChatServiceBuildClientTests
{
    private static IChatClient InvokeBuildClient(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        var method = typeof(ChatService).GetMethod("BuildClient", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (IChatClient)method.Invoke(null, [connection, featureConfig, apiKey])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException; // unwrap so tests can assert the real exception type
        }
    }

    private static AIConnection Conn(AIProviderType provider, string? azureEndpoint = null, string? localEndpoint = null) =>
        new() { Id = "c", Provider = provider, Enabled = true, AzureEndpoint = azureEndpoint, LocalEndpoint = localEndpoint };

    private static AIFeatureConfig Feat(string model = "gpt-4o", string? azureDeployment = null) =>
        new() { ConnectionId = "c", Model = model, AzureDeploymentName = azureDeployment };

    // --- Guard branches: each missing-credential/config path throws ---

    [Test]
    public void BuildClient_OpenAI_WithoutApiKey_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn(AIProviderType.OpenAI), Feat(), null),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_AzureOpenAI_WithoutApiKey_Throws()
    {
        Assert.That(() => InvokeBuildClient(
                Conn(AIProviderType.AzureOpenAI, azureEndpoint: "https://r.openai.azure.com"), Feat(azureDeployment: "d"), null),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_AzureOpenAI_WithoutEndpoint_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn(AIProviderType.AzureOpenAI), Feat(azureDeployment: "d"), "key"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_AzureOpenAI_WithoutDeploymentName_Throws()
    {
        Assert.That(() => InvokeBuildClient(
                Conn(AIProviderType.AzureOpenAI, azureEndpoint: "https://r.openai.azure.com"), Feat(), "key"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_OpenAICompatible_WithoutEndpoint_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn(AIProviderType.OpenAICompatible), Feat(), "key"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_OpenRouter_WithoutApiKey_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn(AIProviderType.OpenRouter), Feat(), null),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_Anthropic_WithoutApiKey_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn(AIProviderType.Anthropic), Feat(), null),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildClient_UnsupportedProvider_Throws()
    {
        Assert.That(() => InvokeBuildClient(Conn((AIProviderType)999), Feat(), "key"),
            Throws.TypeOf<InvalidOperationException>());
    }

    // --- Success branches: valid config constructs a client (offline) ---

    [Test]
    public void BuildClient_OpenAI_WithApiKey_ReturnsClient()
    {
        Assert.That(InvokeBuildClient(Conn(AIProviderType.OpenAI), Feat(), "sk-test"), Is.Not.Null);
    }

    [Test]
    public void BuildClient_OpenAICompatible_WithoutApiKey_UsesPlaceholderAndReturnsClient()
    {
        Assert.That(InvokeBuildClient(
                Conn(AIProviderType.OpenAICompatible, localEndpoint: "http://localhost:11434/v1"), Feat(), null),
            Is.Not.Null);
    }

    [Test]
    public void BuildClient_OpenRouter_WithApiKey_DefaultEndpoint_ReturnsClient()
    {
        Assert.That(InvokeBuildClient(Conn(AIProviderType.OpenRouter), Feat(), "sk-or"), Is.Not.Null);
    }

    [Test]
    public void BuildClient_OpenRouter_WithApiKey_CustomEndpoint_ReturnsClient()
    {
        Assert.That(InvokeBuildClient(
                Conn(AIProviderType.OpenRouter, localEndpoint: "https://proxy.example.com/v1"), Feat(), "sk-or"),
            Is.Not.Null);
    }

    [Test]
    public void BuildClient_Anthropic_WithApiKey_ReturnsClient()
    {
        Assert.That(InvokeBuildClient(Conn(AIProviderType.Anthropic), Feat(model: "claude-opus-4"), "sk-ant"),
            Is.Not.Null);
    }
}
