using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// Integration tests for ExamFlowService orchestration methods.
/// Tests database state transitions with real PostgreSQL, mocked Telegram bot and AI services.
/// </summary>
/// <remarks>
/// Tests cover:
/// - Session lifecycle (create, update, delete)
/// - MC answer recording and state transitions
/// - Exam completion flows (pass/fail)
/// </remarks>
[TestFixture]
public class ExamFlowServiceTests
{
    // Canonical MainChat anchor: chat_id = -100026957614982 (Main Community in golden_template)
    private const long TestChatId = -100026957614982L;
    private const long TestUserId = 123456789L;
    private const long TestDmChatId = 123456789L; // DM chat ID equals user ID

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBotMessageService? _mockMessageService;
    private IBotChatService? _mockChatService;
    private IBotDmService? _mockDmService;
    private IBotModerationService? _mockModerationService;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        // Set up mocks for the new Bot*Service interfaces
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockChatService = Substitute.For<IBotChatService>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        var mockExamEvaluationService = Substitute.For<IExamEvaluationService>();
        var mockConfigService = Substitute.For<IConfigService>();

        // Configure mock config service to return valid exam config
        var defaultConfig = CreateValidExamConfig();
        mockConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(new ValueTask<WelcomeConfig?>(defaultConfig));

        // Create a real Message for mock returns
        var responseMessage = TelegramTestFactory.CreateMessage(messageId: 1);

