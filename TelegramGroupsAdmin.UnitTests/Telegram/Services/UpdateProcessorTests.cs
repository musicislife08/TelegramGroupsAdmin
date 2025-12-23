using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for UpdateProcessor routing logic.
/// Tests verify updates are routed to correct handlers based on update type.
///
/// NOTE: Message and EditedMessage tests are deferred to issue #23 (REFACTOR-18-4)
/// which will extract IMessageProcessingService interface for proper mocking.
/// MessageProcessingService is a concrete class with non-virtual methods.
/// </summary>
[TestFixture]
public class UpdateProcessorTests
{
    private IChatManagementService _mockChatManagementService = null!;
    private IWelcomeService _mockWelcomeService = null!;
    private ITelegramBotClientFactory _mockBotFactory = null!;
    private ITelegramOperations _mockOperations = null!;
    private ILogger<UpdateProcessor> _mockLogger = null!;
    private UpdateProcessor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockChatManagementService = Substitute.For<IChatManagementService>();
        _mockWelcomeService = Substitute.For<IWelcomeService>();
        _mockBotFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockOperations = Substitute.For<ITelegramOperations>();
        _mockLogger = Substitute.For<ILogger<UpdateProcessor>>();

        // Setup factory to return mock operations
        _mockBotFactory.GetOperationsAsync().Returns(_mockOperations);

        // Create SUT with null for MessageProcessingService.
        // SAFETY: This is safe because Message/EditedMessage routes (which use this dependency)
        // are explicitly NOT tested here - those tests are deferred to #23 which will extract
        // IMessageProcessingService interface. All routes tested in this file use other dependencies.
        _sut = new UpdateProcessor(
            null!, // MessageProcessingService - Message/EditedMessage routes not tested (see #23)
            _mockChatManagementService,
            _mockWelcomeService,
            _mockBotFactory,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockBotFactory?.Dispose();
    }

    #region Test Data Helpers

    private static ChatMemberUpdated CreateChatMemberUpdated(long chatId = 123, long userId = 456)
    {
        return new ChatMemberUpdated
        {
            Chat = new Chat { Id = chatId, Type = ChatType.Supergroup, Title = "Test Chat" },
            From = new User { Id = userId, FirstName = "Test" },
            Date = DateTime.UtcNow,
            OldChatMember = new ChatMemberLeft { User = new User { Id = userId, FirstName = "Test" } },
            NewChatMember = new ChatMemberMember { User = new User { Id = userId, FirstName = "Test" } }
        };
    }

    private static CallbackQuery CreateCallbackQuery(string callbackId = "test-callback", string data = "accept")
    {
        return new CallbackQuery
        {
            Id = callbackId,
            Data = data,
            From = new User { Id = 123, FirstName = "Test" },
            ChatInstance = "test-instance"
        };
    }

    private static Update CreateMyChatMemberUpdate(long chatId = 123)
    {
        return new Update
        {
            Id = 1,
            MyChatMember = CreateChatMemberUpdated(chatId)
        };
    }

    private static Update CreateChatMemberUpdate(long chatId = 123, long userId = 456)
    {
        return new Update
        {
            Id = 2,
            ChatMember = CreateChatMemberUpdated(chatId, userId)
        };
    }

    private static Update CreateCallbackQueryUpdate(string callbackId = "test-callback")
    {
        return new Update
        {
            Id = 3,
            CallbackQuery = CreateCallbackQuery(callbackId)
        };
    }

    private static Update CreateUnhandledUpdate()
    {
        return new Update { Id = 999 };
    }

    #endregion

    #region MyChatMember Update Tests

