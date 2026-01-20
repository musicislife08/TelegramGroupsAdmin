using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.E2ETests.Tests.ExamFlow;

/// <summary>
/// Full end-to-end tests for the DM exam flow.
/// Tests the complete integration: user joins → starts exam in DM → answers questions →
/// welcome timeout is cancelled → welcome response is updated.
///
/// These tests caught a critical bug where DM exams weren't properly cancelling
/// welcome timeouts because message.Chat.Id in DM is the user's ID, not the group chat ID.
/// </summary>
[TestFixture]
public class ExamFlowE2ETests : E2ETestBase
{
    private const long TestGroupChatId = -1001234567890L;
    private const long TestUserId = 123456789L;
    private const long TestDmChatId = 123456789L; // DM chat ID equals user ID

    [Test]
    public async Task FullExamFlow_McOnlyExam_PassingAnswers_CancelsTimeoutAndAcceptsUser()
    {
        // Arrange: Set up chat with MC-only exam config
        var chat = await new TestChatBuilder(Factory.Services)
            .WithId(TestGroupChatId)
            .WithTitle("Test Tech Group")
            .AsSupergroup()
            .BuildAsync();

        await new TestWelcomeConfigBuilder(Factory.Services)
            .ForChat(chat)
            .WithMcQuestionsOnly()
            .BuildAsync();

        var telegramUser = await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(TestUserId)
            .WithUsername("testexamuser")
            .WithName("Test", "ExamUser")
            .BuildAsync();

        // Create welcome response with timeout job (simulates user joining)
        var welcomeResponse = await CreateWelcomeResponseWithTimeoutAsync(
            TestUserId, TestGroupChatId, "timeout-job-123");

        using var scope = Factory.Services.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var welcomeService = scope.ServiceProvider.GetRequiredService<IWelcomeService>();
        var welcomeRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();

        // Get the exam config to know how many questions
        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();
        var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, TestGroupChatId);
        var examConfig = welcomeConfig!.ExamConfig!;

        // Start exam in DM (simulates user clicking deep link)
        var botUser = CreateTelegramUser(TestUserId, "testexamuser", "Test", "ExamUser");
        var startResult = await examFlowService.StartExamInDmAsync(
            TestGroupChatId, botUser, TestDmChatId, welcomeConfig, CancellationToken.None);

        Assert.That(startResult.Success, Is.True, "Exam should start successfully");

        // Get the session to find the session ID
        var session = await GetExamSessionAsync(TestGroupChatId, TestUserId);
        Assert.That(session, Is.Not.Null, "Exam session should exist");

        // Act: Answer all MC questions correctly through WelcomeService (the full E2E path)
        // This goes through WelcomeService.HandleCallbackQueryAsync → ExamFlowService → timeout cancellation
        for (var i = 0; i < examConfig.McQuestions.Count; i++)
        {
            var correctIndex = GetCorrectAnswerIndex(session!.Id, i, examConfig.McQuestions[i].Answers.Count);
            var callbackQuery = CreateExamCallbackQuery(session.Id, i, correctIndex, TestUserId, TestDmChatId);
            await welcomeService.HandleCallbackQueryAsync(callbackQuery, CancellationToken.None);
        }

        // Assert: Timeout job was cancelled (verified via mock)
        await Factory.MockJobScheduler.Received(1).CancelJobAsync(
            "timeout-job-123", Arg.Any<CancellationToken>());

