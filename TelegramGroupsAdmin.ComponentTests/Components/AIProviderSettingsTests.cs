using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for AIProviderSettings.razor.
///
/// Primary purpose: guard the invariant that saving a connection invalidates the
/// ChatService client cache for that connection. This is load-bearing because the
/// API key is intentionally NOT part of the client cache key (see
/// ChatService.GenerateCacheKey) - a key rotation only takes effect because
/// SaveConnectionAsync calls ChatService.InvalidateCache(connection.Id). If that
/// call regresses, rotated keys would silently keep using the old cached client.
///
/// The delete path (DeleteConnectionAsync) is also InvalidateCache-gated, but it is
/// driven by DialogService.ShowMessageBoxAsync (an extension method that cannot be
/// substituted), so it is not covered here.
/// </summary>
[TestFixture]
public class AIProviderSettingsTests : MudBlazorTestContext
{
    private ISystemConfigRepository _configRepository = null!;
    private IChatService _chatService = null!;
    private IAIServiceFactory _serviceFactory = null!;
    private IAuditService _auditService = null!;

    [SetUp]
    public void SetUp()
    {
        _configRepository = Substitute.For<ISystemConfigRepository>();
        _chatService = Substitute.For<IChatService>();
        _serviceFactory = Substitute.For<IAIServiceFactory>();
        _auditService = Substitute.For<IAuditService>();

        Services.AddSingleton(_configRepository);
        Services.AddSingleton(_chatService);
        Services.AddSingleton(_serviceFactory);
        Services.AddSingleton(_auditService);

        this.AddTestWebUser();
    }

    private IRenderedComponent<AIProviderSettings> RenderWithConnection(AIConnection connection)
    {
        var config = new AIProviderConfig { Connections = [connection] };
        _configRepository.GetAIProviderConfigAsync(Arg.Any<CancellationToken>()).Returns(config);
        _configRepository.GetApiKeysAsync(Arg.Any<CancellationToken>()).Returns(new ApiKeysConfig());

        return Render<AIProviderSettings>();
    }

    [Test]
    public async Task SaveConnection_InvalidatesChatClientCacheForThatConnection()
    {
        // Arrange - settings page rendered with a single existing connection
        var connection = new AIConnection
        {
            Id = "rotate-me",
            Provider = AIProviderType.OpenAI,
            Enabled = true
        };
        var cut = RenderWithConnection(connection);

        // Act - the connection card raises OnSave (as it does on a key rotation),
        // which the parent wires to SaveConnectionAsync.
        var card = cut.FindComponent<AIConnectionCard>();
        await cut.InvokeAsync(() => card.Instance.OnSave.InvokeAsync((connection, "new-rotated-key")));

        // Assert - the cache for this connection must be invalidated so the new key takes effect.
        _chatService.Received(1).InvalidateCache("rotate-me");
    }
}
