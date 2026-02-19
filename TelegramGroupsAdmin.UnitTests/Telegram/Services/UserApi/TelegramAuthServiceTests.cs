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
/// Unit tests for TelegramAuthService.
///
/// Testing strategy:
/// - TelegramAuthService.ActiveFlows is a static ConcurrentDictionary that persists across
///   scoped service instances. Tests use unique per-test user IDs and call CancelAuthAsync
///   in TearDown to prevent cross-test contamination.
/// - All external dependencies mocked with NSubstitute.
/// - IServiceScopeFactory chain configured via SetupScope() helper.
/// - Auth flow background tasks (LoginUserIfNeeded) are not exercised in unit tests because
///   they require real WTelegram connection negotiation. Tests focus on pre-flow checks
///   (credentials gate, duplicate detection) and post-flow operations (status, disconnect).
/// </summary>
[TestFixture]
public class TelegramAuthServiceTests
{
    // Use a per-test unique user ID to prevent static ActiveFlows contamination.
    // Each test gets its own ID derived from a Guid so parallel test runs don't collide.
    private string _testWebUserId = null!;

    private IServiceScopeFactory _mockScopeFactory = null!;
    private IWTelegramClientFactory _mockClientFactory = null!;
    private ILogger<TelegramAuthService> _mockLogger = null!;
    private ITelegramSessionRepository _mockSessionRepo = null!;
    private ISystemConfigRepository _mockConfigRepo = null!;
    private IAuditService _mockAuditService = null!;
    private TelegramAuthService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _testWebUserId = Guid.NewGuid().ToString();

        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockClientFactory = Substitute.For<IWTelegramClientFactory>();
        _mockLogger = Substitute.For<ILogger<TelegramAuthService>>();
        _mockSessionRepo = Substitute.For<ITelegramSessionRepository>();
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockAuditService = Substitute.For<IAuditService>();

