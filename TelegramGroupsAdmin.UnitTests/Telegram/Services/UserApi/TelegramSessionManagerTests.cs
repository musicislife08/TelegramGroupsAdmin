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

        _sut = new TelegramSessionManager(_mockScopeFactory, _mockClientFactory, _mockLogger);
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
    public async Task GetClientAsync_CachedClient_Disconnected_CleansUpAndReturnsNull()
    {
        // Arrange — seed the cache with a client that will become disconnected
        SetupScope();
        var session = MakeSession();

        // First call returns session (for seeding), subsequent calls return null (session deactivated)
        var sessionCallCount = 0;
        _mockSessionRepo.GetActiveSessionAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionCallCount++;
                return sessionCallCount == 1 ? session : null;
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

        // Seed cache
        await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Now flip the Disconnected flag — next access will detect it
        mockApiClient.Disconnected.Returns(true);

        // Act — second access, client is now disconnected
        var result = await _sut.GetClientAsync(TestWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);

        // The session should be deactivated as a revoked session
        await _mockSessionRepo.Received(1).DeactivateSessionAsync(TestSessionId, Arg.Any<CancellationToken>());
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Arg.Any<Actor>(),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
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
            Arg.Is<Actor>(a => a.Type == ActorType.WebUser && a.WebUserId == TestWebUserId),
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
