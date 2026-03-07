using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating exam failure records in the database for E2E testing.
/// Uses IReportsRepository to create unified review entries with Type=ExamFailure.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var examFailure = await new TestExamFailureBuilder(Factory.Services)
///     .WithUser(123456789, "testuser", "Test", "User")
///     .InChat(chat)
///     .WithScore(50, 80) // 50% score, 80% threshold (failed)
///     .WithMcAnswers(new Dictionary&lt;int, string&gt; { { 0, "A" }, { 1, "B" } })
///     .WithOpenEndedAnswer("I want to join", "FAIL - Too short")
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestExamFailureBuilder
{
    private readonly IServiceProvider _services;

    private long _userId;
    private long _chatId;
    private int _score = 50;
    private int _passingThreshold = 80;
    private Dictionary<int, string>? _mcAnswers;
    private Dictionary<int, int[]>? _shuffleState;
    private string? _openEndedAnswer;
    private string? _aiEvaluation;
    private DateTimeOffset _failedAt = DateTimeOffset.UtcNow;
    private string? _reviewedBy;
    private DateTimeOffset? _reviewedAt;
    private string? _actionTaken;
    private string? _adminNotes;

    // Denormalized display fields
    private string? _userName;
    private string? _userFirstName;
    private string? _userLastName;
    private string? _userPhotoPath;
    private string? _chatName;

    public TestExamFailureBuilder(IServiceProvider services)
    {
        _services = services;
        // Generate random user ID and chat ID by default
        _userId = Random.Shared.NextInt64(100_000_000, 999_999_999);
        _chatId = -Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999);

        // Default MC answers and shuffle state (simulates a user who got 1/2 correct)
        _mcAnswers = new Dictionary<int, string> { { 0, "A" }, { 1, "B" } };
        _shuffleState = new Dictionary<int, int[]>
        {
            { 0, [0, 1, 2, 3] }, // Q1: A=correct
            { 1, [1, 0, 2, 3] }  // Q2: B=correct (user selected B, which is wrong in shuffled order)
        };
    }

    /// <summary>
    /// Sets the user who failed the exam.
    /// </summary>
    public TestExamFailureBuilder WithUser(long userId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _userId = userId;
        _userName = username;
        _userFirstName = firstName ?? "Test";
        _userLastName = lastName ?? "User";
        return this;
    }

    /// <summary>
    /// Sets the chat where the exam was taken.
    /// </summary>
    public TestExamFailureBuilder InChat(long chatId, string? chatName = null)
    {
        _chatId = chatId;
        _chatName = chatName;
        return this;
    }

    /// <summary>
    /// Sets the chat using a TestChat.
    /// </summary>
    public TestExamFailureBuilder InChat(TestChat chat)
    {
        _chatId = chat.ChatId;
        _chatName = chat.ChatName;
        return this;
    }

    /// <summary>
    /// Sets the exam score and passing threshold.
    /// </summary>
    public TestExamFailureBuilder WithScore(int score, int passingThreshold = 80)
    {
        _score = score;
        _passingThreshold = passingThreshold;
        return this;
    }

    /// <summary>
    /// Sets as a passing score (still recorded for review).
    /// </summary>
    public TestExamFailureBuilder AsPassing()
    {
        _score = 100;
        _passingThreshold = 80;
        return this;
    }

    /// <summary>
    /// Sets as a failing score.
    /// </summary>
    public TestExamFailureBuilder AsFailing()
    {
        _score = 50;
        _passingThreshold = 80;
        return this;
    }

    /// <summary>
    /// Sets the MC answers and shuffle state.
    /// </summary>
    public TestExamFailureBuilder WithMcAnswers(Dictionary<int, string> answers, Dictionary<int, int[]>? shuffleState = null)
    {
        _mcAnswers = answers;
        _shuffleState = shuffleState ?? new Dictionary<int, int[]>
        {
            { 0, [0, 1, 2, 3] },
            { 1, [0, 1, 2, 3] }
        };
        return this;
    }

    /// <summary>
    /// Sets the open-ended answer and AI evaluation.
    /// </summary>
    public TestExamFailureBuilder WithOpenEndedAnswer(string answer, string? aiEvaluation = null)
    {
        _openEndedAnswer = answer;
        _aiEvaluation = aiEvaluation;
        return this;
    }

    /// <summary>
    /// Sets the open-ended answer with a passing AI evaluation.
    /// </summary>
    public TestExamFailureBuilder WithPassingOpenEnded(string answer = "I am genuinely interested in this community")
    {
        _openEndedAnswer = answer;
        _aiEvaluation = "PASS - The answer demonstrates genuine interest and understanding of the group's purpose.";
        return this;
    }

    /// <summary>
    /// Sets the open-ended answer with a failing AI evaluation.
    /// </summary>
    public TestExamFailureBuilder WithFailingOpenEnded(string answer = "idk")
    {
        _openEndedAnswer = answer;
        _aiEvaluation = "FAIL - Response is too short and does not demonstrate genuine interest.";
        return this;
    }

    /// <summary>
    /// Sets the failure timestamp.
    /// </summary>
    public TestExamFailureBuilder FailedAt(DateTimeOffset timestamp)
    {
        _failedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Marks as reviewed with the specified action.
    /// </summary>
    public TestExamFailureBuilder AsReviewed(string reviewedBy, string actionTaken, string? adminNotes = null)
    {
        _reviewedBy = reviewedBy;
        _reviewedAt = DateTimeOffset.UtcNow;
        _actionTaken = actionTaken;
        _adminNotes = adminNotes;
        return this;
    }

    /// <summary>
    /// Marks as approved.
    /// </summary>
    public TestExamFailureBuilder AsApproved(string reviewedBy)
    {
        return AsReviewed(reviewedBy, "approved");
    }

    /// <summary>
    /// Marks as denied.
    /// </summary>
    public TestExamFailureBuilder AsDenied(string reviewedBy)
    {
        return AsReviewed(reviewedBy, "denied");
    }

    /// <summary>
    /// Marks as denied and banned.
    /// </summary>
    public TestExamFailureBuilder AsDeniedAndBanned(string reviewedBy)
    {
        return AsReviewed(reviewedBy, "denied_ban");
    }

    /// <summary>
    /// Sets the user's photo path.
    /// </summary>
    public TestExamFailureBuilder WithUserPhoto(string photoPath)
    {
        _userPhotoPath = photoPath;
        return this;
    }

    /// <summary>
    /// Builds and persists the exam failure to the database.
    /// </summary>
    public async Task<TestExamFailure> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var reportsRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

        var examFailure = new ExamFailureRecord
        {
            Id = 0, // Will be assigned by database
            McAnswers = _mcAnswers,
            ShuffleState = _shuffleState,
            OpenEndedAnswer = _openEndedAnswer,
            Score = _score,
            PassingThreshold = _passingThreshold,
            AiEvaluation = _aiEvaluation,
            FailedAt = _failedAt,
            ReviewedBy = _reviewedBy,
            ReviewedAt = _reviewedAt,
            ActionTaken = _actionTaken,
            AdminNotes = _adminNotes,
            // Identity objects
            User = new UserIdentity(_userId, _userFirstName, _userLastName, _userName),
            Chat = new ChatIdentity(_chatId, _chatName),
            UserPhotoPath = _userPhotoPath
        };

        var id = await reportsRepository.InsertExamFailureAsync(examFailure, cancellationToken);

        // InsertExamFailureAsync always creates with Pending status
        // If this exam failure is already reviewed, update the status separately
        if (_reviewedAt.HasValue && !string.IsNullOrEmpty(_reviewedBy) && !string.IsNullOrEmpty(_actionTaken))
        {
            await reportsRepository.UpdateStatusAsync(
                id,
                ReportStatus.Reviewed,
                _reviewedBy,
                _actionTaken,
                _adminNotes,
                cancellationToken);
        }

        return new TestExamFailure(
            Id: (int)id,
            UserId: _userId,
            ChatId: _chatId,
            Score: _score,
            PassingThreshold: _passingThreshold,
            FailedAt: _failedAt,
            ActionTaken: _actionTaken
        );
    }
}

/// <summary>
/// Represents a test exam failure for E2E testing.
/// </summary>
public record TestExamFailure(
    int Id,
    long UserId,
    long ChatId,
    int Score,
    int PassingThreshold,
    DateTimeOffset FailedAt,
    string? ActionTaken
);
