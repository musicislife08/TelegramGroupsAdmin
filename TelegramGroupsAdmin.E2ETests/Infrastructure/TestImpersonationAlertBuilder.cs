using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating impersonation alerts in the database for E2E testing.
/// Uses IReviewsRepository to create unified review entries with Type=ImpersonationAlert.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var alert = await new TestImpersonationAlertBuilder(Factory.Services)
///     .WithSuspectedUser(123456789, "scammer")
///     .WithTargetUser(987654321, "admin")
///     .InChat(chat)
///     .WithRiskLevel(ImpersonationRiskLevel.Critical)
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestImpersonationAlertBuilder
{
    private readonly IServiceProvider _services;

    private long _suspectedUserId;
    private long _targetUserId;
    private long _chatId;
    private int _totalScore = 50;
    private ImpersonationRiskLevel _riskLevel = ImpersonationRiskLevel.Medium;
    private bool _nameMatch = true;
    private bool _photoMatch;
    private double? _photoSimilarityScore;
    private DateTimeOffset _detectedAt = DateTimeOffset.UtcNow;
    private bool _autoBanned;
    private string? _reviewedByUserId;
    private DateTimeOffset? _reviewedAt;
    private ImpersonationVerdict? _verdict;

    // Denormalized display fields
    private string? _suspectedUserName;
    private string? _suspectedFirstName;
    private string? _suspectedLastName;
    private string? _targetUserName;
    private string? _targetFirstName;
    private string? _targetLastName;
    private string? _chatName;

    public TestImpersonationAlertBuilder(IServiceProvider services)
    {
        _services = services;
        // Generate random user IDs by default
        _suspectedUserId = Random.Shared.NextInt64(100_000_000, 999_999_999);
        _targetUserId = Random.Shared.NextInt64(100_000_000, 999_999_999);
        _chatId = -Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999);
    }

    /// <summary>
    /// Sets the suspected impersonator user.
    /// </summary>
    public TestImpersonationAlertBuilder WithSuspectedUser(long userId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _suspectedUserId = userId;
        _suspectedUserName = username;
        _suspectedFirstName = firstName ?? "Suspected";
        _suspectedLastName = lastName ?? "User";
        return this;
    }

    /// <summary>
    /// Sets the target admin being impersonated.
    /// </summary>
    public TestImpersonationAlertBuilder WithTargetUser(long userId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _targetUserId = userId;
        _targetUserName = username;
        _targetFirstName = firstName ?? "Admin";
        _targetLastName = lastName ?? "User";
        return this;
    }

    /// <summary>
    /// Sets the chat where impersonation was detected.
    /// </summary>
    public TestImpersonationAlertBuilder InChat(long chatId, string? chatName = null)
    {
        _chatId = chatId;
        _chatName = chatName;
        return this;
    }

    /// <summary>
    /// Sets the chat using a TestChat.
    /// </summary>
    public TestImpersonationAlertBuilder InChat(TestChat chat)
    {
        _chatId = chat.ChatId;
        _chatName = chat.ChatName;
        return this;
    }

    /// <summary>
    /// Sets the risk level and adjusts score accordingly.
    /// </summary>
    public TestImpersonationAlertBuilder WithRiskLevel(ImpersonationRiskLevel riskLevel)
    {
        _riskLevel = riskLevel;
        _totalScore = riskLevel == ImpersonationRiskLevel.Critical ? 100 : 50;
        return this;
    }

    /// <summary>
    /// Sets the total impersonation score.
    /// </summary>
    public TestImpersonationAlertBuilder WithScore(int score)
    {
        _totalScore = score;
        return this;
    }

    /// <summary>
    /// Sets name match detected.
    /// </summary>
    public TestImpersonationAlertBuilder WithNameMatch()
    {
        _nameMatch = true;
        return this;
    }

    /// <summary>
    /// Sets photo match detected with optional similarity score.
    /// </summary>
    public TestImpersonationAlertBuilder WithPhotoMatch(double? similarityScore = null)
    {
        _photoMatch = true;
        _photoSimilarityScore = similarityScore ?? 0.95;
        return this;
    }

    /// <summary>
    /// Sets as critical risk (both name and photo match).
    /// </summary>
    public TestImpersonationAlertBuilder AsCritical()
    {
        _riskLevel = ImpersonationRiskLevel.Critical;
        _totalScore = 100;
        _nameMatch = true;
        _photoMatch = true;
        _photoSimilarityScore = 0.95;
        _autoBanned = true;
        return this;
    }

    /// <summary>
    /// Sets the detection timestamp.
    /// </summary>
    public TestImpersonationAlertBuilder DetectedAt(DateTimeOffset timestamp)
    {
        _detectedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Marks as auto-banned.
    /// </summary>
    public TestImpersonationAlertBuilder AsAutoBanned()
    {
        _autoBanned = true;
        return this;
    }

    /// <summary>
    /// Marks as reviewed with a specific verdict.
    /// </summary>
    public TestImpersonationAlertBuilder AsReviewed(string reviewedByUserId, ImpersonationVerdict verdict)
    {
        _reviewedByUserId = reviewedByUserId;
        _reviewedAt = DateTimeOffset.UtcNow;
        _verdict = verdict;
        return this;
    }

    /// <summary>
    /// Marks as confirmed scam.
    /// </summary>
    public TestImpersonationAlertBuilder AsConfirmedScam(string reviewedByUserId)
    {
        return AsReviewed(reviewedByUserId, ImpersonationVerdict.ConfirmedScam);
    }

    /// <summary>
    /// Marks as dismissed.
    /// </summary>
    public TestImpersonationAlertBuilder AsDismissed(string reviewedByUserId)
    {
        return AsReviewed(reviewedByUserId, ImpersonationVerdict.FalsePositive);
    }

    /// <summary>
    /// Marks as whitelisted.
    /// </summary>
    public TestImpersonationAlertBuilder AsWhitelisted(string reviewedByUserId)
    {
        return AsReviewed(reviewedByUserId, ImpersonationVerdict.Whitelisted);
    }

    /// <summary>
    /// Builds and persists the impersonation alert to the database.
    /// </summary>
    public async Task<TestImpersonationAlert> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var reviewsRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

        var alert = new ImpersonationAlertRecord
        {
            Id = 0, // Will be assigned by database
            SuspectedUser = new UserIdentity(_suspectedUserId, _suspectedFirstName, _suspectedLastName, _suspectedUserName),
            TargetUser = new UserIdentity(_targetUserId, _targetFirstName, _targetLastName, _targetUserName),
            Chat = new ChatIdentity(_chatId, _chatName),
            TotalScore = _totalScore,
            RiskLevel = _riskLevel,
            NameMatch = _nameMatch,
            PhotoMatch = _photoMatch,
            PhotoSimilarityScore = _photoSimilarityScore,
            DetectedAt = _detectedAt,
            AutoBanned = _autoBanned,
            ReviewedByUserId = _reviewedByUserId,
            ReviewedAt = _reviewedAt,
            Verdict = _verdict
        };

        var id = await reviewsRepository.InsertImpersonationAlertAsync(alert, cancellationToken);

        return new TestImpersonationAlert(
            Id: (int)id,
            SuspectedUserId: _suspectedUserId,
            TargetUserId: _targetUserId,
            ChatId: _chatId,
            RiskLevel: _riskLevel,
            TotalScore: _totalScore,
            DetectedAt: _detectedAt,
            AutoBanned: _autoBanned,
            Verdict: _verdict
        );
    }
}

/// <summary>
/// Represents a test impersonation alert for E2E testing.
/// </summary>
public record TestImpersonationAlert(
    int Id,
    long SuspectedUserId,
    long TargetUserId,
    long ChatId,
    ImpersonationRiskLevel RiskLevel,
    int TotalScore,
    DateTimeOffset DetectedAt,
    bool AutoBanned,
    ImpersonationVerdict? Verdict
);
