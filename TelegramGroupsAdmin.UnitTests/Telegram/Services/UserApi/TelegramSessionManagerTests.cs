using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TL;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Unit tests for TelegramSessionManager.
///
/// Testing strategy:
/// - All external dependencies are mocked with NSubstitute
/// - IServiceScopeFactory chain (factory -> scope -> provider) is configured via SetupScope()
/// - IWTelegramClientFactory mocked to avoid any real WTelegram connections
/// - IWTelegramApiClient's Disconnected property drives cache hit/miss behavior
/// - Static ConcurrentDictionary state is isolated per test because each test creates a fresh
///   TelegramSessionManager instance with its own _clients dictionary
/// </summary>
[TestFixture]
public class TelegramSessionManagerTests
{
    private const string TestWebUserId = "test-web-user-id";
    private const long TestSessionId = 42L;

    private IServiceScopeFactory _mockScopeFactory = null!;
    private IWTelegramClientFactory _mockClientFactory = null!;
    private ILogger<TelegramSessionManager> _mockLogger = null!;
    private ITelegramSessionRepository _mockSessionRepo = null!;
    private ISystemConfigRepository _mockConfigRepo = null!;
    private IAuditService _mockAuditService = null!;
    private ManualTimeProvider _timeProvider = null!;
    private TelegramSessionManager _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockClientFactory = Substitute.For<IWTelegramClientFactory>();
        _mockLogger = Substitute.For<ILogger<TelegramSessionManager>>();
        _mockSessionRepo = Substitute.For<ITelegramSessionRepository>();
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockAuditService = Substitute.For<IAuditService>();
        _timeProvider = new ManualTimeProvider();