        // Assert: Welcome response updated in database
        var updatedWelcome = await welcomeRepo.GetByUserAndChatAsync(TestUserId, TestGroupChatId);
        Assert.That(updatedWelcome, Is.Not.Null, "Welcome response should still exist");
        Assert.That(updatedWelcome!.TimeoutJobId, Is.Null, "Timeout job ID should be cleared");
        Assert.That(updatedWelcome.Response, Is.EqualTo(WelcomeResponseType.Accepted),
            "Welcome response should be marked as Accepted");
    }

    [Test]
    public async Task FullExamFlow_WithOpenEnded_PassingAnswers_CancelsTimeoutAndAcceptsUser()
    {
        // Arrange: Set up chat with full exam (MC + open-ended)
        var chat = await new TestChatBuilder(Factory.Services)
            .WithId(TestGroupChatId)
            .WithTitle("Test Tech Group")
            .AsSupergroup()
            .BuildAsync();

        await new TestWelcomeConfigBuilder(Factory.Services)
            .ForChat(chat)
            .WithFullExam()
            .BuildAsync();

        var telegramUser = await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(TestUserId)
            .WithUsername("testexamuser")
            .WithName("Test", "ExamUser")
            .BuildAsync();

        // Create welcome response with timeout job
        var welcomeResponse = await CreateWelcomeResponseWithTimeoutAsync(
            TestUserId, TestGroupChatId, "timeout-job-456");

        // Configure AI to pass the open-ended answer
        Factory.MockExamEvaluation.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExamEvaluationResult(Passed: true, Reasoning: "Good answer about technology", Confidence: 0.92));

        using var scope = Factory.Services.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var welcomeService = scope.ServiceProvider.GetRequiredService<IWelcomeService>();
        var messageProcessingService = scope.ServiceProvider.GetRequiredService<IMessageProcessingService>();
        var welcomeRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();

        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();
        var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, TestGroupChatId);
        var examConfig = welcomeConfig!.ExamConfig!;

        // Start exam in DM
        var botUser = CreateTelegramUser(TestUserId, "testexamuser", "Test", "ExamUser");
        await examFlowService.StartExamInDmAsync(
            TestGroupChatId, botUser, TestDmChatId, welcomeConfig, CancellationToken.None);

        var session = await GetExamSessionAsync(TestGroupChatId, TestUserId);

        // Answer MC questions through WelcomeService (full E2E path)
        for (var i = 0; i < examConfig.McQuestions.Count; i++)
        {
            var correctIndex = GetCorrectAnswerIndex(session!.Id, i, examConfig.McQuestions[i].Answers.Count);
            var callbackQuery = CreateExamCallbackQuery(session.Id, i, correctIndex, TestUserId, TestDmChatId);
            await welcomeService.HandleCallbackQueryAsync(callbackQuery, CancellationToken.None);
        }

        // Act: Answer open-ended question through MessageProcessingService (full E2E path)
        // This is how it works in production: user sends a DM text message
        var openEndedMessage = CreateDmTextMessage(TestUserId, TestDmChatId, "I love technology and want to learn more!");
        await messageProcessingService.HandleNewMessageAsync(openEndedMessage, CancellationToken.None);

        // Assert: Timeout job was cancelled
        await Factory.MockJobScheduler.Received(1).CancelJobAsync(
            "timeout-job-456", Arg.Any<CancellationToken>());

        // Assert: Welcome response updated
        var updatedWelcome = await welcomeRepo.GetByUserAndChatAsync(TestUserId, TestGroupChatId);
        Assert.That(updatedWelcome, Is.Not.Null);
        Assert.That(updatedWelcome!.TimeoutJobId, Is.Null, "Timeout job ID should be cleared");
        Assert.That(updatedWelcome.Response, Is.EqualTo(WelcomeResponseType.Accepted));
    }

    [Test]
    public async Task FullExamFlow_FailedExam_SentToReview_CancelsTimeoutButKeepsPending()
    {
        // Arrange: Set up chat with full exam
        var chat = await new TestChatBuilder(Factory.Services)
            .WithId(TestGroupChatId)
            .WithTitle("Test Tech Group")
            .AsSupergroup()
            .BuildAsync();

        await new TestWelcomeConfigBuilder(Factory.Services)
            .ForChat(chat)
            .WithFullExam()
            .BuildAsync();

        var telegramUser = await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(TestUserId)
            .WithUsername("testexamuser")
            .WithName("Test", "ExamUser")
            .BuildAsync();

        // Create welcome response with timeout job
        await CreateWelcomeResponseWithTimeoutAsync(TestUserId, TestGroupChatId, "timeout-job-789");

        // Configure AI to FAIL the open-ended answer
        Factory.MockExamEvaluation.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExamEvaluationResult(Passed: false, Reasoning: "Answer doesn't demonstrate interest", Confidence: 0.88));

        using var scope = Factory.Services.CreateScope();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var welcomeService = scope.ServiceProvider.GetRequiredService<IWelcomeService>();
        var messageProcessingService = scope.ServiceProvider.GetRequiredService<IMessageProcessingService>();
        var welcomeRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();

        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();
        var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, TestGroupChatId);
        var examConfig = welcomeConfig!.ExamConfig!;

        // Start exam in DM
        var botUser = CreateTelegramUser(TestUserId, "testexamuser", "Test", "ExamUser");
        await examFlowService.StartExamInDmAsync(
            TestGroupChatId, botUser, TestDmChatId, welcomeConfig, CancellationToken.None);

        var session = await GetExamSessionAsync(TestGroupChatId, TestUserId);

        // Answer MC questions correctly through WelcomeService (to reach open-ended)
        for (var i = 0; i < examConfig.McQuestions.Count; i++)
        {
            var correctIndex = GetCorrectAnswerIndex(session!.Id, i, examConfig.McQuestions[i].Answers.Count);
            var callbackQuery = CreateExamCallbackQuery(session.Id, i, correctIndex, TestUserId, TestDmChatId);
            await welcomeService.HandleCallbackQueryAsync(callbackQuery, CancellationToken.None);
        }

        // Act: Answer open-ended question through MessageProcessingService (will fail due to AI mock)
        var openEndedMessage = CreateDmTextMessage(TestUserId, TestDmChatId, "I dunno, just want to join");
        await messageProcessingService.HandleNewMessageAsync(openEndedMessage, CancellationToken.None);

        // Assert: Timeout job was still cancelled (user shouldn't be kicked while in review)
        await Factory.MockJobScheduler.Received(1).CancelJobAsync(
            "timeout-job-789", Arg.Any<CancellationToken>());

        // Assert: Welcome response stays Pending (admin will decide)
        var updatedWelcome = await welcomeRepo.GetByUserAndChatAsync(TestUserId, TestGroupChatId);
        Assert.That(updatedWelcome, Is.Not.Null);
        Assert.That(updatedWelcome!.TimeoutJobId, Is.Null, "Timeout should be cleared");
        Assert.That(updatedWelcome.Response, Is.EqualTo(WelcomeResponseType.Pending),
            "Should stay Pending while in review queue");
    }

    #region Helper Methods

    private async Task<WelcomeResponse> CreateWelcomeResponseWithTimeoutAsync(
        long userId, long chatId, string timeoutJobId)
    {
        using var scope = Factory.Services.CreateScope();
        var welcomeRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();

        // WelcomeResponse record parameters in order:
        // Id, ChatId, UserId, Username, WelcomeMessageId, Response, RespondedAt, DmSent, DmFallback, CreatedAt, TimeoutJobId
        var response = new WelcomeResponse(
            Id: 0,
            ChatId: chatId,
            UserId: userId,
            Username: "testexamuser",
            WelcomeMessageId: 1000,
            Response: WelcomeResponseType.Pending,
            RespondedAt: DateTimeOffset.MinValue, // Not responded yet
            DmSent: true,
            DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow,
            TimeoutJobId: timeoutJobId
        );

        var id = await welcomeRepo.InsertAsync(response);
        return response with { Id = id };
    }

    private async Task<ExamSession?> GetExamSessionAsync(long chatId, long userId)
    {
        using var scope = Factory.Services.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        return await sessionRepo.GetSessionAsync(chatId, userId);
    }

    private static User CreateTelegramUser(long id, string? username, string firstName, string? lastName)
    {
        return new User
        {
            Id = id,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            IsBot = false
        };
    }

    private static Message CreateDmMessage(long userId, long dmChatId)
    {
        return new Message
        {
            Id = Random.Shared.Next(1, 100000),  // Property is "Id", not "MessageId"
            Date = DateTime.UtcNow,
            Chat = new Chat
            {
                Id = dmChatId,
                Type = ChatType.Private
            },
            From = new User
            {
                Id = userId,
                FirstName = "Test",
                IsBot = false
            }
        };
    }

    /// <summary>
    /// Creates a DM text message for open-ended exam answers.
    /// </summary>
    private static Message CreateDmTextMessage(long userId, long dmChatId, string text)
    {
        return new Message
        {
            Id = Random.Shared.Next(1, 100000),
            Date = DateTime.UtcNow,
            Chat = new Chat
            {
                Id = dmChatId,
                Type = ChatType.Private
            },
            From = new User
            {
                Id = userId,
                FirstName = "Test",
                LastName = "ExamUser",
                Username = "testexamuser",
                IsBot = false
            },
            Text = text
        };
    }

    /// <summary>
    /// Gets the correct answer index for a question after shuffle.
    /// Uses the same deterministic algorithm as ExamFlowService.GenerateShuffleForQuestion.
    /// </summary>
    /// <remarks>
    /// The first answer (original index 0) is always correct, but answers are shuffled
    /// deterministically based on sessionId and questionIndex. This method finds where
    /// the correct answer ended up after shuffling.
    /// </remarks>
    private static int GetCorrectAnswerIndex(long sessionId, int questionIndex, int answerCount)
    {
        // Same deterministic algorithm as ExamFlowService.GenerateShuffleForQuestion
        var indices = Enumerable.Range(0, answerCount).ToArray();
        var seed = HashCode.Combine(sessionId, questionIndex);
        var random = new Random(seed);

        // Fisher-Yates shuffle
        for (var i = answerCount - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        // Find where original index 0 (the correct answer) ended up
        return Array.IndexOf(indices, 0);
    }

    /// <summary>
    /// Creates a CallbackQuery for exam answer callbacks.
    /// Format: exam:{sessionId}:{questionIndex}:{answerIndex}
    /// </summary>
    private static CallbackQuery CreateExamCallbackQuery(
        long sessionId, int questionIndex, int answerIndex, long userId, long dmChatId)
    {
        return new CallbackQuery
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            From = new User
            {
                Id = userId,
                FirstName = "Test",
                LastName = "ExamUser",
                Username = "testexamuser",
                IsBot = false
            },
            ChatInstance = dmChatId.ToString(),
            Data = $"exam:{sessionId}:{questionIndex}:{answerIndex}",
            Message = new Message
            {
                Id = Random.Shared.Next(1, 100000),
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = dmChatId, Type = ChatType.Private },
                From = new User { Id = userId, FirstName = "Test", IsBot = false }
            }
        };
    }

    #endregion
}
