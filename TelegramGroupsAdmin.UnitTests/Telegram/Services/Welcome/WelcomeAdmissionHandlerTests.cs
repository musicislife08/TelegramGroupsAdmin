using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Unit tests for WelcomeAdmissionHandler.TryAdmitUserAsync().
///
/// Strategy: The handler uses IServiceScopeFactory to resolve scoped dependencies.
/// CreateAsyncScope() is an extension method that internally calls CreateScope().
/// We build a real ServiceCollection with mock registrations, then substitute
/// IServiceScopeFactory.CreateScope() to return the real scope.
/// This avoids trying to substitute AsyncServiceScope (a struct) directly.
/// </summary>
[TestFixture]
public class WelcomeAdmissionHandlerTests
{
    private const long TestUserId = 111_222_333L;
    private const long TestChatId = -100_987_654_321L;

    private IReportsRepository _reportsRepo = null!;
    private IWelcomeResponsesRepository _welcomeRepo = null!;
    private IBotModerationService _moderationService = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private WelcomeAdmissionHandler _handler = null!;

    private static readonly UserIdentity TestUser =
        new(TestUserId, "Alice", null, "alice_tg");

    private static readonly ChatIdentity TestChat =
        new(TestChatId, "Test Group");

    private static readonly Actor TestExecutor =
        Actor.FromSystem("WelcomeFlow");

    private const string TestReason = "Welcome gate cleared";

    [SetUp]
    public void SetUp()
    {
        _reportsRepo = Substitute.For<IReportsRepository>();
        _welcomeRepo = Substitute.For<IWelcomeResponsesRepository>();
        _moderationService = Substitute.For<IBotModerationService>();

        // Build a real service provider with mock registrations so that
        // GetRequiredService<T>() calls inside the handler resolve correctly.
        var services = new ServiceCollection();
        services.AddSingleton(_reportsRepo);
        services.AddSingleton(_welcomeRepo);
        services.AddSingleton(_moderationService);
        var serviceProvider = services.BuildServiceProvider();

        // Substitute only the factory — its CreateScope() returns the real scope.
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(serviceProvider.CreateScope());

        _handler = new WelcomeAdmissionHandler(
            _scopeFactory,
            NullLogger<WelcomeAdmissionHandler>.Instance);
    }

    #region Gate 1: Profile Scan

    [Test]
    public async Task TryAdmitUserAsync_ProfileScanGateBlocks_ReturnsStillWaiting()
    {
        // Arrange — Gate 1 is blocked; user has a pending profile scan alert
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(AdmissionResult.StillWaiting));

        // Gate 1 blocked early — welcome repo and moderation must NOT be called
        await _welcomeRepo.DidNotReceive().GetByUserAndChatAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());

        await _moderationService.DidNotReceive().RestoreUserPermissionsAsync(
            Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Gate 2: Welcome Response

    [Test]
    public async Task TryAdmitUserAsync_WelcomeGateBlocks_ReturnsStillWaiting()
    {
        // Arrange — Gate 1 passes, Gate 2 is blocked (welcome response is Pending)
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var pendingResponse = CreateWelcomeResponse(WelcomeResponseType.Pending);
        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(pendingResponse);

        // Act
        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(AdmissionResult.StillWaiting));

        // Gate 2 blocked — moderation must NOT be called
        await _moderationService.DidNotReceive().RestoreUserPermissionsAsync(
            Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region All Gates Clear

    [Test]
    public async Task TryAdmitUserAsync_AllGatesClear_WelcomeAccepted_ReturnsAdmitted()
    {
        // Arrange — Gate 1 passes, Gate 2 passes (welcome response is Accepted)
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var acceptedResponse = CreateWelcomeResponse(WelcomeResponseType.Accepted);
        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(acceptedResponse);

        _moderationService
            .RestoreUserPermissionsAsync(Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        // Act
        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(AdmissionResult.Admitted));

        await _moderationService.Received(1).RestoreUserPermissionsAsync(
            Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryAdmitUserAsync_AllGatesClear_NoWelcomeResponse_ReturnsAdmitted()
    {
        // Arrange — Gate 1 passes, Gate 2 passes (null = welcome disabled, gate is cleared)
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);

        _moderationService
            .RestoreUserPermissionsAsync(Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        // Act
        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(AdmissionResult.Admitted));

        await _moderationService.Received(1).RestoreUserPermissionsAsync(
            Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryAdmitUserAsync_AllGatesClear_PassesCorrectIntentToModerationService()
    {
        // Arrange — Capture the intent passed to RestoreUserPermissionsAsync
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);

        RestorePermissionsIntent? capturedIntent = null;
        _moderationService
            .RestoreUserPermissionsAsync(Arg.Do<RestorePermissionsIntent>(i => capturedIntent = i), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        // Act
        await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        // Assert — all intent fields must match what was passed in
        Assert.That(capturedIntent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedIntent!.User.Id, Is.EqualTo(TestUser.Id));
            Assert.That(capturedIntent.User.DisplayName, Is.EqualTo(TestUser.DisplayName));
            Assert.That(capturedIntent.Chat.Id, Is.EqualTo(TestChat.Id));
            Assert.That(capturedIntent.Executor, Is.EqualTo(TestExecutor));
            Assert.That(capturedIntent.Reason, Is.EqualTo(TestReason));
        }
    }

    #endregion

    #region Helpers

    private static WelcomeResponse CreateWelcomeResponse(WelcomeResponseType responseType) =>
        new(
            Id: 1L,
            ChatId: TestChatId,
            UserId: TestUserId,
            Username: "alice_tg",
            WelcomeMessageId: 42,
            Response: responseType,
            RespondedAt: DateTimeOffset.UtcNow,
            DmSent: false,
            DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            TimeoutJobId: null
        );

    #endregion
}