    [Test]
    public async Task ProcessUpdateAsync_WithMyChatMember_RoutesToChatManagementService()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate(chatId: 12345);

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert
        await _mockChatManagementService.Received(1)
            .HandleMyChatMemberUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 12345),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithMyChatMember_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert - verify other handlers NOT called
        await _mockChatManagementService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithMyChatMember_PassesCancellationToken()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ProcessUpdateAsync(update, token);

        // Assert
        await _mockChatManagementService.Received(1)
            .HandleMyChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), token);
    }

    #endregion

    #region ChatMember Update Tests

    [Test]
    public async Task ProcessUpdateAsync_WithChatMember_RoutesToBothServices()
    {
        // Arrange
        var update = CreateChatMemberUpdate(chatId: 789, userId: 999);

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert - both handlers called
        await _mockChatManagementService.Received(1)
            .HandleAdminStatusChangeAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 789),
                Arg.Any<CancellationToken>());
        await _mockWelcomeService.Received(1)
            .HandleChatMemberUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.NewChatMember.User.Id == 999),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithChatMember_DoesNotCallMyChatMemberHandler()
    {
        // Arrange
        var update = CreateChatMemberUpdate();

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert
        await _mockChatManagementService.DidNotReceive()
            .HandleMyChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithChatMember_PassesCancellationToken()
    {
        // Arrange
        var update = CreateChatMemberUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ProcessUpdateAsync(update, token);

        // Assert
        await _mockChatManagementService.Received(1)
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), token);
        await _mockWelcomeService.Received(1)
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), token);
    }

    #endregion

    #region CallbackQuery Update Tests

    [Test]
    public async Task ProcessUpdateAsync_WithCallbackQuery_RoutesToWelcomeService()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate("my-callback-id");

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert
        await _mockWelcomeService.Received(1)
            .HandleCallbackQueryAsync(
                Arg.Is<CallbackQuery>(q => q.Id == "my-callback-id"),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithCallbackQuery_AnswersCallback()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate("answer-this-callback");

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert
        await _mockBotFactory.Received(1).GetOperationsAsync();
        await _mockOperations.Received(1)
            .AnswerCallbackQueryAsync("answer-this-callback", text: null, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithCallbackQuery_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert
        await _mockChatManagementService.DidNotReceive()
            .HandleMyChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockChatManagementService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessUpdateAsync_WithCallbackQuery_PassesCancellationToken()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ProcessUpdateAsync(update, token);

        // Assert
        await _mockWelcomeService.Received(1)
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), token);
        await _mockOperations.Received(1)
            .AnswerCallbackQueryAsync(Arg.Any<string>(), text: null, cancellationToken: token);
    }

    #endregion

    #region Unhandled Update Tests

    [Test]
    public async Task ProcessUpdateAsync_WithUnhandledUpdate_DoesNotCallAnyHandlers()
    {
        // Arrange
        var update = CreateUnhandledUpdate();

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert - no handlers called
        await _mockChatManagementService.DidNotReceive()
            .HandleMyChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockChatManagementService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
        await _mockBotFactory.DidNotReceive().GetOperationsAsync();
    }

    [Test]
    public async Task ProcessUpdateAsync_WithUnhandledUpdate_CompletesWithoutError()
    {
        // Arrange
        var update = CreateUnhandledUpdate();

        // Act & Assert - should not throw
        await _sut.ProcessUpdateAsync(update);
    }

    #endregion

    #region Priority/Early Return Tests

    [Test]
    public async Task ProcessUpdateAsync_WithMyChatMemberAndChatMember_OnlyProcessesMyChatMember()
    {
        // Arrange - Update with both properties set (edge case, violates Telegram API contract)
        var update = new Update
        {
            Id = 1,
            MyChatMember = CreateChatMemberUpdated(chatId: 111),
            ChatMember = CreateChatMemberUpdated(chatId: 222)
        };

        // Act
        await _sut.ProcessUpdateAsync(update);

        // Assert - MyChatMember processed (first in priority), ChatMember skipped
        await _mockChatManagementService.Received(1)
            .HandleMyChatMemberUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 111),
                Arg.Any<CancellationToken>());
        await _mockChatManagementService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exception Propagation Tests

    [Test]
    public async Task ProcessUpdateAsync_WhenChatManagementServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();
        _mockChatManagementService
            .HandleMyChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.ProcessUpdateAsync(update));
    }

    [Test]
    public async Task ProcessUpdateAsync_WhenWelcomeServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();
        _mockWelcomeService
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Welcome service error"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.ProcessUpdateAsync(update));
    }

    #endregion

    #region Deferred Tests (Require IMessageProcessingService - Issue #23)

    // NOTE: The following tests are deferred until issue #23 (REFACTOR-18-4) is completed.
    // MessageProcessingService is a concrete class with non-virtual methods,
    // preventing proper mocking with NSubstitute.
    //
    // Deferred tests:
    // - ProcessUpdateAsync_WithMessage_RoutesToMessageProcessingService
    // - ProcessUpdateAsync_WithEditedMessage_RoutesToMessageProcessingService
    // - ProcessUpdateAsync_WithMessage_DoesNotCallOtherHandlers
    // - ProcessUpdateAsync_WithEditedMessage_DoesNotCallOtherHandlers

    #endregion
}
