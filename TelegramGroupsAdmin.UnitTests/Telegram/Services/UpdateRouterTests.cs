using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for UpdateRouter routing logic.
/// Tests verify updates are routed to correct services based on update type.
/// Uses scope-per-update architecture for proper DI lifetime management.
/// </summary>
[TestFixture]
public class UpdateRouterTests
{
    private IServiceProvider _mockServiceProvider = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockScopeServiceProvider = null!;
    private ILogger<UpdateRouter> _mockLogger = null!;

    // Services resolved from scope
    private IBotChatService _mockChatService = null!;
    private IWelcomeService _mockWelcomeService = null!;
    private IMessageProcessingService _mockMessageProcessingService = null!;
    private IBanCallbackService _mockBanCallbackService = null!;
    private IReportCallbackService _mockReportCallbackService = null!;
    private IBotMessageService _mockMessageService = null!;
    private IChatHealthRefreshOrchestrator _mockHealthOrchestrator = null!;

    private UpdateRouter _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockScopeServiceProvider = Substitute.For<IServiceProvider>();
        _mockLogger = Substitute.For<ILogger<UpdateRouter>>();

        // Services resolved from scope
        _mockChatService = Substitute.For<IBotChatService>();
        _mockWelcomeService = Substitute.For<IWelcomeService>();
        _mockMessageProcessingService = Substitute.For<IMessageProcessingService>();
        _mockBanCallbackService = Substitute.For<IBanCallbackService>();
        _mockReportCallbackService = Substitute.For<IReportCallbackService>();
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockHealthOrchestrator = Substitute.For<IChatHealthRefreshOrchestrator>();

        // Wire up scope factory pattern
        var mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockServiceProvider.GetService(typeof(IServiceScopeFactory)).Returns(mockScopeFactory);

        _mockScope.ServiceProvider.Returns(_mockScopeServiceProvider);

        // Wire up service resolution from scope
        _mockScopeServiceProvider.GetService(typeof(IBotChatService)).Returns(_mockChatService);
        _mockScopeServiceProvider.GetService(typeof(IWelcomeService)).Returns(_mockWelcomeService);
        _mockScopeServiceProvider.GetService(typeof(IMessageProcessingService)).Returns(_mockMessageProcessingService);
        _mockScopeServiceProvider.GetService(typeof(IBanCallbackService)).Returns(_mockBanCallbackService);
        _mockScopeServiceProvider.GetService(typeof(IReportCallbackService)).Returns(_mockReportCallbackService);
        _mockScopeServiceProvider.GetService(typeof(IBotMessageService)).Returns(_mockMessageService);
        _mockScopeServiceProvider.GetService(typeof(IChatHealthRefreshOrchestrator)).Returns(_mockHealthOrchestrator);

        // Wire up IBotUserService for bot message filtering (bot ID 999 won't match test user ID 456)
        var mockBotUserService = Substitute.For<IBotUserService>();
        mockBotUserService.GetBotIdAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _mockScopeServiceProvider.GetService(typeof(IBotUserService)).Returns(mockBotUserService);

        // Callback services return false by default (routes to welcome service)
        _mockBanCallbackService.CanHandle(Arg.Any<string>()).Returns(false);
        _mockReportCallbackService.CanHandle(Arg.Any<string>()).Returns(false);

