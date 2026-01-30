using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

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
    private const long TestChatId = -1001234567890L;
    private const long TestUserId = 123456789L;
    private const long TestDmChatId = 123456789L; // DM chat ID equals user ID

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITelegramOperations? _mockOperations;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up mocks
        _mockOperations = Substitute.For<ITelegramOperations>();
        var mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        var mockExamEvaluationService = Substitute.For<IExamEvaluationService>();
        var mockConfigService = Substitute.For<IConfigService>();

        // Configure mock config service to return valid exam config
        var defaultConfig = CreateValidExamConfig();
        mockConfigService.GetEffectiveAsync<WelcomeConfig>(
                Arg.Any<ConfigType>(),
                Arg.Any<long>())
            .Returns(new ValueTask<WelcomeConfig?>(defaultConfig));

        // Create a real Message for mock returns
        var responseMessage = TelegramTestFactory.CreateMessage(messageId: 1);

        // Mock GetChatAsync to return chat info (needed for exam intro message)
        var testChatInfo = TelegramTestFactory.CreateChatFullInfo(id: TestChatId, type: ChatType.Supergroup, title: "Test Chat");
        _mockOperations.GetChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(testChatInfo));

        _mockOperations.SendMessageAsync(
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<ReplyMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(responseMessage));

        _mockOperations.EditMessageTextAsync(
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(responseMessage));

        mockBotClientFactory.GetOperationsAsync().Returns(Task.FromResult(_mockOperations));

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
        services.AddScoped<IReportsRepository, ReportsRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        // Mocked external services (match scopes from main app registration)
        services.AddSingleton(mockBotClientFactory);  // Singleton in main app
        services.AddScoped(_ => mockExamEvaluationService);  // Scoped in main app
        services.AddScoped(_ => mockConfigService);  // Scoped in main app

        // The service under test
        services.AddScoped<IExamFlowService, ExamFlowService>();

        _serviceProvider = services.BuildServiceProvider();

        // Create test chat in database (required for exam flow)
        await CreateTestChatAsync();
    }

    private async Task CreateTestChatAsync()
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        context.ManagedChats.Add(new Data.Models.ManagedChatRecordDto
        {
            ChatId = TestChatId,
            ChatName = "Test Chat",
            ChatType = Data.Models.ManagedChatType.Supergroup,
            AddedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await context.SaveChangesAsync();
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
            TestChatId, user, TestDmChatId, config);

        // Assert
        Assert.That(result.Success, Is.True);

        var session = await sessionRepo.GetSessionAsync(TestChatId, TestUserId);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.ChatId, Is.EqualTo(TestChatId));
        Assert.That(session.UserId, Is.EqualTo(TestUserId));
        Assert.That(session.CurrentQuestionIndex, Is.EqualTo(0));
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
            TestChatId, user, TestDmChatId, config);

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
        await examFlowService.StartExamInDmAsync(TestChatId, user, TestDmChatId, config);

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
        Assert.That(updatedSession!.CurrentQuestionIndex, Is.EqualTo(1));
        Assert.That(updatedSession.McAnswers, Is.Not.Null);
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
        var sessionId = await sessionRepo.CreateSessionAsync(TestChatId, TestUserId, expiredTime);

        var user = TelegramTestFactory.CreateUser(id: TestUserId, firstName: "Test");
        var message = TelegramTestFactory.CreateMessage(
            messageId: 1,
            chatId: TestDmChatId,
            chatType: ChatType.Private);

        // Act
        var result = await examFlowService.HandleMcAnswerAsync(
            sessionId, questionIndex: 0, answerIndex: 0, user, message);

        // Assert - expired session should be treated as complete/failed
        Assert.That(result.ExamComplete, Is.True);
        Assert.That(result.Passed, Is.False);
    }

    [Test]
    public async Task HandleMcAnswerAsync_WrongUser_RejectsAnswer()
    {
        // Arrange - create session for one user
        using var scope = _serviceProvider!.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await sessionRepo.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Different user tries to answer
        var wrongUser = TelegramTestFactory.CreateUser(id: TestUserId + 1, firstName: "Wrong");
        var message = TelegramTestFactory.CreateMessage(
            messageId: 1,
            chatId: TestDmChatId,
            chatType: ChatType.Private);

        // Act
        var result = await examFlowService.HandleMcAnswerAsync(
            sessionId, questionIndex: 0, answerIndex: 0, wrongUser, message);

        // Assert - wrong user is rejected, but legitimate user's exam is still active (not complete)
        Assert.That(result.ExamComplete, Is.False);
        Assert.That(result.Passed, Is.Null);

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
        await sessionRepo.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        var hasSession = await examFlowService.HasActiveSessionAsync(TestChatId, TestUserId);

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
        var hasSession = await examFlowService.HasActiveSessionAsync(TestChatId, TestUserId);

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
        await sessionRepo.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        var context = await examFlowService.GetActiveExamContextAsync(TestUserId);

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
        var context = await examFlowService.GetActiveExamContextAsync(TestUserId);

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
        await sessionRepo.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        await examFlowService.CancelSessionAsync(TestChatId, TestUserId);

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
            await examFlowService.CancelSessionAsync(TestChatId, TestUserId));
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