        _sut = new TelegramAuthService(_mockScopeFactory, _mockClientFactory, _mockLogger);
    }

    [TearDown]
    public async Task TearDown()
    {
        // Clean up any auth flow left in the static dictionary to prevent test contamination.
        await _sut.CancelAuthAsync(_testWebUserId);
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

    #region StartAuthAsync

    [Test]
    public async Task StartAuthAsync_NoApiCredentials_ReturnsFailed()
    {
        // Arrange
        SetupScope();
        _mockConfigRepo.HasUserApiCredentialsAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.StartAuthAsync(_testWebUserId, "+1234567890", CancellationToken.None);

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("API credentials must be configured"));
    }

    [Test]
    public async Task StartAuthAsync_AlreadyInProgress_ReturnsFailed()
    {
        // Arrange — prime the static dictionary with a fake flow context by calling StartAuthAsync
        // with credentials that pass the gate but whose client blocks indefinitely on login.
        SetupScope();
        _mockConfigRepo.HasUserApiCredentialsAsync(Arg.Any<CancellationToken>()).Returns(true);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        // Client whose LoginUserIfNeeded never completes — keeps the flow alive
        var blockingClient = Substitute.For<IWTelegramApiClient>();
        var neverComplete = new TaskCompletionSource<TL.User>();
        blockingClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(neverComplete.Task);

        _mockClientFactory.Create(
                Arg.Any<Func<string, string?>>(),
                Arg.Any<byte[]>(),
                Arg.Any<Action<byte[]>>())
            .Returns(blockingClient);

        // First call starts the flow (will be in CodeSent step after the delay).
        // We use a short-circuit: StartAuthAsync takes real 2s delay internally.
        // To avoid slow tests, we instead directly verify the second call is rejected
        // by injecting a pre-existing flow via CancelAuthAsync NOT being called first.
        //
        // We simulate the second call to an already-in-progress flow by calling StartAuthAsync
        // a second time (the first call will linger due to the blocking client).
        // However, StartAuthAsync itself delays 2 seconds — so we test only the error path
        // by calling StartAuthAsync twice with credentials that fail the gate the second time.
        //
        // Alternative: set up two separate service instances sharing the same static dictionary.
        // Because ActiveFlows is static, a second TelegramAuthService instance shares the same dict.
        var secondSut = new TelegramAuthService(_mockScopeFactory, _mockClientFactory, _mockLogger);

        // Start first flow — credentials pass, client blocks; flow enters CodeSent
        // (we can't easily wait for CodeSent without the real 2s delay, so instead
        //  we inject into the dict directly through the second call path).
        //
        // The simplest approach: call StartAuthAsync once with no-credentials on secondSut
        // to confirm it returns Failed("already in progress") AFTER we manually add a flow.
        //
        // Because we can't directly insert into the private static dict, we rely on the fact that
        // two TelegramAuthService instances share the same static ConcurrentDictionary.
        // We configure sut1 to reach the TryAdd step and start background work, then immediately
        // call secondSut.StartAuthAsync to hit the ContainsKey guard.
        //
        // To avoid the 2s Task.Delay in the first call, wrap the first call without awaiting it
        // and cancel it quickly.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        // We don't await — let it run in background to insert into ActiveFlows
        var firstCall = _sut.StartAuthAsync(_testWebUserId, "+1234567890", cts.Token);

        // Give just enough time for the ContainsKey/TryAdd path to execute
        await Task.Delay(50);

        // Now call with the second service instance sharing the same static dict
        var result = await secondSut.StartAuthAsync(_testWebUserId, "+9999999999", CancellationToken.None);

        // Await the first call to avoid unobserved task exceptions
        try { await firstCall; } catch { /* expected: task cancelled or delay interrupted */ }

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("already in progress"));
    }

    #endregion

    #region CancelAuthAsync

    [Test]
    public async Task CancelAuthAsync_RemovesActiveFlow()
    {
        // Arrange — simulate an in-progress flow by starting one with blocking client
        SetupScope();
        _mockConfigRepo.HasUserApiCredentialsAsync(Arg.Any<CancellationToken>()).Returns(true);
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        var blockingClient = Substitute.For<IWTelegramApiClient>();
        var neverComplete = new TaskCompletionSource<TL.User>();
        blockingClient.LoginUserIfNeeded(Arg.Any<TL.CodeSettings?>(), Arg.Any<bool>())
            .Returns(neverComplete.Task);
        _mockClientFactory.Create(
                Arg.Any<Func<string, string?>>(),
                Arg.Any<byte[]>(),
                Arg.Any<Action<byte[]>>())
            .Returns(blockingClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var startTask = _sut.StartAuthAsync(_testWebUserId, "+1234567890", cts.Token);

        // Brief pause to let TryAdd execute
        await Task.Delay(20);

        // Act — cancel before TearDown to verify the explicit cancel path
        await _sut.CancelAuthAsync(_testWebUserId);

        try { await startTask; } catch { /* ignore */ }

        // Assert — a subsequent StartAuthAsync for the same user should not hit the "already in progress" guard.
        // We verify by checking that the dictionary no longer blocks a new flow.
        // Configure a second flow that will immediately fail on credentials.
        _mockConfigRepo.HasUserApiCredentialsAsync(Arg.Any<CancellationToken>()).Returns(false);
        var secondStart = await _sut.StartAuthAsync(_testWebUserId, "+1234567890", CancellationToken.None);

        // If CancelAuthAsync worked, the second call should return "No credentials" failure,
        // NOT "already in progress" failure.
        Assert.That(secondStart.ErrorMessage, Does.Not.Contain("already in progress"));
    }

    #endregion

    #region GetStatusAsync

    [Test]
    public async Task GetStatusAsync_NoSession_ReturnsNotConnected()
    {
        // Arrange
        SetupScope();
        _mockSessionRepo.GetActiveSessionAsync(_testWebUserId, Arg.Any<CancellationToken>())
            .Returns((TelegramSession?)null);

        // Act
        var result = await _sut.GetStatusAsync(_testWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result.IsConnected, Is.False);
        Assert.That(result.DisplayName, Is.Null);
        Assert.That(result.TelegramUserId, Is.Null);
        Assert.That(result.ConnectedAt, Is.Null);
    }

    [Test]
    public async Task GetStatusAsync_ActiveSession_ReturnsConnected()
    {
        // Arrange
        SetupScope();
        var connectedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var session = new TelegramSession
        {
            Id = 1L,
            WebUserId = _testWebUserId,
            TelegramUserId = 123456789L,
            DisplayName = "Alice Smith",
            SessionData = [],
            IsActive = true,
            ConnectedAt = connectedAt
        };
        _mockSessionRepo.GetActiveSessionAsync(_testWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);

        // Act
        var result = await _sut.GetStatusAsync(_testWebUserId, CancellationToken.None);

        // Assert
        Assert.That(result.IsConnected, Is.True);
        Assert.That(result.DisplayName, Is.EqualTo("Alice Smith"));
        Assert.That(result.TelegramUserId, Is.EqualTo(123456789L));
        Assert.That(result.ConnectedAt, Is.EqualTo(connectedAt));
    }

    #endregion

    #region DisconnectAsync

    [Test]
    public async Task DisconnectAsync_ActiveSession_DeactivatesAndAudits()
    {
        // Arrange
        SetupScope();
        var session = new TelegramSession
        {
            Id = 77L,
            WebUserId = _testWebUserId,
            TelegramUserId = 987654321L,
            DisplayName = "Bob Jones",
            SessionData = [],
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _mockSessionRepo.GetActiveSessionAsync(_testWebUserId, Arg.Any<CancellationToken>())
            .Returns(session);

        // Act
        await _sut.DisconnectAsync(_testWebUserId, CancellationToken.None);

        // Assert — session deactivated
        await _mockSessionRepo.Received(1).DeactivateSessionAsync(77L, Arg.Any<CancellationToken>());

        // Assert — audit event logged with correct actor type
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Arg.Is<Actor>(a => a.Type == ActorType.WebUser && a.WebUserId == _testWebUserId),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_NoSession_DoesNothing()
    {
        // Arrange
        SetupScope();
        _mockSessionRepo.GetActiveSessionAsync(_testWebUserId, Arg.Any<CancellationToken>())
            .Returns((TelegramSession?)null);

        // Act — should complete without throwing
        await _sut.DisconnectAsync(_testWebUserId, CancellationToken.None);

        // Assert — nothing deactivated or audited
        await _mockSessionRepo.DidNotReceive().DeactivateSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _mockAuditService.DidNotReceive().LogEventAsync(
            Arg.Any<AuditEventType>(),
            Arg.Any<Actor>(),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region SubmitCodeAsync

    [Test]
    public async Task SubmitCodeAsync_NoActiveFlow_ReturnsFailed()
    {
        // Arrange — no flow started for this user

        // Act
        var result = await _sut.SubmitCodeAsync(_testWebUserId, "12345", CancellationToken.None);

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("No authentication flow in progress"));
    }

    #endregion

    #region Submit2FAAsync

    [Test]
    public async Task Submit2FAAsync_NoActiveFlow_ReturnsFailed()
    {
        // Arrange — no flow started for this user

        // Act
        var result = await _sut.Submit2FAAsync(_testWebUserId, "my-password", CancellationToken.None);

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("No authentication flow in progress"));
    }

    #endregion
}
