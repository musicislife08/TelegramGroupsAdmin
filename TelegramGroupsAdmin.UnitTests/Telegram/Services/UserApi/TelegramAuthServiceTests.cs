using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
/// - IAuthFlowStore is mocked to control flow existence without timing hacks.
/// - All external dependencies mocked with NSubstitute.
/// - IServiceScopeFactory chain configured via SetupScope() helper.
/// - Auth flow background tasks (LoginUserIfNeeded) are not exercised in unit tests because
///   they require real WTelegram connection negotiation. Tests focus on pre-flow checks
///   (credentials gate, duplicate detection) and post-flow operations (status, disconnect).
/// </summary>
[TestFixture]
public class TelegramAuthServiceTests
{
    private string _testWebUserId = null!;

    private IServiceScopeFactory _mockScopeFactory = null!;
    private IWTelegramClientFactory _mockClientFactory = null!;
    private IAuthFlowStore _mockFlowStore = null!;
    private ILogger<TelegramAuthService> _mockLogger = null!;
    private ITelegramSessionRepository _mockSessionRepo = null!;
    private ISystemConfigRepository _mockConfigRepo = null!;
    private IAuditService _mockAuditService = null!;
    private TelegramAuthService _sut = null!;
    private Actor _testExecutor = null!;

    [SetUp]
    public void SetUp()
    {
        _testWebUserId = Guid.NewGuid().ToString();
        _testExecutor = Actor.FromWebUser(_testWebUserId, "test@example.com");

        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockClientFactory = Substitute.For<IWTelegramClientFactory>();
        _mockFlowStore = Substitute.For<IAuthFlowStore>();
        _mockLogger = Substitute.For<ILogger<TelegramAuthService>>();
        _mockSessionRepo = Substitute.For<ITelegramSessionRepository>();
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockAuditService = Substitute.For<IAuditService>();

        // Default: TryAdd succeeds, TryGetValue returns false (no existing flow)
        _mockFlowStore.TryAdd(Arg.Any<string>(), Arg.Any<AuthFlowContext>()).Returns(true);
        _mockFlowStore.TryGetValue(Arg.Any<string>(), out Arg.Any<AuthFlowContext?>()).Returns(false);
        _mockFlowStore.TryRemove(Arg.Any<string>(), out Arg.Any<AuthFlowContext?>()).Returns(false);

        _sut = new TelegramAuthService(_mockScopeFactory, _mockClientFactory, _mockFlowStore, _mockLogger);
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
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 0 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        var result = await _sut.StartAuthAsync(_testWebUserId, "+1234567890", _testExecutor, CancellationToken.None);

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("API credentials must be configured"));
    }

    [Test]
    public async Task StartAuthAsync_AlreadyInProgress_ReturnsFailed()
    {
        // Arrange — flow store rejects TryAdd (flow already exists for this user)
        SetupScope();
        _mockConfigRepo.GetUserApiConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserApiConfig { ApiId = 12345 });
        _mockConfigRepo.GetUserApiHashAsync(Arg.Any<CancellationToken>())
            .Returns("test-api-hash");

        _mockFlowStore.TryAdd(Arg.Any<string>(), Arg.Any<AuthFlowContext>()).Returns(false);

        // Act — single synchronous call, no timing needed
        var result = await _sut.StartAuthAsync(_testWebUserId, "+1234567890", _testExecutor, CancellationToken.None);

        // Assert
        Assert.That(result.Step, Is.EqualTo(AuthStep.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("already in progress"));
    }

    #endregion

    #region CancelAuthAsync

    [Test]
    public async Task CancelAuthAsync_RemovesActiveFlow()
    {
        // Arrange — flow store has an active flow that will be removed
        var mockClient = Substitute.For<IWTelegramApiClient>();
        _mockFlowStore.TryRemove(_testWebUserId, out Arg.Any<AuthFlowContext?>())
            .Returns(x =>
            {
                // Simulate having a context to dispose
                x[1] = new AuthFlowContext
                {
                    Client = mockClient,
                    PhoneNumber = "+1234567890",
                    Executor = _testExecutor
                };
                return true;
            });

        // Act
        await _sut.CancelAuthAsync(_testWebUserId);

        // Assert — TryRemove was called and client was disposed
        _mockFlowStore.Received(1).TryRemove(_testWebUserId, out Arg.Any<AuthFlowContext?>());
        await mockClient.Received(1).DisposeAsync();
    }

    [Test]
    public async Task CancelAuthAsync_NoFlow_DoesNothing()
    {
        // Arrange — no flow exists
        _mockFlowStore.TryRemove(_testWebUserId, out Arg.Any<AuthFlowContext?>()).Returns(false);

        // Act
        await _sut.CancelAuthAsync(_testWebUserId);

        // Assert — TryRemove was called but nothing else happened
        _mockFlowStore.Received(1).TryRemove(_testWebUserId, out Arg.Any<AuthFlowContext?>());
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