        _sut = new TelegramSessionManager(_mockScopeFactory, _mockClientFactory, _timeProvider, _mockLogger);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _sut.DisposeAsync();
    }

    /// <summary>
    /// Configures the service scope chain so that CreateScope() returns a scope whose
    /// ServiceProvider resolves the standard repository mocks.
    /// </summary>
    private void SetupScope(Action<IServiceProvider>? configureProvider = null)
    {
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        var provider = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(provider);

        provider.GetService(typeof(ITelegramSessionRepository)).Returns(_mockSessionRepo);
        provider.GetService(typeof(ISystemConfigRepository)).Returns(_mockConfigRepo);
        provider.GetService(typeof(IAuditService)).Returns(_mockAuditService);

        configureProvider?.Invoke(provider);

        _mockScopeFactory.CreateScope().Returns(scope);
    }

    #region GetClientAsync — no cached client

    [Test]
    public async Task GetClientAsync_NoCachedClient_NoActiveSession_ReturnsNull()
    {
        // Arrange
        SetupScope();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((TelegramSession?)null);

        // Act
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetClientAsync_NoCachedClient_ActiveSession_CreatesAndCachesClient()
    {
        // Arrange — two scopes will be created: one for TryReconnectAsync, one for UpdateLastUsedAsync
        // We configure the scope factory to always return a fresh scope with the right services.
        var scopeCallCount = 0;
        _mockScopeFactory.CreateScope().Returns(_ =>
        {
            scopeCallCount++;
            var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
            var provider = Substitute.For<IServiceProvider>();
            scope.ServiceProvider.Returns(provider);
            provider.GetService(typeof(ITelegramSessionRepository)).Returns(_mockSessionRepo);
            provider.GetService(typeof(ISystemConfigRepository)).Returns(_mockConfigRepo);
            provider.GetService(typeof(IAuditService)).Returns(_mockAuditService);
            return scope;
        });

        var session = new TelegramSession
        {
            Id = TestSessionId,
            WebUserId = TestWebUserId,
            TelegramUserId = 9876543210L,
            DisplayName = "Test User",
            SessionData = [0x01, 0x02, 0x03],
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));

        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        // Act
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(mockApiClient));

        // Verify the client was connected and session was updated
        await mockApiClient.Received(1).LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>());
        await mockApiClient.Received(1).WarmPeerCacheAsync();
        await _mockSessionRepo.Received(1).UpdateLastUsedAsync(TestSessionId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetClientAsync — cached client

    [Test]
    public async Task GetClientAsync_CachedClient_NotDisconnected_ReturnsCachedClient()
    {
        // Arrange — seed the cache by creating a connected session first
        SetupScope();
        var session = MakeSession();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));

        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        // Seed the cache
        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Reset call counts before the second call
        _mockSessionRepo.ClearReceivedCalls();
        _mockClientFactory.ClearReceivedCalls();

        // Act — second call should hit cache
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.SameAs(mockApiClient));

        // Factory should not have been called again (client came from cache)
        _mockClientFactory.DidNotReceive().Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>());

        // UpdateLastUsedAsync should be called on the cached session
        await _mockSessionRepo.Received(1).UpdateLastUsedAsync(TestSessionId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetClientAsync_CachedClient_Disconnected_ReconnectsFromStoredSession()
    {
        // Arrange — a dropped connection is stale, not revoked (#577)
        SetupFreshScopes();
        SetupActiveSession();
        var staleClient = MakeConnectedClient();
        var freshClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, freshClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.SameAs(freshClient));
        await staleClient.Received(1).DisposeAsync();
        await _mockSessionRepo.DidNotReceive().DeactivateSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await AssertNoDisconnectAuditAsync();
    }

    [Test]
    public async Task GetClientAsync_CachedClient_Disconnected_ReconnectRevoked_DeactivatesAndAudits()
    {
        // Arrange — the reconnect hits a genuine revocation error
        SetupFreshScopes();
        SetupActiveSession();
        var staleClient = MakeConnectedClient();
        var revokedClient = Substitute.For<IWTelegramApiClient>();
        revokedClient.LoginUserIfNeeded(Arg.Any<CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromException<User>(new RpcException(401, "AUTH_KEY_UNREGISTERED")));
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, revokedClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
        await staleClient.Received(1).DisposeAsync();
        await revokedClient.Received(1).DisposeAsync();
        await _mockSessionRepo.Received(1).DeactivateSessionAsync(TestSessionId, Arg.Any<CancellationToken>());
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Arg.Any<Actor>(),
            Arg.Any<Actor?>(),
            Arg.Is<string?>(v => v!.Contains("AUTH_KEY_UNREGISTERED")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetClientAsync_CachedClient_Disconnected_ReconnectNetworkFailure_ReturnsNullAndKeepsSession()
    {
        // Arrange — the network is down, so the reconnect fails with a socket error
        SetupFreshScopes();
        SetupActiveSession();
        var staleClient = MakeConnectedClient();
        var failingClient = MakeNetworkFailingClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, failingClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
        Assert.That(_sut.ActiveClientCount, Is.Zero, "the stale client must not stay cached");
        await staleClient.Received(1).DisposeAsync();
        await failingClient.Received(1).DisposeAsync();
        await _mockSessionRepo.DidNotReceive().DeactivateSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await AssertNoDisconnectAuditAsync();
    }

    [Test]
    public async Task GetClientAsync_NetworkOutage_SessionStaysActiveAndRecoversWhenNetworkReturns()
    {
        // Arrange — a ~5 minute outage: several reconnects fail with socket errors, then the network returns
        SetupFreshScopes();
        SetupActiveSession();
        var staleClient = MakeConnectedClient();
        var outageClients = Enumerable.Range(0, 4).Select(_ => MakeNetworkFailingClient()).ToArray();
        var recoveredClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, [.. outageClients, recoveredClient]);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act — callers keep asking through the outage, spaced past the reconnect backoff
        foreach (var _ in outageClients)
        {
            var duringOutage = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
            Assert.That(duringOutage, Is.Null);
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }

        var afterOutage = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(afterOutage, Is.SameAs(recoveredClient));
        foreach (var client in outageClients)
            await client.Received(1).DisposeAsync();
        await _mockSessionRepo.DidNotReceive().DeactivateSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await AssertNoDisconnectAuditAsync();
    }

    [Test]
    public async Task GetClientAsync_AfterFailedReconnect_SkipsAttemptsInsideBackoffAndRetriesAfter()
    {
        // Arrange — one failed reconnect starts the backoff window
        SetupFreshScopes();
        SetupActiveSession();
        var failingClient = MakeNetworkFailingClient();
        var recoveredClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(failingClient, recoveredClient);

        Assert.That(await _sut.GetClientAsync(TestWebUserId, CancellationToken.None), Is.Null);
        _mockClientFactory.ClearReceivedCalls();

        // Act + Assert — inside the window no new connection is attempted
        _timeProvider.Advance(TelegramSessionManager.ReconnectBackoff - TimeSpan.FromSeconds(1));
        Assert.That(await _sut.GetClientAsync(TestWebUserId, CancellationToken.None), Is.Null);
        _mockClientFactory.DidNotReceive().Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>());

        // Act + Assert — once the window has passed, the next call reconnects
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.That(await _sut.GetClientAsync(TestWebUserId, CancellationToken.None), Is.SameAs(recoveredClient));
        _mockClientFactory.Received(1).Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>());
    }

    [Test]
    public async Task GetAnyClientAsync_CachedClientDisconnected_DisposesStaleClientAndReconnects()
    {
        // Arrange
        SetupFreshScopes();
        var session = SetupActiveSession();
        _mockSessionRepo.GetAllActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns([session]);
        var staleClient = MakeConnectedClient();
        var freshClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, freshClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act
        var result = await _sut.GetAnyClientAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.SameAs(freshClient));
        await staleClient.Received(1).DisposeAsync();
        Assert.That(_sut.ActiveClientCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetClientForChatAsync_CachedClientDisconnected_DisposesStaleClientAndReconnects()
    {
        // Arrange
        const long chatId = -1001234567890L;
        SetupFreshScopes();
        var session = SetupActiveSession();
        _mockSessionRepo.GetAllActiveSessionsAsync(Arg.Any<CancellationToken>(), Arg.Any<long?>()).Returns([session]);
        var staleClient = MakeConnectedClient();
        var freshClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(staleClient, freshClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);

        // Act
        var result = await _sut.GetClientForChatAsync(chatId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.SameAs(freshClient));
        await staleClient.Received(1).DisposeAsync();
        Assert.That(_sut.ActiveClientCount, Is.EqualTo(1));
    }

    #endregion

    #region HasAnyActiveSessionAsync

    [Test]
    public async Task HasAnyActiveSessionAsync_CachedClientsExist_ReturnsTrue()
    {
        // Arrange — seed cache so _clients is non-empty
        SetupScope();
        var session = MakeSession();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Act
        var result = await _sut.HasAnyActiveSessionAsync(CancellationToken.None);

        // Assert — should short-circuit on non-empty cache without hitting DB
        Assert.That(result, Is.True);
        await _mockSessionRepo.DidNotReceive().AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HasAnyActiveSessionAsync_CachedClientDisconnected_StillReportsStoredSession()
    {
        // Arrange — a dropped connection is not a lost session: the DB fallback still sees it
        SetupFreshScopes();
        SetupActiveSession();
        var staleClient = MakeConnectedClient();
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>()).Returns(staleClient);
        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        staleClient.Disconnected.Returns(true);
        _mockSessionRepo.AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.HasAnyActiveSessionAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.True);
        await _mockSessionRepo.Received(1).AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HasAnyActiveSessionAsync_NoCachedClients_ChecksDatabase()
    {
        // Arrange — empty cache, DB has active sessions
        SetupScope();
        _mockSessionRepo.AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.HasAnyActiveSessionAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.True);
        await _mockSessionRepo.Received(1).AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HasAnyActiveSessionAsync_NoCachedClients_NoDbSessions_ReturnsFalse()
    {
        // Arrange — empty cache, no DB sessions
        SetupScope();
        _mockSessionRepo.AnyActiveSessionExistsAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.HasAnyActiveSessionAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetAnyClientAsync

    [Test]
    public async Task GetAnyClientAsync_CachedClientAvailable_ReturnsCachedClient()
    {
        // Arrange — seed cache
        SetupScope();
        var session = MakeSession();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Act
        var result = await _sut.GetAnyClientAsync(CancellationToken.None);

        // Assert — returns from cache without touching DB
        Assert.That(result, Is.SameAs(mockApiClient));
        await _mockSessionRepo.DidNotReceive().GetAllActiveSessionsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAnyClientAsync_NoCachedClients_ReconnectsFromDatabase()
    {
        // Arrange — no cached clients, but DB has an active session
        SetupScope();
        var session = MakeSession();
        _mockSessionRepo.GetAllActiveSessionsAsync(Arg.Any<CancellationToken>())
            .Returns([session]);
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        // Act
        var result = await _sut.GetAnyClientAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        await _mockSessionRepo.Received(1).GetAllActiveSessionsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAnyClientAsync_NoCachedClients_NoDbSessions_ReturnsNull()
    {
        // Arrange
        SetupScope();
        _mockSessionRepo.GetAllActiveSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TelegramSession>());

        // Act
        var result = await _sut.GetAnyClientAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region DisconnectAsync

    [Test]
    public async Task DisconnectAsync_ActiveSession_RemovesFromCacheAndDeactivatesSession()
    {
        // Arrange — seed cache
        SetupScope();
        var session = MakeSession();

        // First call returns session (for seeding + disconnect lookup), after that null (deactivated)
        var sessionCallCount = 0;
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionCallCount++;
                return sessionCallCount <= 2 ? session : null;
            });
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var mockApiClient = Substitute.For<IWTelegramApiClient>();
        mockApiClient.Disconnected.Returns(false);
        mockApiClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));
        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(mockApiClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Act
        var executor = Actor.FromWebUser(TestWebUserId, "test@example.com");
        await _sut.DisconnectAsync(TestWebUserId, executor, CancellationToken.None);

        // Assert
        await _mockSessionRepo.Received(1).DeactivateSessionAsync(TestSessionId, Arg.Any<CancellationToken>());
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Arg.Is<Actor>(a => a!.Type == ActorType.WebUser && a.WebUserId == TestWebUserId),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        // The cached client should be disposed
        await mockApiClient.Received(1).DisposeAsync();

        // Subsequent GetClientAsync should return null (session deactivated)
        var postDisconnectClient = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        Assert.That(postDisconnectClient, Is.Null);
    }

    [Test]
    public async Task DisconnectAsync_NoExistingSession_DoesNotDeactivateOrAudit()
    {
        // Arrange — nothing cached, no active session in DB
        SetupScope();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((TelegramSession?)null);

        // Act
        var executor = Actor.FromWebUser(TestWebUserId, "test@example.com");
        await _sut.DisconnectAsync(TestWebUserId, executor, CancellationToken.None);

        // Assert — no deactivation or audit log because there was nothing to disconnect
        await _mockSessionRepo.DidNotReceive().DeactivateSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _mockAuditService.DidNotReceive().LogEventAsync(
            Arg.Any<AuditEventType>(),
            Arg.Any<Actor>(),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region DisposeAsync

    [Test]
    public async Task DisposeAsync_DisposesAllCachedClients()
    {
        // Arrange — seed two separate cached sessions
        const string secondUserId = "second-web-user-id";
        const long secondSessionId = 99L;

        _mockScopeFactory.CreateScope().Returns(_ =>
        {
            var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
            var provider = Substitute.For<IServiceProvider>();
            scope.ServiceProvider.Returns(provider);
            provider.GetService(typeof(ITelegramSessionRepository)).Returns(_mockSessionRepo);
            provider.GetService(typeof(ISystemConfigRepository)).Returns(_mockConfigRepo);
            provider.GetService(typeof(IAuditService)).Returns(_mockAuditService);
            return scope;
        });

        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var firstSession = MakeSession(TestWebUserId, TestSessionId);
        var secondSession = MakeSession(secondUserId, secondSessionId);

        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(firstSession);
        _mockSessionRepo.GetActiveSessionAsync(secondUserId, Arg.Any<CancellationToken>())
            .Returns(secondSession);

        var firstClient = Substitute.For<IWTelegramApiClient>();
        firstClient.Disconnected.Returns(false);
        firstClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));

        var secondClient = Substitute.For<IWTelegramApiClient>();
        secondClient.Disconnected.Returns(false);
        secondClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new TL.User { id = 99999, first_name = "Test" }));

        _mockClientFactory.Create(Arg.Any<Func<string, string?>>(), Arg.Any<Stream>())
            .Returns(firstClient, secondClient);

        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);
        await _sut.GetClientAsync(secondUserId, CancellationToken.None);

        // Act
        await _sut.DisposeAsync();

        // Assert — both clients disposed
        await firstClient.Received(1).DisposeAsync();
        await secondClient.Received(1).DisposeAsync();
    }

    #endregion

    #region Helpers

    /// <summary>Every CreateScope() call gets a fresh scope resolving the standard repository mocks.</summary>
    private void SetupFreshScopes()
    {
        _mockScopeFactory.CreateScope().Returns(_ =>
        {
            var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
            var provider = Substitute.For<IServiceProvider>();
            scope.ServiceProvider.Returns(provider);
            provider.GetService(typeof(ITelegramSessionRepository)).Returns(_mockSessionRepo);
            provider.GetService(typeof(ISystemConfigRepository)).Returns(_mockConfigRepo);
            provider.GetService(typeof(IAuditService)).Returns(_mockAuditService);
            return scope;
        });
    }

    /// <summary>An active stored session for <see cref="TestWebUserId"/> plus a usable User API config.</summary>
    private TelegramSession SetupActiveSession()
    {
        var session = MakeSession();
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>()).Returns(session);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>()).Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>()).Returns("test-api-hash");
        return session;
    }

    private static IWTelegramApiClient MakeConnectedClient()
    {
        var client = Substitute.For<IWTelegramApiClient>();
        client.Disconnected.Returns(false);
        client.LoginUserIfNeeded(Arg.Any<CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new User { id = 99999, first_name = "Test" }));
        return client;
    }

    /// <summary>A client whose login fails the way it does with no network: a socket error, not an RPC error.</summary>
    private static IWTelegramApiClient MakeNetworkFailingClient()
    {
        var client = Substitute.For<IWTelegramApiClient>();
        client.LoginUserIfNeeded(Arg.Any<CodeSettings?>(), Arg.Any<bool>())
            .Returns(Task.FromException<User>(new SocketException((int)SocketError.NetworkUnreachable)));
        return client;
    }

    private Task AssertNoDisconnectAuditAsync() =>
        _mockAuditService.DidNotReceive().LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Arg.Any<Actor>(),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static TelegramSession MakeSession(string webUserId = TestWebUserId, long sessionId = TestSessionId)
        => new()
        {
            Id = sessionId,
            WebUserId = webUserId,
            TelegramUserId = 9876543210L,
            DisplayName = "Test User",
            SessionData = [0x01, 0x02, 0x03],
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow
        };

    #endregion
}