        // Mock GetChatAsync to return chat info (needed for exam intro message)
        var testChatInfo = TelegramTestFactory.CreateChatFullInfo(id: TestChatId, type: ChatType.Supergroup, title: "Test Chat");
        _mockChatService.GetChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(testChatInfo));

        // Mock message service methods
        _mockMessageService.SendAndSaveMessageAsync(
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(responseMessage));

        _mockMessageService.EditAndUpdateMessageAsync(
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(responseMessage));

        // Mock DM service methods - exam questions are sent via DM
        var dmSuccessResult = new DmDeliveryResult { DmSent = true, MessageId = 1 };
        _mockDmService.SendDmWithKeyboardAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<InlineKeyboardMarkup>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dmSuccessResult));

        _mockDmService.SendDmAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<long?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dmSuccessResult));

        _mockDmService.SendDmAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<string>(),
                Arg.Any<long?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dmSuccessResult));

        _mockDmService.DeleteDmMessageAsync(
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Build service provider with real database and mocked externals
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        // Real repositories
        services.AddScoped<IExamSessionRepository, ExamSessionRepository>();
        services.AddScoped<IWelcomeResponsesRepository, WelcomeResponsesRepository>();
        services.AddScoped<IReportsRepository, ReportsRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        // Mocked external services (match scopes from main app registration)
        services.AddSingleton(_mockMessageService);
        services.AddSingleton(_mockChatService);
        services.AddSingleton(_mockDmService);
        services.AddSingleton(_mockModerationService);
        services.AddScoped(_ => mockExamEvaluationService);  // Scoped in main app
        services.AddScoped(_ => mockConfigService);  // Scoped in main app
        services.AddSingleton(Substitute.For<IWelcomeAdmissionHandler>());

        // The service under test
        services.AddScoped<IExamFlowService, ExamFlowService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region StartExamInDmAsync Tests

    [Test]
    public async Task StartExamInDmAsync_WithValidConfig_CreatesSession()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test", username: "testuser");
        var config = CreateValidExamConfig();

        // Act
        var result = await examFlowService.StartExamInDmAsync(
            new ChatIdentity(TestChatId, "Test Chat"), user, TestDmChatId, config);

        // Assert
        Assert.That(result.Success, Is.True);

        var session = await sessionRepo.GetSessionAsync(TestChatId, TestUserId);
        Assert.That(session, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(session.UserId, Is.EqualTo(TestUserId));
            Assert.That(session.CurrentQuestionIndex, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task StartExamInDmAsync_WithInvalidConfig_ReturnsFalse()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var config = new WelcomeConfig { ExamConfig = null };

        // Act
        var result = await examFlowService.StartExamInDmAsync(
            new ChatIdentity(TestChatId, "Test Chat"), user, TestDmChatId, config);

        // Assert
        Assert.That(result.Success, Is.False);
    }

    #endregion

    #region HandleMcAnswerAsync Tests

    [Test]
    public async Task HandleMcAnswerAsync_WithValidAnswer_RecordsAndAdvances()
    {
        // Arrange - create session first
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var config = CreateValidExamConfig();

        // Start exam to create session
        await examFlowService.StartExamInDmAsync(new ChatIdentity(TestChatId, "Test Chat"), user, TestDmChatId, config);

        var session = await sessionRepo.GetSessionAsync(TestChatId, TestUserId);
        Assert.That(session, Is.Not.Null);

        // Create message using UnsafeAccessor factory
        var message = TelegramTestFactory.CreateMessage(
            messageId: 1,
            chatId: TestDmChatId,
            chatType: ChatType.Private);

        // Act - answer first question
        var result = await examFlowService.HandleMcAnswerAsync(
            session!.Id, questionIndex: 0, answerIndex: 0, user, message);

        // Assert
        Assert.That(result.ExamComplete, Is.False); // Only 1 of 2 MC questions answered

        var updatedSession = await sessionRepo.GetByIdAsync(session.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updatedSession!.CurrentQuestionIndex, Is.EqualTo(1));
            Assert.That(updatedSession.McAnswers, Is.Not.Null);
        }
        Assert.That(updatedSession.McAnswers!.ContainsKey(0), Is.True);
    }

    [Test]
    public async Task HandleMcAnswerAsync_WithExpiredSession_ReturnsCompleteWithFail()
    {
        // Arrange - create expired session directly
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var sessionId = await sessionRepo.CreateSessionAsync(new ChatIdentity(TestChatId, "Test Chat"), UserIdentity.FromId(TestUserId), expiredTime);

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var message = TelegramTestFactory.CreateMessage(
            messageId: 1,
            chatId: TestDmChatId,
            chatType: ChatType.Private);

        // Act
        var result = await examFlowService.HandleMcAnswerAsync(
            sessionId, questionIndex: 0, answerIndex: 0, user, message);

        using (Assert.EnterMultipleScope())
        {
            // Assert - expired session should be treated as complete/failed
            Assert.That(result.ExamComplete, Is.True);
            Assert.That(result.Passed, Is.False);
        }
    }

    [Test]
    public async Task HandleMcAnswerAsync_WrongUser_RejectsAnswer()
    {
        // Arrange - create session for one user
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await sessionRepo.CreateSessionAsync(new ChatIdentity(TestChatId, "Test Chat"), UserIdentity.FromId(TestUserId), expiresAt);

        // Different user tries to answer
        var wrongUser = TelegramTestFactory.CreateUser(id: TestUserId + 1, firstName: "Wrong");
        var message = TelegramTestFactory.CreateMessage(
            messageId: 1,
            chatId: TestDmChatId,
            chatType: ChatType.Private);

        // Act
        var result = await examFlowService.HandleMcAnswerAsync(
            sessionId, questionIndex: 0, answerIndex: 0, wrongUser, message);

        using (Assert.EnterMultipleScope())
        {
            // Assert - wrong user is rejected, but legitimate user's exam is still active (not complete)
            Assert.That(result.ExamComplete, Is.False);
            Assert.That(result.Passed, Is.Null);
        }

        // Verify the session still exists for the legitimate user
        var session = await sessionRepo.GetByIdAsync(sessionId);
        Assert.That(session, Is.Not.Null);
    }

    #endregion

    #region HasActiveSessionAsync Tests

    [Test]
    public async Task HasActiveSessionAsync_WithActiveSession_ReturnsTrue()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await sessionRepo.CreateSessionAsync(new ChatIdentity(TestChatId, "Test Chat"), UserIdentity.FromId(TestUserId), expiresAt);

        // Act
        var hasSession = await examFlowService.HasActiveSessionAsync(
            new ChatIdentity(TestChatId, "Test Chat"),
            UserIdentity.FromId(TestUserId));

        // Assert
        Assert.That(hasSession, Is.True);
    }

    [Test]
    public async Task HasActiveSessionAsync_WithNoSession_ReturnsFalse()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

        // Act
        var hasSession = await examFlowService.HasActiveSessionAsync(
            new ChatIdentity(TestChatId, "Test Chat"),
            UserIdentity.FromId(TestUserId));

        // Assert
        Assert.That(hasSession, Is.False);
    }

    #endregion

    #region GetActiveExamContextAsync Tests

    [Test]
    public async Task GetActiveExamContextAsync_WithSession_ReturnsContext()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await sessionRepo.CreateSessionAsync(new ChatIdentity(TestChatId, "Test Chat"), UserIdentity.FromId(TestUserId), expiresAt);

        // Act
        var context = await examFlowService.GetActiveExamContextAsync(UserIdentity.FromId(TestUserId));

        // Assert
        Assert.That(context, Is.Not.Null);
        Assert.That(context!.GroupChatId, Is.EqualTo(TestChatId));
    }

    [Test]
    public async Task GetActiveExamContextAsync_WithNoSession_ReturnsNull()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

        // Act
        var context = await examFlowService.GetActiveExamContextAsync(UserIdentity.FromId(TestUserId));

        // Assert
        Assert.That(context, Is.Null);
    }

    #endregion

    #region CancelSessionAsync Tests

    [Test]
    public async Task CancelSessionAsync_WithExistingSession_DeletesSession()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await sessionRepo.CreateSessionAsync(new ChatIdentity(TestChatId, "Test Chat"), UserIdentity.FromId(TestUserId), expiresAt);

        // Act
        await examFlowService.CancelSessionAsync(
            new ChatIdentity(TestChatId, "Test Chat"),
            UserIdentity.FromId(TestUserId));

        // Assert
        var session = await sessionRepo.GetSessionAsync(TestChatId, TestUserId);
        Assert.That(session, Is.Null);
    }

    [Test]
    public async Task CancelSessionAsync_WithNoSession_DoesNotThrow()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

        // Act & Assert - should not throw
        Assert.DoesNotThrowAsync(async () =>
            await examFlowService.CancelSessionAsync(
                new ChatIdentity(TestChatId, "Test Chat"),
                UserIdentity.FromId(TestUserId)));
    }

    #endregion

    #region Exam Denial Teaser Cleanup Tests

    [Test]
    public async Task DenyExamFailureAsync_FailedKick_DeletesTeaserMessage()
    {
        // Regression coverage for I2: step 1 writes welcome_responses.Response = Denied before
        // the kick runs. If the kick fails, BotModerationService's own cleanup never runs (it's
        // gated on success), so the teaser must be deleted here instead — otherwise it strands
        // with live buttons while the response already reads Denied.
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var welcomeResponsesRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        // welcome_responses.user_id has an FK to telegram_users — the test's synthetic
        // TestUserId isn't in the canonical dataset, so create it first.
        await telegramUserRepo.GetOrCreateAsync(UserIdentity.FromId(TestUserId), isBot: false);

        const int teaserMessageId = 42424;
        await welcomeResponsesRepo.InsertAsync(new WelcomeResponse(
            Id: 0, ChatId: TestChatId, UserId: TestUserId, Username: "testuser",
            WelcomeMessageId: teaserMessageId, Response: WelcomeResponseType.Pending,
            RespondedAt: DateTimeOffset.UtcNow, DmSent: false, DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow, TimeoutJobId: null));

        _mockModerationService!.KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("kick failed"));

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var chat = new ChatIdentity(TestChatId, "Test Chat");
        var executor = Actor.WelcomeFlow;

        // Act
        var result = await examFlowService.DenyExamFailureAsync(
            UserIdentity.From(user), chat, executor);

        // Assert
        Assert.That(result.Success, Is.False);

        var response = await welcomeResponsesRepo.GetByUserAndChatAsync(TestUserId, TestChatId);
        Assert.That(response!.Response, Is.EqualTo(WelcomeResponseType.Denied),
            "step 1 writes Denied unconditionally, before the kick is attempted");

        await _mockModerationService.Received(1).DeleteMessageAsync(
            Arg.Is<DeleteMessageIntent>(i => i!.MessageId == teaserMessageId && i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DenyAndBanExamFailureAsync_FailedBan_DeletesTeaserMessage()
    {
        // Same defect class as the kick branch above, for the global-ban path.
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var welcomeResponsesRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        await telegramUserRepo.GetOrCreateAsync(UserIdentity.FromId(TestUserId), isBot: false);

        const int teaserMessageId = 42425;
        await welcomeResponsesRepo.InsertAsync(new WelcomeResponse(
            Id: 0, ChatId: TestChatId, UserId: TestUserId, Username: "testuser",
            WelcomeMessageId: teaserMessageId, Response: WelcomeResponseType.Pending,
            RespondedAt: DateTimeOffset.UtcNow, DmSent: false, DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow, TimeoutJobId: null));

        _mockModerationService!.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("ban failed"));

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var chat = new ChatIdentity(TestChatId, "Test Chat");
        var executor = Actor.WelcomeFlow;

        // Act
        var result = await examFlowService.DenyAndBanExamFailureAsync(
            UserIdentity.From(user), chat, executor);

        // Assert
        Assert.That(result.Success, Is.False);

        await _mockModerationService.Received(1).DeleteMessageAsync(
            Arg.Is<DeleteMessageIntent>(i => i!.MessageId == teaserMessageId && i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static WelcomeConfig CreateValidExamConfig()
    {
        return new WelcomeConfig
        {
            TimeoutSeconds = 300,
            ExamConfig = new ExamConfig
            {
                McQuestions =
                [
                    new ExamMcQuestion
                    {
                        Question = "What is 2 + 2?",
                        Answers = ["4", "3", "5", "6"] // First answer is always correct
                    },
                    new ExamMcQuestion
                    {
                        Question = "What color is the sky?",
                        Answers = ["Blue", "Green", "Red", "Yellow"]
                    }
                ],
                OpenEndedQuestion = "Why do you want to join this group?",
                McPassingThreshold = 50
            }
        };
    }

    #endregion
}
