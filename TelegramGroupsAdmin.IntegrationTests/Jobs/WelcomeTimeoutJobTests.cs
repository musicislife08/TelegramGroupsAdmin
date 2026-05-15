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
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.IntegrationTests.Jobs;

/// <summary>
/// Integration tests for WelcomeTimeoutJob.
/// Uses the canonical golden template clone for DB state; Telegram API
/// dependencies (IBotModerationService, IBotMessageService) are mocked.
/// </summary>
[TestFixture]
public class WelcomeTimeoutJobTests
{
    // ── canonical anchors ─────────────────────────────────────────────────────
    // Synthetic welcome-flow target user. Canonical telegram_users carries this
    // row; canonical welcome_responses 999001..999005 are pinned to it.
    private const long WelcomeUserId = 9196379650113L;
    private const long MainChatId = -100026957614982L;

    // welcome_responses anchors (canonical id → welcome_message_id → response):
    //   999001 → 99001 → Pending
    //   999002 → 99002 → Accepted
    //   999003 → 99003 → Denied
    //   999004 → 99004 → Timeout
    //   999005 → 99005 → Left
    private const int PendingWelcomeMsgId = 99001;
    private const int AcceptedWelcomeMsgId = 99002;
    private const int DeniedWelcomeMsgId = 99003;
    private const int TimeoutWelcomeMsgId = 99004;
    private const int LeftWelcomeMsgId = 99005;

    // Deliberately not in canonical — used by "no matching row" tests.
    private const int NonExistentWelcomeMsgId = 99099;

    // Second-most-active canonical chat; carries no welcome_responses for
    // WelcomeUserId, so payloads addressed here against the canonical Pending
    // anchor's user/message-id miss the WHERE clause.
    private const long WorkshopAlumniChatId = -100059667856554L;

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
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

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

    private WelcomeTimeoutJob BuildJob() =>
        new WelcomeTimeoutJob(
            _mockLogger!,
            ContextFactory,
            _mockModerationService!,
            _mockMessageService!,
            _mockExamSessionRepository!,
            new JobMetrics(),
            new WelcomeMetrics());

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

    private static WelcomeTimeoutPayload BuildPayload(
        long chatId = MainChatId,
        long userId = WelcomeUserId,
        int welcomeMessageId = PendingWelcomeMsgId) =>
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
        // Arrange — payload targets a welcome_message_id deliberately absent from canonical
        var payload = BuildPayload(welcomeMessageId: NonExistentWelcomeMsgId);
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert — no kick, but welcome message cleanup still runs
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());

        await _mockMessageService!
            .Received(1)
            .DeleteAndMarkMessageAsync(
                MainChatId,
                NonExistentWelcomeMsgId,
                "welcome_timeout_cleanup",
                Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the WelcomeResponse row exists but has already transitioned out of Pending
    /// (e.g., Accepted), the job should skip — the user has already responded.
    /// </summary>
    [TestCase(WelcomeResponseType.Accepted, AcceptedWelcomeMsgId)]
    [TestCase(WelcomeResponseType.Denied, DeniedWelcomeMsgId)]
    [TestCase(WelcomeResponseType.Left, LeftWelcomeMsgId)]
    [TestCase(WelcomeResponseType.Timeout, TimeoutWelcomeMsgId)]
    public async Task Execute_ResponseNotPending_EarlyReturn_NoKick(
        WelcomeResponseType responseType,
        int welcomeMessageId)
    {
        // Arrange — canonical already carries the pre-baked non-Pending row
        var payload = BuildPayload(welcomeMessageId: welcomeMessageId);
        var context = BuildJobContext(payload);
        var job = BuildJob();

        // Act
        await job.Execute(context);

        // Assert — no kick
        await _mockModerationService!
            .DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());

        // Assert — the pre-existing non-Pending status was NOT mutated to Timeout
        await using var verifyContext = ContextFactory.CreateDbContext();
        var actualResponse = await verifyContext.WelcomeResponses
            .Where(r => r.ChatId == MainChatId
                        && r.UserId == WelcomeUserId
                        && r.WelcomeMessageId == welcomeMessageId)
            .Select(r => r.Response)
            .FirstAsync();
        Assert.That(actualResponse, Is.EqualTo(responseType),
            "Non-Pending row should not be mutated by the job");
    }

    /// <summary>
    /// When a Pending response exists, the job must kick the user, delete the welcome
    /// message, and persist a Timeout response with an updated RespondedAt timestamp.
    /// </summary>
    [Test]
    public async Task Execute_ResponsePending_KicksUserAndUpdatesResponse()
    {
        // Arrange — canonical 999001 is Pending at (MainChatId, WelcomeUserId, 99001)
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
                    intent.User.Id == WelcomeUserId
                    && intent.Chat.Id == MainChatId),
                Arg.Any<CancellationToken>());

        // Assert — welcome message deletion was called with correct chatId and messageId
        await _mockMessageService!
            .Received(1)
            .DeleteAndMarkMessageAsync(
                MainChatId,
                PendingWelcomeMsgId,
                "welcome_timeout",
                Arg.Any<CancellationToken>());

        // Assert — database row is now Timeout with a fresh RespondedAt
        await using var verifyContext = ContextFactory.CreateDbContext();
        var updated = await verifyContext.WelcomeResponses
            .Where(r => r.ChatId == MainChatId
                        && r.UserId == WelcomeUserId
                        && r.WelcomeMessageId == PendingWelcomeMsgId)
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
        // Arrange — canonical 999001 is Pending; force the kick to throw
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
                MainChatId,
                PendingWelcomeMsgId,
                "welcome_timeout",
                Arg.Any<CancellationToken>());

        // Assert — response row transitioned to Timeout regardless of kick outcome
        await using var verifyContext = ContextFactory.CreateDbContext();
        var updated = await verifyContext.WelcomeResponses
            .Where(r => r.ChatId == MainChatId
                        && r.UserId == WelcomeUserId
                        && r.WelcomeMessageId == PendingWelcomeMsgId)
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
        // Arrange — canonical 999001 Pending row lives in MainChat; query a different chat
        var payload = BuildPayload(chatId: WorkshopAlumniChatId);
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
        // Arrange — canonical 999001 Pending row is at welcome_message_id 99001; query 99099
        var payload = BuildPayload(welcomeMessageId: NonExistentWelcomeMsgId);
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
        // Arrange — canonical 999001 Pending + mock returns active exam session
        _mockExamSessionRepository!
            .HasActiveSessionAsync(MainChatId, WelcomeUserId, Arg.Any<CancellationToken>())
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
            .Where(r => r.ChatId == MainChatId
                        && r.UserId == WelcomeUserId
                        && r.WelcomeMessageId == PendingWelcomeMsgId)
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
        // Arrange — canonical 999001 Pending; default mock returns no active exam session
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
                    intent.User.Id == WelcomeUserId
                    && intent.Chat.Id == MainChatId),
                Arg.Any<CancellationToken>());
    }
}
