using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Metrics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for ChatHealthRefreshOrchestrator.
/// Covers: CheckHealthAsync delegation, 3-strike MarkInactiveAsync rule,
/// per-chat failure counter isolation, and counter reset behavior.
/// </summary>
[TestFixture]
public class ChatHealthRefreshOrchestratorTests
{
    private const long ChatAId = 100L;
    private const long ChatBId = 200L;

    private ITelegramConfigLoader _configLoader = null!;
    private IChatCache _chatCache = null!;
    private IChatHealthCache _healthCache = null!;
    private IManagedChatsRepository _managedChatsRepository = null!;
    private ILinkedChannelsRepository _linkedChannelsRepository = null!;
    private IBotChatHandler _chatHandler = null!;
    private IBotUserHandler _userHandler = null!;
    private IBotUserService _userService = null!;
    private IBotChatService _chatService = null!;
    private TelegramPhotoService _photoService = null!;
    private IPhotoHashService _photoHashService = null!;
    private INotificationService _notificationService = null!;
    private ChatMetrics _chatMetrics = null!;
    private ILogger<ChatHealthRefreshOrchestrator> _logger = null!;

    private ChatHealthRefreshOrchestrator _orchestrator = null!;

    private static ChatIdentity ChatA => ChatIdentity.FromId(ChatAId);
    private static ChatIdentity ChatB => ChatIdentity.FromId(ChatBId);

    [SetUp]
    public void SetUp()
    {
        _configLoader = Substitute.For<ITelegramConfigLoader>();
        _chatCache = Substitute.For<IChatCache>();
        _healthCache = Substitute.For<IChatHealthCache>();
        _managedChatsRepository = Substitute.For<IManagedChatsRepository>();
        _linkedChannelsRepository = Substitute.For<ILinkedChannelsRepository>();
        _chatHandler = Substitute.For<IBotChatHandler>();
        _userHandler = Substitute.For<IBotUserHandler>();
        _userService = Substitute.For<IBotUserService>();
        _chatService = Substitute.For<IBotChatService>();
        _photoService = Substitute.ForPartsOf<TelegramPhotoService>(
            Substitute.For<ILogger<TelegramPhotoService>>(),
            Substitute.For<IBotMediaService>(),
            Substitute.For<IBotChatService>(),
            Microsoft.Extensions.Options.Options.Create(new AppOptions { DataPath = Path.GetTempPath() }));
        _photoHashService = Substitute.For<IPhotoHashService>();
        _notificationService = Substitute.For<INotificationService>();
        _chatMetrics = new ChatMetrics(_chatCache);
        _logger = Substitute.For<ILogger<ChatHealthRefreshOrchestrator>>();

        _orchestrator = new ChatHealthRefreshOrchestrator(
            _configLoader,
            _chatCache,
            _healthCache,
            _managedChatsRepository,
            _linkedChannelsRepository,
            _chatHandler,
            _userHandler,
            _userService,
            _chatService,
            _photoService,
            _photoHashService,
            _notificationService,
            _chatMetrics,
            _logger);
    }

    private static ChatFullInfo MakeSupergroup(long chatId) => new()
    {
        Id = chatId,
        Type = ChatType.Supergroup
    };

    private static ChatMemberMember MakeMember(long userId) => new()
    {
        User = new User { Id = userId }
    };

    #region CheckHealthAsync Delegation

