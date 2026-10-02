using System.Collections;
using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.UnitTests.AI;

/// <summary>
/// Tests for the ChatOptions that ChatService hands to the provider client.
/// No request may carry a temperature or any other sampling parameter: current reasoning models
/// reject non-default values, so the field is omitted and the provider default applies.
/// A fake IChatClient is planted in the static client cache, so the real public methods run
/// end to end and the options they build are captured without a network call.
/// </summary>
[TestFixture]
public class ChatServiceRequestOptionsTests
{
    private const string ConnectionId = "options-test";
    private const string Model = "test-model";
    private const string LocalEndpoint = "http://localhost:11434/v1";
    private const int ConfiguredMaxTokens = 4321;

    private static readonly byte[] ImageBytes = [0x01, 0x02, 0x03];

    private IChatClient _fakeClient = null!;
    private ChatOptions? _capturedOptions;
    private int _callCount;
    private ChatService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _capturedOptions = null;
        _callCount = 0;

        _fakeClient = Substitute.For<IChatClient>();
        _fakeClient
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _capturedOptions = call.Arg<ChatOptions?>();
                _callCount++;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            });

        // OpenAI-compatible connection with no key requirement, so no key material is needed
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection
                {
                    Id = ConnectionId,
                    Provider = AIProviderType.OpenAICompatible,
                    Enabled = true,
                    LocalEndpoint = LocalEndpoint,
                    LocalRequiresApiKey = false
                }
            ],
            Features = new()
            {
                [AIFeatureType.SpamDetection] = new() { ConnectionId = ConnectionId, Model = Model, MaxTokens = ConfiguredMaxTokens },
                [AIFeatureType.ImageAnalysis] = new() { ConnectionId = ConnectionId, Model = Model, MaxTokens = ConfiguredMaxTokens, RequiresVision = true }
            }
        };

        var repository = Substitute.For<ISystemConfigRepository>();
        repository.GetAIProviderConfigAsync(Arg.Any<CancellationToken>()).Returns(config);
        repository.GetApiKeysAsync(Arg.Any<CancellationToken>()).Returns((ApiKeysConfig?)null);

        // Key shape: id|provider|model|azureDeployment|azureEndpoint|localEndpoint.
        // Feature calls and test calls with the same model resolve to this one entry.
        GetCache()[$"{ConnectionId}|{AIProviderType.OpenAICompatible}|{Model}|||{LocalEndpoint}"] = NewCachedClient(_fakeClient);

        _service = new ChatService(repository, NullLogger<ChatService>.Instance, new ApiMetrics(), new CacheMetrics());
    }

    [TearDown]
    public void TearDown()
    {
        // The cache is static, so a leftover entry would leak into other ChatService tests
        GetCache().Clear();
        _fakeClient.Dispose();
    }

    #region Real Path - Feature Calls

    [Test]
    public async Task GetCompletionAsync_WithoutCallerOptions_SendsConfiguredMaxTokensAndNoSamplingParameters()
    {
        var result = await _service.GetCompletionAsync(AIFeatureType.SpamDetection, "system", "user");

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        // Proves the options came from the stored feature config, not from an empty default
        Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(ConfiguredMaxTokens));
    }

    [Test]
    public async Task GetCompletionAsync_WithCallerOptions_SendsNoSamplingParameters()
    {
        var result = await _service.GetCompletionAsync(
            AIFeatureType.SpamDetection, "system", "user",
            new ChatCompletionOptions { MaxTokens = 123, JsonMode = true });

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(123));
            Assert.That(_capturedOptions.ResponseFormat, Is.SameAs(ChatResponseFormat.Json));
        }
    }

    [Test]
    public async Task GetVisionCompletionAsync_SingleImage_SendsNoSamplingParameters()
    {
        var result = await _service.GetVisionCompletionAsync(
            AIFeatureType.ImageAnalysis, "system", "user", ImageBytes, "image/png");

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(ConfiguredMaxTokens));
    }

    [Test]
    public async Task GetVisionCompletionAsync_MultipleImages_SendsNoSamplingParameters()
    {
        IReadOnlyList<ImageInput> images = [new(ImageBytes, "image/png"), new(ImageBytes, "image/jpeg")];

        var result = await _service.GetVisionCompletionAsync(
            AIFeatureType.ImageAnalysis, "system", "user", images);

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(ConfiguredMaxTokens));
    }

    #endregion

    #region Test Path - Feature Test Calls

    [Test]
    public async Task TestCompletionAsync_SendsNoSamplingParameters()
    {
        var result = await _service.TestCompletionAsync(
            ConnectionId, Model, azureDeploymentName: null, "system", "user",
            new ChatCompletionOptions { MaxTokens = 50 });

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(50));
    }

    [Test]
    public async Task TestVisionCompletionAsync_SendsNoSamplingParameters()
    {
        var result = await _service.TestVisionCompletionAsync(
            ConnectionId, Model, azureDeploymentName: null, "system", "user", ImageBytes, "image/png",
            new ChatCompletionOptions { MaxTokens = 50 });

        Assert.That(result, Is.Not.Null);
        AssertNoSamplingParameters();
        Assert.That(_capturedOptions!.MaxOutputTokens, Is.EqualTo(50));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Asserts the planted client was called exactly once and that the options it received
    /// leave every sampling parameter unset.
    /// </summary>
    private void AssertNoSamplingParameters()
    {
        Assert.That(_callCount, Is.EqualTo(1), "the planted client must have received the request");
        Assert.That(_capturedOptions, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_capturedOptions!.Temperature, Is.Null);
            Assert.That(_capturedOptions.TopP, Is.Null);
            Assert.That(_capturedOptions.TopK, Is.Null);
        }
    }

    private static IDictionary GetCache()
    {
        var field = typeof(ChatService).GetField("ClientCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IDictionary)field.GetValue(null)!;
    }

    private static object NewCachedClient(IChatClient client)
    {
        // CachedClient is a private nested record: CachedClient(IChatClient Client, string ModelId)
        var type = typeof(ChatService).GetNestedType("CachedClient", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, client, Model)!;
    }

    #endregion
}
