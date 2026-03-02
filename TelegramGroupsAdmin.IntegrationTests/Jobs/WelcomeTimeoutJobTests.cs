using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.IntegrationTests.Jobs;

/// <summary>
/// Integration tests for WelcomeTimeoutJob.
/// Validates the full job execution path using real PostgreSQL (via Testcontainers)
/// for database state assertions. Telegram API dependencies (IBotModerationService,
/// IBotMessageService) are mocked with NSubstitute.
/// </summary>
[TestFixture]
public class WelcomeTimeoutJobTests
{
    // ── test constants ────────────────────────────────────────────────────────
    private const long TestChatId = -1001234567890L;
    private const long TestUserId = 987654321L;
    private const int TestWelcomeMessageId = 42;

    // ── infrastructure ────────────────────────────────────────────────────────
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    // ── mocks ─────────────────────────────────────────────────────────────────
    private IBotModerationService? _mockModerationService;
    private IBotMessageService? _mockMessageService;
    private IExamSessionRepository? _mockExamSessionRepository;
    private ILogger<WelcomeTimeoutJob>? _mockLogger;

    // ── helper properties ─────────────────────────────────────────────────────
    private IDbContextFactory<AppDbContext> ContextFactory =>
        _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        _serviceProvider = services.BuildServiceProvider();

        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockExamSessionRepository = Substitute.For<IExamSessionRepository>();
        _mockLogger = Substitute.For<ILogger<WelcomeTimeoutJob>>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures a TelegramUserDto parent row exists for the given userId.
    /// Required because welcome_responses has an FK to telegram_users.
    /// </summary>
    private async Task EnsureTelegramUserExistsAsync(long userId)
    {
        await using var context = ContextFactory.CreateDbContext();
        var exists = await context.TelegramUsers.AnyAsync(u => u.TelegramUserId == userId);
        if (exists) return;

        context.TelegramUsers.Add(new TelegramUserDto
        {
            TelegramUserId = userId,
            FirstName = "Test",
            LastName = "User",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a WelcomeResponseDto directly via AppDbContext and returns its generated ID.
    /// Automatically creates the prerequisite telegram_users row if needed.
    /// </summary>
    private async Task<long> SeedWelcomeResponseAsync(
        WelcomeResponseType responseType,
        long chatId = TestChatId,
        long userId = TestUserId,
        int welcomeMessageId = TestWelcomeMessageId)
    {
        await EnsureTelegramUserExistsAsync(userId);

        await using var context = ContextFactory.CreateDbContext();
        var dto = new WelcomeResponseDto
        {
            ChatId = chatId,
            UserId = userId,
            WelcomeMessageId = welcomeMessageId,
            Response = responseType,
            RespondedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.WelcomeResponses.Add(dto);
        await context.SaveChangesAsync();
        return dto.Id;
    }

    /// <summary>
    /// Builds a WelcomeTimeoutJob using the real ContextFactory and the mock services.
    /// </summary>
    private WelcomeTimeoutJob BuildJob() =>
        new WelcomeTimeoutJob(
            _mockLogger!,
            ContextFactory,
            _mockModerationService!,
            _mockMessageService!,
            _mockExamSessionRepository!);

    /// <summary>
    /// Creates a mock IJobExecutionContext whose MergedJobDataMap contains the given payload.
    /// </summary>
    private static IJobExecutionContext BuildJobContext(WelcomeTimeoutPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);

        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, payloadJson } };

        var trigger = Substitute.For<ITrigger>();
        trigger.Key.Returns(new TriggerKey("test-trigger", "test-group"));

        var scheduler = Substitute.For<IScheduler>();
        scheduler.UnscheduleJob(Arg.Any<TriggerKey>()).Returns(true);

        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(jobDataMap);
        context.Trigger.Returns(trigger);
        context.Scheduler.Returns(scheduler);
        context.CancellationToken.Returns(CancellationToken.None);

        return context;
    }

    /// <summary>
    /// Convenience payload for the standard test user/chat/message triple.
    /// </summary>
    private static WelcomeTimeoutPayload BuildPayload(
        long chatId = TestChatId,
        long userId = TestUserId,
        int welcomeMessageId = TestWelcomeMessageId) =>
        new WelcomeTimeoutPayload(
            User: UserIdentity.FromId(userId),
            Chat: ChatIdentity.FromId(chatId),
            WelcomeMessageId: welcomeMessageId);

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When no WelcomeResponse row exists for the payload triple, the job should exit
    /// early without touching Telegram API services.
    /// </summary>
    [Test]
    public async Task Execute_ResponseNotFound_EarlyReturn_NoKick()
    {
        // Arrange — no database row seeded
        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());

        await _mockMessageService!
            .DidNotReceive()
            .DeleteAndMarkMessageAsync(
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the WelcomeResponse row exists but has already transitioned out of Pending
    /// (e.g., Accepted), the job should skip — the user has already responded.
    /// </summary>
    [TestCase(WelcomeResponseType.Accepted)]
    [TestCase(WelcomeResponseType.Denied)]
    [TestCase(WelcomeResponseType.Left)]
    [TestCase(WelcomeResponseType.Timeout)]
    public async Task Execute_ResponseNotPending_EarlyReturn_NoKick(WelcomeResponseType responseType)
    {
        // Arrange
        await SeedWelcomeResponseAsync(responseType);

        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When a Pending response exists, the job must kick the user, delete the welcome
    /// message, and persist a Timeout response with an updated RespondedAt timestamp.
    /// </summary>
    [Test]
    public async Task Execute_ResponsePending_KicksUserAndUpdatesResponse()
    {
        // Arrange
        await SeedWelcomeResponseAsync(WelcomeResponseType.Pending);

        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        var beforeExecution = DateTimeOffset.UtcNow;

        // Act
        await job.Execute(context);

        // Assert — Telegram kick was called with matching user/chat identity
        await _mockModerationService!
            .Received(1)
            .KickUserFromChatAsync(
                Arg.Is<KickIntent>(intent =>
                    intent.User.Id == TestUserId
                    && intent.Chat.Id == TestChatId),
                Arg.Any<CancellationToken>());

        // Assert — welcome message deletion was called with correct chatId and messageId
        await _mockMessageService!
            .Received(1)
            .DeleteAndMarkMessageAsync(
                TestChatId,
                TestWelcomeMessageId,
                "welcome_timeout",
                Arg.Any<CancellationToken>());

        // Assert — database row is now Timeout with a fresh RespondedAt
        await using var context2 = ContextFactory.CreateDbContext();
        var updated = await context2.WelcomeResponses
            .Where(r => r.ChatId == TestChatId
                        && r.UserId == TestUserId
                        && r.WelcomeMessageId == TestWelcomeMessageId)
            .FirstOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.Not.Null, "WelcomeResponse row should still exist");
            Assert.That(updated!.Response, Is.EqualTo(WelcomeResponseType.Timeout),
                "Response should be updated to Timeout");
            Assert.That(updated.RespondedAt, Is.GreaterThanOrEqualTo(beforeExecution),
                "RespondedAt should reflect the timeout timestamp");
        }
    }

    /// <summary>
    /// When KickUserFromChatAsync throws (e.g., user already left), the job must still
    /// delete the welcome message and record the Timeout response.  The kick failure is
    /// logged and swallowed by the job; it must not prevent cleanup.
    /// </summary>
    [Test]
    public async Task Execute_KickThrows_StillDeletesMessageAndUpdatesResponse()
    {
        // Arrange
        await SeedWelcomeResponseAsync(WelcomeResponseType.Pending);

        _mockModerationService!
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Telegram API error: user not found"));

        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act — must NOT propagate the kick exception
        Assert.DoesNotThrowAsync(async () => await job.Execute(context));

        // Assert — message deletion still called despite the kick failure
        await _mockMessageService!
            .Received(1)
            .DeleteAndMarkMessageAsync(
                TestChatId,
                TestWelcomeMessageId,
                "welcome_timeout",
                Arg.Any<CancellationToken>());

        // Assert — response row transitioned to Timeout regardless of kick outcome
        await using var verifyContext = ContextFactory.CreateDbContext();
        var updated = await verifyContext.WelcomeResponses
            .Where(r => r.ChatId == TestChatId
                        && r.UserId == TestUserId
                        && r.WelcomeMessageId == TestWelcomeMessageId)
            .FirstOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.Not.Null, "WelcomeResponse row should still exist");
            Assert.That(updated!.Response, Is.EqualTo(WelcomeResponseType.Timeout),
                "Response should still be updated to Timeout even when kick fails");
        }
    }

    /// <summary>
    /// Validates that the job matches on the full (ChatId, UserId, WelcomeMessageId) triple.
    /// A pending response for the SAME user in a DIFFERENT chat must be ignored.
    /// </summary>
    [Test]
    public async Task Execute_PendingRowForDifferentChat_EarlyReturn_NoKick()
    {
        // Arrange — seed a pending row with a different chat ID
        const long differentChatId = -9999999999L;
        await SeedWelcomeResponseAsync(WelcomeResponseType.Pending, chatId: differentChatId);

        // Payload targets the original chat — no matching row
        var payload = BuildPayload(chatId: TestChatId);
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Validates that the job matches on the full (ChatId, UserId, WelcomeMessageId) triple.
    /// A pending response for the SAME chat and user but a DIFFERENT message ID must be ignored.
    /// </summary>
    [Test]
    public async Task Execute_PendingRowForDifferentMessageId_EarlyReturn_NoKick()
    {
        // Arrange — seed a pending row with a different welcome message ID
        const int differentMessageId = 9999;
        await SeedWelcomeResponseAsync(
            WelcomeResponseType.Pending,
            welcomeMessageId: differentMessageId);

        // Payload targets the original message — no matching row
        var payload = BuildPayload(welcomeMessageId: TestWelcomeMessageId);
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When a Pending response exists but the user has an active exam session,
    /// the job should defer to the exam flow and NOT kick the user.
    /// </summary>
    [Test]
    public async Task Execute_PendingWithActiveExamSession_DefersToExamFlow_NoKick()
    {
        // Arrange — pending welcome response + active exam session
        await SeedWelcomeResponseAsync(WelcomeResponseType.Pending);

        _mockExamSessionRepository!
            .HasActiveSessionAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert — no kick, no message deletion
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());

        await _mockMessageService!
            .DidNotReceive()
            .DeleteAndMarkMessageAsync(
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

        // Assert — welcome response remains Pending (not changed to Timeout)
        await using var verifyContext = ContextFactory.CreateDbContext();
        var response = await verifyContext.WelcomeResponses
            .Where(r => r.ChatId == TestChatId
                        && r.UserId == TestUserId
                        && r.WelcomeMessageId == TestWelcomeMessageId)
            .FirstOrDefaultAsync();

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Response, Is.EqualTo(WelcomeResponseType.Pending),
            "Response should remain Pending when exam session is active");
    }

    /// <summary>
    /// When a Pending response exists but the user does NOT have an active exam session,
    /// the job should proceed with kick as normal (exam session check returns false).
    /// </summary>
    [Test]
    public async Task Execute_PendingWithNoExamSession_KicksUser()
    {
        // Arrange — pending welcome response, no active exam session (default mock returns false)
        await SeedWelcomeResponseAsync(WelcomeResponseType.Pending);

        var payload = BuildPayload();
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert — kick was called
        await _mockModerationService!
            .Received(1)
            .KickUserFromChatAsync(
                Arg.Is<KickIntent>(intent =>
                    intent.User.Id == TestUserId
                    && intent.Chat.Id == TestChatId),
                Arg.Any<CancellationToken>());
    }
}
