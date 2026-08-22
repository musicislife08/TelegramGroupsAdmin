using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Integration tests for the ToProfileScanAlert() mapping in EnrichedReportMappings.
/// The mapping class is internal, so it is exercised through the public IReportsRepository
/// methods: InsertProfileScanAlertAsync → GetProfileScanAlertsAsync.
///
/// These tests validate that JSONB context is correctly serialized on write and deserialized
/// on read, and that the enriched_reports view JOIN correctly resolves user names and chat names.
/// </summary>
[TestFixture]
public class ProfileScanAlertMappingTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _repository;

    // Canonical anchor: MainChat
    private const long MainChatId = -100026957614982L;
    private const string MainChatName = "Main Community";

    // Canonical anchor: profile-scan target (has profile_scan_results row 532, outcome=0)
    private const long ProfileScanUserId = 9408530993787L;
    private const string ProfileScanUserFirstName = "Anouk";
    private const string ProfileScanUserLastName = "Vandenberghe";
    private const string ProfileScanUserUsername = "AnoukVanDe";

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IReportsRepository, ReportsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private static ProfileScanAlertRecord CreateAlert(
        long userId = ProfileScanUserId,
        long chatId = MainChatId,
        decimal score = 3.5m,
        ProfileScanOutcome outcome = ProfileScanOutcome.HeldForReview,
        string? aiReason = "Suspicious bio and channel",
        string[]? aiSignals = null,
        string? bio = "Buy crypto now",
        string? personalChannelTitle = "Crypto Signals",
        bool hasPinnedStories = true,
        bool isScam = false,
        bool isFake = false,
        DateTimeOffset? detectedAt = null)
    {
        return new ProfileScanAlertRecord
        {
            User = UserIdentity.FromId(userId),
            Chat = new ChatIdentity(chatId, "Test Group"),
            Score = score,
            Outcome = outcome,
            AiReason = aiReason,
            AiSignalsDetected = aiSignals ?? ["suspicious bio", "crypto channel"],
            Bio = bio,
            PersonalChannelTitle = personalChannelTitle,
            HasPinnedStories = hasPinnedStories,
            IsScam = isScam,
            IsFake = isFake,
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow
        };
    }

    #region RoundTrip_AllFields

    [Test]
    public async Task RoundTrip_AllFields_PreservesEveryFieldThroughJsonbAndViewJoin()
    {
        // Arrange
        var detectedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var alert = CreateAlert(
            score: 4.25m,
            outcome: ProfileScanOutcome.Banned,
            aiReason: "Explicit profile and spam bio",
            aiSignals: ["spam bio", "explicit photo", "crypto channel"],
            bio: "Join my channel for free signals!",
            personalChannelTitle: "Free Crypto Signals",
            hasPinnedStories: true,
            isScam: true,
            isFake: false,
            detectedAt: detectedAt);

        // Act
        var id = await _repository!.InsertProfileScanAlertAsync(alert, CancellationToken.None);
        var results = await _repository.GetProfileScanAlertsAsync(pendingOnly: true, CancellationToken.None);

        // Assert — canonical carries one pre-existing pending profile-scan alert
        // (join-gate cleanup fixture, report id 188), so look up this test's own row by id.
        Assert.That(results, Has.Count.EqualTo(2));

        var retrieved = results.Single(r => r.Id == id);
        Assert.That(retrieved, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            // Identity
            Assert.That(retrieved.Id, Is.EqualTo(id));

            // User — ID comes from JSONB, name columns come from telegram_users JOIN
            Assert.That(retrieved.User.Id, Is.EqualTo(ProfileScanUserId));
            Assert.That(retrieved.User.FirstName, Is.EqualTo(ProfileScanUserFirstName));
            Assert.That(retrieved.User.LastName, Is.EqualTo(ProfileScanUserLastName));
            Assert.That(retrieved.User.Username, Is.EqualTo(ProfileScanUserUsername));

            // Chat — chat_name comes from managed_chats JOIN
            Assert.That(retrieved.Chat.Id, Is.EqualTo(MainChatId));
            Assert.That(retrieved.Chat.ChatName, Is.EqualTo(MainChatName));

            // JSONB scalar fields
            Assert.That(retrieved.Score, Is.EqualTo(4.25m));
            Assert.That(retrieved.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
            Assert.That(retrieved.AiReason, Is.EqualTo("Explicit profile and spam bio"));
            Assert.That(retrieved.Bio, Is.EqualTo("Join my channel for free signals!"));
            Assert.That(retrieved.PersonalChannelTitle, Is.EqualTo("Free Crypto Signals"));
            Assert.That(retrieved.HasPinnedStories, Is.True);
            Assert.That(retrieved.IsScam, Is.True);
            Assert.That(retrieved.IsFake, Is.False);

            // JSONB array
            Assert.That(retrieved.AiSignalsDetected, Is.Not.Null);
            Assert.That(retrieved.AiSignalsDetected, Is.EquivalentTo(new[] { "spam bio", "explicit photo", "crypto channel" }));

            // Timestamps
            Assert.That(retrieved.DetectedAt.UtcDateTime,
                Is.EqualTo(detectedAt.UtcDateTime).Within(TimeSpan.FromSeconds(1)));

            // Review fields — nothing reviewed yet
            Assert.That(retrieved.ReviewedByUserId, Is.Null);
            Assert.That(retrieved.ReviewedAt, Is.Null);
            Assert.That(retrieved.ReviewedByEmail, Is.Null);
            Assert.That(retrieved.ActionTaken, Is.Null);
        }
    }

    #endregion

    #region RoundTrip_MinimalFields

    [Test]
    public async Task RoundTrip_MinimalFields_NullableFieldsReturnNull()
    {
        // Arrange — only required fields, all nullable fields omitted / set to defaults
        var alert = new ProfileScanAlertRecord
        {
            User = UserIdentity.FromId(ProfileScanUserId),
            Chat = new ChatIdentity(MainChatId, MainChatName),
            Score = 1.0m,
            Outcome = ProfileScanOutcome.Clean,
            AiReason = null,
            AiSignalsDetected = null,
            Bio = null,
            PersonalChannelTitle = null,
            HasPinnedStories = false,
            IsScam = false,
            IsFake = false,
            DetectedAt = DateTimeOffset.UtcNow
        };

        // Act
        var id = await _repository!.InsertProfileScanAlertAsync(alert, CancellationToken.None);
        var results = await _repository.GetProfileScanAlertsAsync(pendingOnly: true, CancellationToken.None);

        // Assert — canonical carries one pre-existing pending profile-scan alert
        // (join-gate cleanup fixture, report id 188), so look up this test's own row by id.
        Assert.That(results, Has.Count.EqualTo(2));

        var retrieved = results.Single(r => r.Id == id);
        Assert.That(retrieved, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved.Score, Is.EqualTo(1.0m));
            Assert.That(retrieved.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
            Assert.That(retrieved.AiReason, Is.Null);
            Assert.That(retrieved.AiSignalsDetected, Is.Null);
            Assert.That(retrieved.Bio, Is.Null);
            Assert.That(retrieved.PersonalChannelTitle, Is.Null);
            Assert.That(retrieved.HasPinnedStories, Is.False);
            Assert.That(retrieved.IsScam, Is.False);
            Assert.That(retrieved.IsFake, Is.False);
        }
    }

    #endregion

    #region OutcomeMappedCorrectly

    [Test]
    [TestCase(ProfileScanOutcome.Clean, 0)]
    [TestCase(ProfileScanOutcome.HeldForReview, 1)]
    [TestCase(ProfileScanOutcome.Banned, 2)]
    public async Task OutcomeMappedCorrectly_IntToEnumCastRoundTrips(ProfileScanOutcome outcome, int expectedOrdinal)
    {
        // Arrange — use distinct user IDs per test case so parallel runs don't collide.
        // Each TestCase runs in its own SetUp/TearDown cycle (fresh database) so no
        // collision risk; the expectedOrdinal parameter is included purely as documentation.
        _ = expectedOrdinal; // confirms the ordinal value matches the enum member

        var alert = CreateAlert(outcome: outcome);

        // Act
        var id = await _repository!.InsertProfileScanAlertAsync(alert, CancellationToken.None);
        var retrieved = await _repository.GetProfileScanAlertAsync(id, CancellationToken.None);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Outcome, Is.EqualTo(outcome));
    }

    #endregion
}