    [Test]
    public async Task RefreshHealthForChatAsync_CallsCheckHealthAsync_ForReachabilityGate()
    {
        // Arrange: chat is reachable — CheckHealthAsync returns true
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(true);
        _healthCache.IncrementFailureCount(ChatAId).Returns(1);

        var sdkChat = MakeSupergroup(ChatAId);
        _chatHandler.GetChatAsync(ChatAId, Arg.Any<CancellationToken>()).Returns(sdkChat);
        _userService.GetBotIdAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _userHandler.GetChatMemberAsync(ChatAId, 999L, Arg.Any<CancellationToken>())
            .Returns(MakeMember(999L));

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: CheckHealthAsync was called as reachability gate
        await _chatService.Received(1).CheckHealthAsync(ChatA, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshHealthForChatAsync_WhenUnreachable_SetsErrorHealthInCache()
    {
        // Arrange: chat is unreachable
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _healthCache.IncrementFailureCount(ChatAId).Returns(1); // first failure

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: health cache was updated with error status for unreachable chat
        _healthCache.Received(1).SetHealth(ChatAId, Arg.Is<ChatHealthStatus>(h =>
            !h.IsReachable && h.Status == ChatHealthStatusType.Error));
    }

    #endregion

    #region 3-Strike MarkInactiveAsync Rule

    [Test]
    public async Task RefreshHealthForChatAsync_After1Failure_DoesNotMarkInactive()
    {
        // Arrange: failure counter returns 1
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _healthCache.IncrementFailureCount(ChatAId).Returns(1);

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync NOT called (need 3 consecutive failures)
        await _managedChatsRepository.DidNotReceive().MarkInactiveAsync(Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshHealthForChatAsync_After2ConsecutiveFailures_DoesNotMarkInactive()
    {
        // Arrange: failure counter returns 2
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _healthCache.IncrementFailureCount(ChatAId).Returns(2);

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync NOT called (only 2 of 3 required failures)
        await _managedChatsRepository.DidNotReceive().MarkInactiveAsync(Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshHealthForChatAsync_After3ConsecutiveFailures_MarksInactiveExactlyOnce()
    {
        // Arrange: failure counter returns 3 (threshold reached)
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _healthCache.IncrementFailureCount(ChatAId).Returns(3);

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync called exactly once for ChatA
        await _managedChatsRepository.Received(1).MarkInactiveAsync(ChatA, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshHealthForChatAsync_After3Failures_ResetsCounterAfterMarkInactive()
    {
        // Arrange: failure counter returns 3 (threshold reached)
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _healthCache.IncrementFailureCount(ChatAId).Returns(3);

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: ResetFailureCount called after MarkInactive (so 4th failure starts fresh)
        _healthCache.Received(1).ResetFailureCount(ChatAId);
    }

    #endregion

    #region Counter Reset on Success

    [Test]
    public async Task RefreshHealthForChatAsync_WhenReachable_ResetsFailureCounter()
    {
        // Arrange: chat is reachable — success resets counter
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(true);

        var sdkChat = MakeSupergroup(ChatAId);
        _chatHandler.GetChatAsync(ChatAId, Arg.Any<CancellationToken>()).Returns(sdkChat);
        _userService.GetBotIdAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _userHandler.GetChatMemberAsync(ChatAId, 999L, Arg.Any<CancellationToken>())
            .Returns(MakeMember(999L));

        // Act
        await _orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: ResetFailureCount called on successful reachability check
        _healthCache.Received(1).ResetFailureCount(ChatAId);
    }

    [Test]
    public async Task RefreshHealthForChatAsync_SuccessBetweenFailures_PreventsMarkInactive()
    {
        // Arrange: use real health cache to track state: fail, fail, success, fail, fail
        // Pattern: max 2 consecutive failures — should NOT trigger MarkInactive
        var realHealthCache = new ChatHealthCache(Substitute.For<ILogger<ChatHealthCache>>(), new CacheMetrics());

        var orchestrator = new ChatHealthRefreshOrchestrator(
            _configLoader,
            _chatCache,
            realHealthCache,
            _managedChatsRepository,
            _linkedChannelsRepository,
            _chatHandler,
            _userHandler,
            _userService,
            _chatService,
            _photoService,
            _photoHashService,
            _notificationService,
            _chatMetrics,
            _logger);

        var sdkChat = MakeSupergroup(ChatAId);
        _chatHandler.GetChatAsync(ChatAId, Arg.Any<CancellationToken>()).Returns(sdkChat);
        _userService.GetBotIdAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _userHandler.GetChatMemberAsync(ChatAId, 999L, Arg.Any<CancellationToken>())
            .Returns(MakeMember(999L));

        // fail #1
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // fail #2
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // success — resets counter
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(true);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // fail #1 again (counter was reset, so this is first failure of new sequence)
        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // fail #2 again (only 2 consecutive — not yet at 3)
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync was NOT called (never reached 3 consecutive)
        await _managedChatsRepository.DidNotReceive().MarkInactiveAsync(Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Per-Chat Isolation

    [Test]
    public async Task RefreshHealthForChatAsync_FailureCounters_ArePerChat()
    {
        // Arrange: use real health cache to verify independent counters
        var realHealthCache = new ChatHealthCache(Substitute.For<ILogger<ChatHealthCache>>(), new CacheMetrics());

        var orchestrator = new ChatHealthRefreshOrchestrator(
            _configLoader,
            _chatCache,
            realHealthCache,
            _managedChatsRepository,
            _linkedChannelsRepository,
            _chatHandler,
            _userHandler,
            _userService,
            _chatService,
            _photoService,
            _photoHashService,
            _notificationService,
            _chatMetrics,
            _logger);

        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _chatService.CheckHealthAsync(ChatB, Arg.Any<CancellationToken>()).Returns(false);

        // ChatA: 2 failures (not enough to trigger)
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // ChatB: 1 failure (not enough to trigger)
        await orchestrator.RefreshHealthForChatAsync(ChatB, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync NOT called for either chat (counters are independent)
        await _managedChatsRepository.DidNotReceive().MarkInactiveAsync(Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshHealthForChatAsync_ChatAAt3Failures_DoesNotAffectChatB()
    {
        // Arrange: use real health cache for true counter isolation
        var realHealthCache = new ChatHealthCache(Substitute.For<ILogger<ChatHealthCache>>(), new CacheMetrics());

        var orchestrator = new ChatHealthRefreshOrchestrator(
            _configLoader,
            _chatCache,
            realHealthCache,
            _managedChatsRepository,
            _linkedChannelsRepository,
            _chatHandler,
            _userHandler,
            _userService,
            _chatService,
            _photoService,
            _photoHashService,
            _notificationService,
            _chatMetrics,
            _logger);

        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);
        _chatService.CheckHealthAsync(ChatB, Arg.Any<CancellationToken>()).Returns(false);

        // ChatA: 3 consecutive failures → triggers MarkInactive for ChatA only
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // ChatB: only 1 failure — should NOT trigger MarkInactive
        await orchestrator.RefreshHealthForChatAsync(ChatB, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync called exactly once for ChatA, never for ChatB
        await _managedChatsRepository.Received(1).MarkInactiveAsync(ChatA, Arg.Any<CancellationToken>());
        await _managedChatsRepository.DidNotReceive().MarkInactiveAsync(ChatB, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Counter Reset After MarkInactive

    [Test]
    public async Task RefreshHealthForChatAsync_After4thConsecutiveFailure_StartsNewCount()
    {
        // Arrange: use real health cache — counter must reset after MarkInactive
        var realHealthCache = new ChatHealthCache(Substitute.For<ILogger<ChatHealthCache>>(), new CacheMetrics());

        var orchestrator = new ChatHealthRefreshOrchestrator(
            _configLoader,
            _chatCache,
            realHealthCache,
            _managedChatsRepository,
            _linkedChannelsRepository,
            _chatHandler,
            _userHandler,
            _userService,
            _chatService,
            _photoService,
            _photoHashService,
            _notificationService,
            _chatMetrics,
            _logger);

        _chatService.CheckHealthAsync(ChatA, Arg.Any<CancellationToken>()).Returns(false);

        // Fail 3 times → MarkInactive called, counter resets
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Fail 2 more times (new count: 1, 2 — should NOT trigger MarkInactive again yet)
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);
        await orchestrator.RefreshHealthForChatAsync(ChatA, cancellationToken: CancellationToken.None);

        // Assert: MarkInactiveAsync called exactly once (only on 3rd failure, 4th and 5th are new count)
        await _managedChatsRepository.Received(1).MarkInactiveAsync(ChatA, Arg.Any<CancellationToken>());
    }

    #endregion
}
