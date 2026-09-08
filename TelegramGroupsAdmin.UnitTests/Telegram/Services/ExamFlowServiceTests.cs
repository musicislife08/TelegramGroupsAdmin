using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Test suite for ExamFlowService pure logic methods plus the safety-critical
/// EvaluateAndCompleteAsync orchestration (insert-before-delete, #515).
/// Most orchestration methods remain covered by integration tests; the
/// EvaluateAndCompleteAsync tests here exist specifically to lock in call order
/// with NSubstitute's Received.InOrder, which the DB-backed integration fixture
/// cannot assert on directly.
/// </summary>
[TestFixture]
public class ExamFlowServiceTests
{
    private const long TestChatId = -1009988776655L;
    private const long TestUserId = 555000111L;
    private const long TestSessionId = 42L;

    private ExamFlowService _service = null!;

    // Scoped services resolved via CreateAsyncScope() inside the flow.
    private IExamSessionRepository _sessionRepo = null!;
    private IReportsRepository _reportsRepo = null!;
    private IConfigService _configService = null!;
    private IManagedChatsRepository _managedChatsRepo = null!;
    private INotificationService _notificationService = null!;
    private IBotModerationService _moderationService = null!;
    private IWelcomeResponsesRepository _welcomeResponsesRepo = null!;
    private ITelegramUserRepository _telegramUserRepo = null!;

    // Constructor-level (singleton) dependencies.
    private IExamEvaluationService _examEvaluationService = null!;
    private IWelcomeAdmissionHandler _admissionHandler = null!;

    [SetUp]
    public void SetUp()
    {
        // Create service with mocked dependencies (only needed for constructor)
        var logger = NullLogger<ExamFlowService>.Instance;
        var botMessageService = Substitute.For<IBotMessageService>();
        var botDmService = Substitute.For<IBotDmService>();
        var botChatService = Substitute.For<IBotChatService>();
        _examEvaluationService = Substitute.For<IExamEvaluationService>();
        _admissionHandler = Substitute.For<IWelcomeAdmissionHandler>();

        // EvaluateAndCompleteAsync (and the methods that call it) resolve scoped
        // services via _serviceProvider.CreateAsyncScope(). Rather than faking
        // IServiceScopeFactory, build a real ServiceCollection whose singleton
        // registrations ARE these substitutes — CreateAsyncScope() then produces
        // a real scope that resolves back to the exact same substitute instances,
        // so Received.InOrder assertions below span every scope the flow opens.
        _sessionRepo = Substitute.For<IExamSessionRepository>();
        _reportsRepo = Substitute.For<IReportsRepository>();
        _configService = Substitute.For<IConfigService>();
        _managedChatsRepo = Substitute.For<IManagedChatsRepository>();
        _notificationService = Substitute.For<INotificationService>();
        _moderationService = Substitute.For<IBotModerationService>();
        _welcomeResponsesRepo = Substitute.For<IWelcomeResponsesRepository>();
        _telegramUserRepo = Substitute.For<ITelegramUserRepository>();

        var services = new ServiceCollection();
        services.AddSingleton(_sessionRepo);
        services.AddSingleton(_reportsRepo);
        services.AddSingleton(_configService);
        services.AddSingleton(_managedChatsRepo);
        services.AddSingleton(_notificationService);
        services.AddSingleton(_moderationService);
        services.AddSingleton(_welcomeResponsesRepo);
        services.AddSingleton(_telegramUserRepo);
        var serviceProvider = services.BuildServiceProvider();

        _service = new ExamFlowService(
            logger,
            serviceProvider,
            botMessageService,
            botDmService,
            botChatService,
            _examEvaluationService,
            _admissionHandler);
    }

    #region IsExamCallback Tests

    [Test]
    public void IsExamCallback_ValidPrefix_ReturnsTrue()
    {
        // Act & Assert
        Assert.That(_service.IsExamCallback("exam:123:0:1"), Is.True);
    }

    [Test]
    public void IsExamCallback_ExactPrefixOnly_ReturnsTrue()
    {
        // Act & Assert - just the prefix with no data after it
        Assert.That(_service.IsExamCallback("exam:"), Is.True);
    }