        _sut = new UpdateRouter(_mockServiceProvider, _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
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

    private static Update CreateCallbackQueryUpdate(string callbackId = "test-callback", string data = "accept")
    {
        return new Update
        {
            Id = 3,
            CallbackQuery = CreateCallbackQuery(callbackId, data)
        };
    }

    private static Update CreateUnhandledUpdate()
    {
        return new Update { Id = 999 };
    }

    private static Message CreateMessage(int messageId = 100, long chatId = 123)
    {
        // Message.MessageId is read-only in Telegram.Bot v22
        // Use JSON deserialization (how Telegram.Bot creates objects internally)
        var json = $$"""
        {
            "message_id": {{messageId}},
            "date": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
            "chat": {
                "id": {{chatId}},
                "type": "supergroup",
                "title": "Test Chat"
            },
            "from": {
                "id": 456,
                "is_bot": false,
                "first_name": "Test"
            },
            "text": "Test message"
        }
        """;
        return JsonSerializer.Deserialize<Message>(json, JsonSerializerOptions.Web)!;
    }

    private static Update CreateMessageUpdate(int messageId = 100)
    {
        return new Update
        {
            Id = 4,
            Message = CreateMessage(messageId)
        };
    }

    private static Update CreateEditedMessageUpdate(int messageId = 100)
    {
        return new Update
        {
            Id = 5,
            EditedMessage = CreateMessage(messageId)
        };
    }

    #endregion

    #region MyChatMember Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithMyChatMember_RoutesToChatService()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate(chatId: 12345);

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockChatService.Received(1)
            .HandleBotMembershipUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 12345),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithMyChatMember_TriggersHealthRefresh()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate(chatId: 12345);

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - health refresh triggered for the chat
        await _mockHealthOrchestrator.Received(1)
            .RefreshHealthForChatAsync(Arg.Is<ChatIdentity>(c => c.Id == 12345), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithMyChatMember_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - verify other handlers NOT called
        await _mockChatService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithMyChatMember_PassesCancellationToken()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.RouteUpdateAsync(update, token);

        // Assert
        await _mockChatService.Received(1)
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), token);
    }

    #endregion

    #region ChatMember Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithChatMember_RoutesToBothServices()
    {
        // Arrange
        var update = CreateChatMemberUpdate(chatId: 789, userId: 999);

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - both handlers called
        await _mockChatService.Received(1)
            .HandleAdminStatusChangeAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 789),
                Arg.Any<CancellationToken>());
        await _mockWelcomeService.Received(1)
            .HandleChatMemberUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.NewChatMember.User.Id == 999),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithChatMember_DoesNotCallMyChatMemberHandler()
    {
        // Arrange
        var update = CreateChatMemberUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockChatService.DidNotReceive()
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithChatMember_PassesCancellationToken()
    {
        // Arrange
        var update = CreateChatMemberUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.RouteUpdateAsync(update, token);

        // Assert
        await _mockChatService.Received(1)
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), token);
        await _mockWelcomeService.Received(1)
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), token);
    }

    #endregion

    #region CallbackQuery Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithCallbackQuery_RoutesToWelcomeService()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate("my-callback-id");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockWelcomeService.Received(1)
            .HandleCallbackQueryAsync(
                Arg.Is<CallbackQuery>(q => q.Id == "my-callback-id"),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithCallbackQuery_AnswersCallback()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate("answer-this-callback");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockMessageService.Received(1)
            .AnswerCallbackAsync("answer-this-callback", null, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithCallbackQuery_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockChatService.DidNotReceive()
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockChatService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithCallbackQuery_PassesCancellationToken()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.RouteUpdateAsync(update, token);

        // Assert
        await _mockWelcomeService.Received(1)
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), token);
        await _mockMessageService.Received(1)
            .AnswerCallbackAsync(Arg.Any<string>(), null, false, token);
    }

    [Test]
    public async Task RouteUpdateAsync_WithReportCallback_RoutesToReportCallbackService()
    {
        // Arrange
        _mockReportCallbackService.CanHandle("rev:12345:0").Returns(true);
        var update = CreateCallbackQueryUpdate(data: "rev:12345:0");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockReportCallbackService.Received(1).HandleCallbackAsync(
            Arg.Is<CallbackQuery>(q => q.Data == "rev:12345:0"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithReportCallback_DoesNotRouteToWelcomeService()
    {
        // Arrange - report service claims the callback
        _mockReportCallbackService.CanHandle("rev:12345:0").Returns(true);
        var update = CreateCallbackQueryUpdate(data: "rev:12345:0");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - welcome service should NOT be called
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithBanCallback_RoutesToBanCallbackService()
    {
        // Arrange
        _mockBanCallbackService.CanHandle("ban:12345:confirm").Returns(true);
        var update = CreateCallbackQueryUpdate(data: "ban:12345:confirm");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockBanCallbackService.Received(1).HandleCallbackAsync(
            Arg.Is<CallbackQuery>(q => q.Data == "ban:12345:confirm"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithBanCallback_DoesNotRouteToWelcomeService()
    {
        // Arrange - ban service claims the callback
        _mockBanCallbackService.CanHandle("ban:12345:confirm").Returns(true);
        var update = CreateCallbackQueryUpdate(data: "ban:12345:confirm");

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - welcome service should NOT be called
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Unhandled Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithUnhandledUpdate_DoesNotCallAnyHandlers()
    {
        // Arrange
        var update = CreateUnhandledUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - no handlers called
        await _mockChatService.DidNotReceive()
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockChatService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithUnhandledUpdate_CompletesWithoutError()
    {
        // Arrange
        var update = CreateUnhandledUpdate();

        // Act & Assert - should not throw
        await _sut.RouteUpdateAsync(update);
    }

    #endregion

    #region Priority/Early Return Tests

    [Test]
    public async Task RouteUpdateAsync_WithMyChatMemberAndChatMember_OnlyProcessesMyChatMember()
    {
        // Arrange - Update with both properties set (edge case, violates Telegram API contract)
        var update = new Update
        {
            Id = 1,
            MyChatMember = CreateChatMemberUpdated(chatId: 111),
            ChatMember = CreateChatMemberUpdated(chatId: 222)
        };

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - MyChatMember processed (first in priority), ChatMember skipped
        await _mockChatService.Received(1)
            .HandleBotMembershipUpdateAsync(
                Arg.Is<ChatMemberUpdated>(m => m.Chat.Id == 111),
                Arg.Any<CancellationToken>());
        await _mockChatService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Message Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithMessage_RoutesToMessageProcessingService()
    {
        // Arrange
        var update = CreateMessageUpdate(messageId: 12345);

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockMessageProcessingService.Received(1)
            .HandleNewMessageAsync(
                Arg.Is<Message>(m => m.MessageId == 12345),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithMessage_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateMessageUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - verify other handlers NOT called
        await _mockChatService.DidNotReceive()
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockChatService.DidNotReceive()
            .HandleAdminStatusChangeAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleChatMemberUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithMessage_PassesCancellationToken()
    {
        // Arrange
        var update = CreateMessageUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.RouteUpdateAsync(update, token);

        // Assert
        await _mockMessageProcessingService.Received(1)
            .HandleNewMessageAsync(Arg.Any<Message>(), token);
    }

    #endregion

    #region EditedMessage Update Tests

    [Test]
    public async Task RouteUpdateAsync_WithEditedMessage_RoutesToMessageProcessingService()
    {
        // Arrange
        var update = CreateEditedMessageUpdate(messageId: 67890);

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert
        await _mockMessageProcessingService.Received(1)
            .HandleEditedMessageAsync(
                Arg.Is<Message>(m => m.MessageId == 67890),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithEditedMessage_DoesNotCallOtherHandlers()
    {
        // Arrange
        var update = CreateEditedMessageUpdate();

        // Act
        await _sut.RouteUpdateAsync(update);

        // Assert - verify other handlers NOT called
        await _mockMessageProcessingService.DidNotReceive()
            .HandleNewMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await _mockChatService.DidNotReceive()
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>());
        await _mockWelcomeService.DidNotReceive()
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithEditedMessage_PassesCancellationToken()
    {
        // Arrange
        var update = CreateEditedMessageUpdate();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.RouteUpdateAsync(update, token);

        // Assert
        await _mockMessageProcessingService.Received(1)
            .HandleEditedMessageAsync(Arg.Any<Message>(), token);
    }

    #endregion

    #region Exception Propagation Tests

    [Test]
    public async Task RouteUpdateAsync_WhenChatServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var update = CreateMyChatMemberUpdate();
        _mockChatService
            .HandleBotMembershipUpdateAsync(Arg.Any<ChatMemberUpdated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.RouteUpdateAsync(update));
    }

    [Test]
    public async Task RouteUpdateAsync_WhenWelcomeServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var update = CreateCallbackQueryUpdate();
        _mockWelcomeService
            .HandleCallbackQueryAsync(Arg.Any<CallbackQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Welcome service error"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.RouteUpdateAsync(update));
    }

    #endregion
}
