using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for ExamSessionRepository.
/// Uses Testcontainers PostgreSQL for real database testing.
/// </summary>
/// <remarks>
/// Critical tests:
/// - JSONB operations (mc_answers, shuffle_state)
/// - Session lifecycle (create, record answers, delete)
/// - Expiry handling
/// </remarks>
[TestFixture]
public class ExamSessionRepositoryTests
{
    private const long TestChatId = -1001234567890L;
    private const long TestUserId = 123456789L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IExamSessionRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IExamSessionRepository, ExamSessionRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region CreateSessionAsync Tests

    [Test]
    public async Task CreateSessionAsync_WithValidData_ReturnsSessionId()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        var sessionId = await _repository!.CreateSessionAsync(
            TestChatId, TestUserId, expiresAt);

        // Assert
        Assert.That(sessionId, Is.GreaterThan(0));
    }

    [Test]
    public async Task CreateSessionAsync_CreatesRetrievableSession()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        var sessionId = await _repository!.CreateSessionAsync(
            TestChatId, TestUserId, expiresAt);
        var session = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.That(session, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(session.UserId, Is.EqualTo(TestUserId));
            Assert.That(session.CurrentQuestionIndex, Is.EqualTo(0));
            Assert.That(session.McAnswers, Is.Null.Or.Empty);
            Assert.That(session.OpenEndedAnswer, Is.Null);
        }
    }

    #endregion

    #region GetSessionAsync Tests

    [Test]
    public async Task GetSessionAsync_WithExistingSession_ReturnsSession()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        var session = await _repository.GetSessionAsync(TestChatId, TestUserId);

        // Assert
        Assert.That(session, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(session.UserId, Is.EqualTo(TestUserId));
        }
    }

    [Test]
    public async Task GetSessionAsync_WithNoSession_ReturnsNull()
    {
        // Act
        var session = await _repository!.GetSessionAsync(TestChatId, TestUserId);

        // Assert
        Assert.That(session, Is.Null);
    }

    [Test]
    public async Task GetSessionAsync_WithExpiredSession_ReturnsSessionWithIsExpiredTrue()
    {
        // Arrange - create expired session
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); // Already expired
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        var session = await _repository.GetSessionAsync(TestChatId, TestUserId);

        // Assert - GetSessionAsync returns session regardless of expiry (caller checks IsExpired)
        // Note: HasActiveSessionAsync and GetActiveSessionsAsync DO filter by expiry
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.IsExpired, Is.True);
    }

    #endregion

    #region RecordMcAnswerAsync Tests (JSONB Operations)

    [Test]
    public async Task RecordMcAnswerAsync_RecordsAnswerAndIncrementsIndex()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        await _repository.RecordMcAnswerAsync(sessionId, 0, "A", [1, 0, 2, 3]);

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        Assert.That(session, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.CurrentQuestionIndex, Is.EqualTo(1));
            Assert.That(session.McAnswers, Is.Not.Null);
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.McAnswers!.ContainsKey(0), Is.True);
            Assert.That(session.McAnswers[0], Is.EqualTo("A"));
        }
    }

    [Test]
    public async Task RecordMcAnswerAsync_RecordsShuffleState()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);
        var shuffle = new[] { 2, 0, 3, 1 };

        // Act
        await _repository.RecordMcAnswerAsync(sessionId, 0, "B", shuffle);

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.ShuffleState, Is.Not.Null);
            Assert.That(session.ShuffleState!.ContainsKey(0), Is.True);
            Assert.That(session.ShuffleState[0], Is.EqualTo(shuffle));
        }
    }

    [Test]
    public async Task RecordMcAnswerAsync_MultipleAnswers_AccumulatesCorrectly()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act - Record 3 answers
        await _repository.RecordMcAnswerAsync(sessionId, 0, "A", [0, 1, 2, 3]);
        await _repository.RecordMcAnswerAsync(sessionId, 1, "C", [1, 2, 0, 3]);
        await _repository.RecordMcAnswerAsync(sessionId, 2, "B", [3, 2, 1, 0]);

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.CurrentQuestionIndex, Is.EqualTo(3));
            Assert.That(session.McAnswers!.Count, Is.EqualTo(3));
            Assert.That(session.McAnswers[0], Is.EqualTo("A"));
            Assert.That(session.McAnswers[1], Is.EqualTo("C"));
            Assert.That(session.McAnswers[2], Is.EqualTo("B"));
        }
    }

    #endregion

    #region RecordOpenEndedAnswerAsync Tests

    [Test]
    public async Task RecordOpenEndedAnswerAsync_RecordsAnswer()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        await _repository.RecordOpenEndedAnswerAsync(sessionId, "I love coding!");

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        Assert.That(session!.OpenEndedAnswer, Is.EqualTo("I love coding!"));
    }

    [Test]
    public async Task RecordOpenEndedAnswerAsync_PreservesUnicode()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);
        var unicodeAnswer = "I love coding! 🚀 Привет мир 中文";

        // Act
        await _repository.RecordOpenEndedAnswerAsync(sessionId, unicodeAnswer);

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        Assert.That(session!.OpenEndedAnswer, Is.EqualTo(unicodeAnswer));
    }

    #endregion

    #region DeleteSessionAsync Tests

    [Test]
    public async Task DeleteSessionAsync_ById_RemovesSession()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var sessionId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        await _repository.DeleteSessionAsync(sessionId);

        // Assert
        var session = await _repository.GetByIdAsync(sessionId);
        Assert.That(session, Is.Null);
    }

    [Test]
    public async Task DeleteSessionAsync_ByChatAndUser_RemovesSession()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        await _repository.DeleteSessionAsync(TestChatId, TestUserId);

        // Assert
        var session = await _repository.GetSessionAsync(TestChatId, TestUserId);
        Assert.That(session, Is.Null);
    }

    #endregion

    #region DeleteExpiredSessionsAsync Tests

    [Test]
    public async Task DeleteExpiredSessionsAsync_DeletesOnlyExpiredSessions()
    {
        // Arrange - create one expired, one active session
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var activeTime = DateTimeOffset.UtcNow.AddMinutes(5);

        var expiredId = await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiredTime);
        var activeId = await _repository.CreateSessionAsync(TestChatId, TestUserId + 1, activeTime);

        // Act
        var deleted = await _repository.DeleteExpiredSessionsAsync();

        // Assert
        Assert.That(deleted, Is.EqualTo(1));

        var expiredSession = await _repository.GetByIdAsync(expiredId);
        var activeSession = await _repository.GetByIdAsync(activeId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(expiredSession, Is.Null, "Expired session should be deleted");
            Assert.That(activeSession, Is.Not.Null, "Active session should remain");
        }
    }

    #endregion

    #region HasActiveSessionAsync Tests

    [Test]
    public async Task HasActiveSessionAsync_WithActiveSession_ReturnsTrue()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act
        var hasSession = await _repository.HasActiveSessionAsync(TestChatId, TestUserId);

        // Assert
        Assert.That(hasSession, Is.True);
    }

    [Test]
    public async Task HasActiveSessionAsync_WithNoSession_ReturnsFalse()
    {
        // Act
        var hasSession = await _repository!.HasActiveSessionAsync(TestChatId, TestUserId);

        // Assert
        Assert.That(hasSession, Is.False);
    }

    [Test]
    public async Task HasActiveSessionAsync_WithExpiredSession_ReturnsFalse()
    {
        // Arrange
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiredTime);

        // Act
        var hasSession = await _repository.HasActiveSessionAsync(TestChatId, TestUserId);

        // Assert
        Assert.That(hasSession, Is.False);
    }

    #endregion

    #region GetActiveSessionForUserAsync Tests

    [Test]
    public async Task GetActiveSessionForUserAsync_WithActiveSession_ReturnsSession()
    {
        // Arrange - create session in one chat
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);

        // Act - find by user only (for DM handling)
        var session = await _repository.GetActiveSessionForUserAsync(TestUserId);

        // Assert
        Assert.That(session, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(session.UserId, Is.EqualTo(TestUserId));
        }
    }

    [Test]
    public async Task GetActiveSessionForUserAsync_WithNoSession_ReturnsNull()
    {
        // Act
        var session = await _repository!.GetActiveSessionForUserAsync(TestUserId);

        // Assert
        Assert.That(session, Is.Null);
    }

    #endregion

    #region GetActiveSessionsAsync Tests

    [Test]
    public async Task GetActiveSessionsAsync_ReturnsAllActiveSessionsForChat()
    {
        // Arrange - create multiple sessions in same chat
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository!.CreateSessionAsync(TestChatId, TestUserId, expiresAt);
        await _repository.CreateSessionAsync(TestChatId, TestUserId + 1, expiresAt);
        await _repository.CreateSessionAsync(TestChatId, TestUserId + 2, expiresAt);

        // Act
        var sessions = await _repository.GetActiveSessionsAsync(TestChatId);

        // Assert
        Assert.That(sessions, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetActiveSessionsAsync_ExcludesExpiredSessions()
    {
        // Arrange
        var activeTime = DateTimeOffset.UtcNow.AddMinutes(5);
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-1);

        await _repository!.CreateSessionAsync(TestChatId, TestUserId, activeTime);
        await _repository.CreateSessionAsync(TestChatId, TestUserId + 1, expiredTime);

        // Act
        var sessions = await _repository.GetActiveSessionsAsync(TestChatId);

        // Assert
        Assert.That(sessions, Has.Count.EqualTo(1));
        Assert.That(sessions[0].UserId, Is.EqualTo(TestUserId));
    }

    #endregion
}