    [Test]
    public void IsExamCallback_DifferentPrefix_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            // Act & Assert
            Assert.That(_service.IsExamCallback("welcome:123"), Is.False);
            Assert.That(_service.IsExamCallback("other:data"), Is.False);
        }
    }

    [Test]
    public void IsExamCallback_EmptyString_ReturnsFalse()
    {
        // Act & Assert
        Assert.That(_service.IsExamCallback(""), Is.False);
    }

    [Test]
    public void IsExamCallback_SimilarButNotExactPrefix_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            // Act & Assert - "examination" starts with "exam" but not "exam:"
            Assert.That(_service.IsExamCallback("examination:123"), Is.False);
            Assert.That(_service.IsExamCallback("exam123"), Is.False);
        }
    }

    [Test]
    public void IsExamCallback_CaseSensitive_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            // Act & Assert - prefix is case-sensitive
            Assert.That(_service.IsExamCallback("EXAM:123:0:1"), Is.False);
            Assert.That(_service.IsExamCallback("Exam:123:0:1"), Is.False);
        }
    }

    #endregion

    #region ParseExamCallback Tests

    [Test]
    public void ParseExamCallback_ValidCallback_ReturnsCorrectValues()
    {
        // Act
        var result = _service.ParseExamCallback("exam:12345:2:3");

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Value.SessionId, Is.EqualTo(12345));
            Assert.That(result!.Value.QuestionIndex, Is.EqualTo(2));
            Assert.That(result!.Value.AnswerIndex, Is.EqualTo(3));
        }
    }

    [Test]
    public void ParseExamCallback_ZeroValues_ReturnsCorrectValues()
    {
        // Act
        var result = _service.ParseExamCallback("exam:0:0:0");

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Value.SessionId, Is.EqualTo(0));
            Assert.That(result!.Value.QuestionIndex, Is.EqualTo(0));
            Assert.That(result!.Value.AnswerIndex, Is.EqualTo(0));
        }
    }

    [Test]
    public void ParseExamCallback_LargeSessionId_ReturnsCorrectValue()
    {
        // Act - test with large session ID
        var result = _service.ParseExamCallback("exam:9223372036854775807:0:0");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SessionId, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void ParseExamCallback_WrongPrefix_ReturnsNull()
    {
        using (Assert.EnterMultipleScope())
        {
            // Act & Assert
            Assert.That(_service.ParseExamCallback("welcome:123:0:1"), Is.Null);
            Assert.That(_service.ParseExamCallback("other:123:0:1"), Is.Null);
        }
    }

    [Test]
    public void ParseExamCallback_TooFewParts_ReturnsNull()
    {
        using (Assert.EnterMultipleScope())
        {
            // Act & Assert
            Assert.That(_service.ParseExamCallback("exam:123"), Is.Null);
            Assert.That(_service.ParseExamCallback("exam:123:0"), Is.Null);
        }
    }

    [Test]
    public void ParseExamCallback_TooManyParts_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:0:1:extra"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericSessionId_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:abc:0:1"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericQuestionIndex_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:abc:1"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericAnswerIndex_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:0:abc"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NegativeValues_ReturnsCorrectValues()
    {
        // Act - negative values are technically valid parses
        var result = _service.ParseExamCallback("exam:-1:-1:-1");

        // Assert - parsing should succeed (validation is elsewhere)
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Value.SessionId, Is.EqualTo(-1));
            Assert.That(result!.Value.QuestionIndex, Is.EqualTo(-1));
            Assert.That(result!.Value.AnswerIndex, Is.EqualTo(-1));
        }
    }

    [Test]
    public void ParseExamCallback_EmptyString_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback(""), Is.Null);
    }

    [Test]
    public void ParseExamCallback_JustPrefix_ReturnsNull()
    {
        // Act & Assert - prefix with no data after colon
        Assert.That(_service.ParseExamCallback("exam:"), Is.Null);
    }

    #endregion

    #region EvaluateAndCompleteAsync Tests (#515: insert-before-delete)

    // Driven via HandleOpenEndedAnswerAsync with an open-ended-only exam config —
    // avoids needing a Telegram.Bot.Types.Message fixture and keeps the MC scoring
    // branch out of scope for these outcome-ordering tests.

    [Test]
    public async Task EvaluateAndComplete_Passed_PersistsPassedRecordBeforeSessionDelete()
    {
        // Arrange
        var user = new User { Id = TestUserId, FirstName = "Test", Username = "testuser" };
        var session = CreateOpenEndedSession("Because I want to learn and contribute.");

        _sessionRepo.GetSessionAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _sessionRepo.GetByIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(session);

        _configService.GetEffectiveWelcomeAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(CreateOpenEndedOnlyConfig()));

        _examEvaluationService.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExamEvaluationResult(Passed: true, Reasoning: "Genuine, on-topic answer", Confidence: 0.9));

        // Act
        var result = await _service.HandleOpenEndedAnswerAsync(TestChatId, user, "Because I want to learn and contribute.");

        // Assert
        Assert.That(result.Passed, Is.True);

        Received.InOrder(() =>
        {
            _reportsRepo.InsertExamResultAsync(
                Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Passed),
                Arg.Any<CancellationToken>());
            _sessionRepo.DeleteSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        });

        await _notificationService.Received(1).SendExamPassNotificationAsync(
            Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EvaluateAndComplete_Failed_PersistsFailedRecordBeforeSessionDelete()
    {
        // Arrange
        var user = new User { Id = TestUserId, FirstName = "Test", Username = "testuser" };
        var session = CreateOpenEndedSession("nah");

        _sessionRepo.GetSessionAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _sessionRepo.GetByIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(session);

        _configService.GetEffectiveWelcomeAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(CreateOpenEndedOnlyConfig()));

        _examEvaluationService.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExamEvaluationResult(Passed: false, Reasoning: "Off-topic, generic response", Confidence: 0.8));

        // Act
        var result = await _service.HandleOpenEndedAnswerAsync(TestChatId, user, "nah");

        // Assert
        Assert.That(result.Passed, Is.False);

        Received.InOrder(() =>
        {
            _reportsRepo.InsertExamResultAsync(
                Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Failed),
                Arg.Any<CancellationToken>());
            _sessionRepo.DeleteSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        });

        await _notificationService.DidNotReceiveWithAnyArgs().SendExamPassNotificationAsync(
            default!, default!, default, default, default, default,
            default, default, default, default, default);
    }

    [Test]
    public async Task EvaluateAndComplete_AiUnavailable_PersistsFailedPendingRecord()
    {
        // Arrange - EvaluateAnswerAsync returning null means the AI is unavailable;
        // the flow must force the result to review rather than silently pass.
        var user = new User { Id = TestUserId, FirstName = "Test", Username = "testuser" };
        var session = CreateOpenEndedSession("An answer the AI never gets to see.");

        _sessionRepo.GetSessionAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(session);
        _sessionRepo.GetByIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(session);

        _configService.GetEffectiveWelcomeAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(CreateOpenEndedOnlyConfig()));

        _examEvaluationService.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExamEvaluationResult?)null);

        // Act
        var result = await _service.HandleOpenEndedAnswerAsync(TestChatId, user, "An answer the AI never gets to see.");

        // Assert
        Assert.That(result.Passed, Is.False);

        await _reportsRepo.Received(1).InsertExamResultAsync(
            Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Failed),
            Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceiveWithAnyArgs().SendExamPassNotificationAsync(
            default!, default!, default, default, default, default,
            default, default, default, default, default);
    }

    private static ExamSession CreateOpenEndedSession(string openEndedAnswer) => new()
    {
        Id = TestSessionId,
        ChatId = TestChatId,
        UserId = TestUserId,
        CurrentQuestionIndex = 0,
        OpenEndedAnswer = openEndedAnswer,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    private static WelcomeConfig CreateOpenEndedOnlyConfig() => new()
    {
        TimeoutSeconds = 300,
        ExamConfig = new ExamConfig
        {
            OpenEndedQuestion = "Why do you want to join this group?",
            GroupTopic = "general discussion",
            McPassingThreshold = 50
        }
    };

    #endregion
}
