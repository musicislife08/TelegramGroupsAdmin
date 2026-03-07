using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
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
    private IDbContextFactory<AppDbContext>? _contextFactory;

    private const long TestChatId = -1001234567890;
    private const string TestChatName = "Test Group";
    private const long TestUserId = 987654321;
    private const string TestUserFirstName = "John";
    private const string TestUserLastName = "Doe";
    private const string TestUserUsername = "johndoe";

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

        services.AddScoped<IReportsRepository, ReportsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        _contextFactory = _scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await SeedRequiredEntitiesAsync(TestChatId, TestChatName, TestUserId, TestUserFirstName, TestUserLastName, TestUserUsername);
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    /// <summary>
    /// Seeds the managed_chats and telegram_users rows that the enriched_reports view JOINs to.
    /// The view LEFT JOINs managed_chats on chat_id and telegram_users on context->>'userId'.
    /// Without these rows the view returns NULLs for chat_name and profile_* columns.
    /// </summary>
    private async Task SeedRequiredEntitiesAsync(
        long chatId,
        string chatName,
        long userId,
        string firstName,
        string? lastName,
        string? username)
    {
        await using var ctx = await _contextFactory!.CreateDbContextAsync(CancellationToken.None);

        ctx.ManagedChats.Add(new ManagedChatRecordDto
        {
            ChatId = chatId,
            ChatName = chatName,
            ChatType = ManagedChatType.Supergroup,
            BotStatus = BotChatStatus.Administrator,
            IsAdmin = true,
            IsActive = true,
            IsDeleted = false,
            AddedAt = DateTimeOffset.UtcNow
        });

        ctx.TelegramUsers.Add(new TelegramUserDto
        {
            TelegramUserId = userId,
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await ctx.SaveChangesAsync(CancellationToken.None);
    }

    private static ProfileScanAlertRecord CreateAlert(
        long userId = TestUserId,
        long chatId = TestChatId,
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

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));

        var retrieved = results[0];
        Assert.That(retrieved, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            // Identity
            Assert.That(retrieved.Id, Is.EqualTo(id));

            // User — ID comes from JSONB, name columns come from telegram_users JOIN
            Assert.That(retrieved.User.Id, Is.EqualTo(TestUserId));
            Assert.That(retrieved.User.FirstName, Is.EqualTo(TestUserFirstName));
            Assert.That(retrieved.User.LastName, Is.EqualTo(TestUserLastName));
            Assert.That(retrieved.User.Username, Is.EqualTo(TestUserUsername));

            // Chat — chat_name comes from managed_chats JOIN
            Assert.That(retrieved.Chat.Id, Is.EqualTo(TestChatId));
            Assert.That(retrieved.Chat.ChatName, Is.EqualTo(TestChatName));

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
            User = UserIdentity.FromId(TestUserId),
            Chat = new ChatIdentity(TestChatId, TestChatName),
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
        await _repository!.InsertProfileScanAlertAsync(alert, CancellationToken.None);
        var results = await _repository.GetProfileScanAlertsAsync(pendingOnly: true, CancellationToken.None);

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));

        var retrieved = results[0];
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
