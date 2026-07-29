using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using global::Telegram.Bot.Types;
using global::Telegram.Bot.Types.Enums;
using global::Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for WelcomeService.HandleChatMemberUpdateAsync.
///
/// Strategy: All 18 dependencies are substituted. Telegram.Bot concrete types
/// (ChatMemberUpdated, User, Chat, ChatMemberMember, etc.) are created via direct
/// object initialization — NSubstitute cannot intercept their non-virtual members.
///
/// TelegramPhotoService is a concrete class with file-system side effects.
/// It is constructed with mocked IBotMediaService and IBotChatService dependencies,
/// using Testably.Abstractions for a fake file system so no real disk I/O occurs.
/// </summary>
[TestFixture]
public class WelcomeServiceTests
{
    private const long TestUserId = 111_222_333L;
    private const long TestChatId = -100_987_654_321L;

    // --- Substituted dependencies ---
    private IConfigService _configService = null!;
    private IWelcomeResponsesRepository _welcomeResponsesRepository = null!;
    private ITelegramUserRepository _telegramUserRepository = null!;
    private IExamFlowService _examFlowService = null!;
    private IImpersonationDetectionService _impersonationDetectionService = null!;
    private IBotProtectionService _botProtectionService = null!;
    private IBotDmService _dmDeliveryService = null!;
    private IBotMessageService _messageService = null!;
    private IBotUserService _userService = null!;
    private IBotChatService _chatService = null!;
    private IBotModerationService _moderationService = null!;
    private IJobScheduler _jobScheduler = null!;
    private ICasCheckService _casCheckService = null!;
    private ITelegramSessionManager _sessionManager = null!;
    private IProfileScanGate _profileScanGate = null!;
    private IWelcomeAdmissionHandler _admissionHandler = null!;
    private IUsernameBlacklistService _usernameBlacklistService = null!;
    private IWelcomeBypassResolver _bypassResolver = null!;
    private IAuditHandler _auditHandler = null!;

    // TelegramPhotoService is concrete — built with mocked sub-dependencies.
    private TelegramPhotoService _photoService = null!;

    private WelcomeService _sut = null!;

    // Reusable test user (not a bot, not an admin)
    private static readonly User TestUser = new()
    {
        Id = TestUserId,
        FirstName = "Alice",
        Username = "alice_tg",
        IsBot = false
    };

    private static readonly TelegramUser NonBannedTelegramUser = new(
        TelegramUserId: TestUserId,
        Username: "alice_tg",
        FirstName: "Alice",
        LastName: null,
        UserPhotoPath: null,
        PhotoHash: null,
        PhotoFileUniqueId: null,
        IsBot: false,
        IsTrusted: false,
        IsBanned: false,
        KickCount: 0,
        BotDmEnabled: false,
        FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-1),
        LastSeenAt: DateTimeOffset.UtcNow,
        CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt: DateTimeOffset.UtcNow
    );

    private static readonly TelegramUser BannedTelegramUser = NonBannedTelegramUser with { IsBanned = true };

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _welcomeResponsesRepository = Substitute.For<IWelcomeResponsesRepository>();
        _telegramUserRepository = Substitute.For<ITelegramUserRepository>();
        _examFlowService = Substitute.For<IExamFlowService>();
        _impersonationDetectionService = Substitute.For<IImpersonationDetectionService>();
        _botProtectionService = Substitute.For<IBotProtectionService>();
        _dmDeliveryService = Substitute.For<IBotDmService>();
        _messageService = Substitute.For<IBotMessageService>();
        _userService = Substitute.For<IBotUserService>();
        _chatService = Substitute.For<IBotChatService>();
        _moderationService = Substitute.For<IBotModerationService>();
        _jobScheduler = Substitute.For<IJobScheduler>();
        _casCheckService = Substitute.For<ICasCheckService>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();
        _profileScanGate = Substitute.For<IProfileScanGate>();
        _admissionHandler = Substitute.For<IWelcomeAdmissionHandler>();
        _usernameBlacklistService = Substitute.For<IUsernameBlacklistService>();
        _bypassResolver = Substitute.For<IWelcomeBypassResolver>();
        _auditHandler = Substitute.For<IAuditHandler>();

        // Build TelegramPhotoService with mocked sub-dependencies so it never touches the real
        // file system. GetUserPhotoWithMetadataAsync calls IBotMediaService, which is mocked to
        // return zero photos, causing the method to return null immediately.
        var mockMediaService = Substitute.For<IBotMediaService>();
        var mockChatServiceForPhoto = Substitute.For<IBotChatService>();
        var mockAppOptions = Microsoft.Extensions.Options.Options.Create(
            new AppOptions { DataPath = System.IO.Path.GetTempPath() });
        _photoService = new TelegramPhotoService(
            NullLogger<TelegramPhotoService>.Instance,
            mockMediaService,
            mockChatServiceForPhoto,
            mockAppOptions);

        // --- Default mock behaviours ---

        // Config always returns WelcomeConfig.Default (enabled, ChatAcceptDeny mode)
        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(WelcomeConfig.Default);

        // User is a regular member (not admin) by default
        _userService
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ChatMemberMember { User = TestUser });

        // User exists and is not banned
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(NonBannedTelegramUser);

        // Bot protection allows bots by default
        _botProtectionService
            .ShouldAllowBotAsync(Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<ChatMemberUpdated?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Mute (RestrictUserAsync) succeeds
        _moderationService
            .RestrictUserAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        // SyncBanToChatAsync succeeds
        _moderationService
            .SyncBanToChatAsync(Arg.Any<SyncBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        // SendAndSaveMessageAsync returns a minimal Message so verifyingMessageId is set (string overload)
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 42, Chat = new Chat { Id = TestChatId } });

        // SendAndSaveMessageAsync returns a minimal Message so verifyingMessageId is set (entity overload)
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<TelegramMessage>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 42, Chat = new Chat { Id = TestChatId } });

        // IBotMediaService returns zero photos (photo service returns null immediately)
        mockMediaService
            .GetUserProfilePhotosAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfilePhotos { TotalCount = 0, Photos = [] });

        _sut = new WelcomeService(
            _configService,
            _welcomeResponsesRepository,
            _telegramUserRepository,
            _examFlowService,
            _impersonationDetectionService,
            _botProtectionService,
            _dmDeliveryService,
            _messageService,
            _userService,
            _chatService,
            _moderationService,
            _jobScheduler,
            _casCheckService,
            _usernameBlacklistService,
            _photoService,
            _profileScanGate,
            _admissionHandler,
            _bypassResolver,
            _auditHandler,
            new WelcomeMetrics(),
            new ChatMetrics(Substitute.For<IChatCache>()),
            NullLogger<WelcomeService>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _sessionManager.DisposeAsync();
    }

    #region Helpers

    /// <summary>
    /// Creates a ChatMemberUpdated representing a status transition.
    /// Defaults to the standard join scenario: Left → Member.
    ///
    /// Pass <paramref name="user"/> or <paramref name="chat"/> to supply a full object, or
    /// use the individual-field overrides (<paramref name="userId"/>, <paramref name="username"/>,
    /// <paramref name="firstName"/>, <paramref name="chatId"/>, <paramref name="chatTitle"/>) to
    /// build an ad-hoc user/chat without constructing the objects at the call site.
    /// Individual-field overrides are ignored when the corresponding object parameter is supplied.
    /// </summary>
    private static ChatMemberUpdated CreateJoinUpdate(
        User? user = null,
        Chat? chat = null,
        ChatMemberStatus oldStatus = ChatMemberStatus.Left,
        ChatMemberStatus newStatus = ChatMemberStatus.Member,
        long userId = TestUserId,
        string? username = "alice_tg",
        string firstName = "Alice",
        long chatId = TestChatId,
        string chatTitle = "Test Group")
    {
        var resolvedUser = user ?? new User
        {
            Id = userId,
            FirstName = firstName,
            Username = username,
            IsBot = false
        };
        var resolvedChat = chat ?? new Chat
        {
            Id = chatId,
            Type = ChatType.Supergroup,
            Title = chatTitle
        };

        ChatMember oldMember = oldStatus switch
        {
            ChatMemberStatus.Member     => new ChatMemberMember     { User = resolvedUser },
            ChatMemberStatus.Restricted => new ChatMemberRestricted { User = resolvedUser },
            ChatMemberStatus.Left       => new ChatMemberLeft       { User = resolvedUser },
            ChatMemberStatus.Kicked     => new ChatMemberBanned     { User = resolvedUser },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = resolvedUser },
            ChatMemberStatus.Creator    => new ChatMemberOwner      { User = resolvedUser },
            _                           => new ChatMemberLeft       { User = resolvedUser }
        };

        ChatMember newMember = newStatus switch
        {
            ChatMemberStatus.Member     => new ChatMemberMember     { User = resolvedUser },
            ChatMemberStatus.Restricted => new ChatMemberRestricted { User = resolvedUser },
            ChatMemberStatus.Left       => new ChatMemberLeft       { User = resolvedUser },
            ChatMemberStatus.Kicked     => new ChatMemberBanned     { User = resolvedUser },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = resolvedUser },
            ChatMemberStatus.Creator    => new ChatMemberOwner      { User = resolvedUser },
            _                           => new ChatMemberMember     { User = resolvedUser }
        };

        return new ChatMemberUpdated
        {
            Chat = resolvedChat,
            From = resolvedUser,
            Date = DateTime.UtcNow,
            OldChatMember = oldMember,
            NewChatMember = newMember
        };
    }

    #endregion

    #region Test 1: Banned user — sync ban and return early

    [Test]
    public async Task HandleChatMemberUpdate_BannedUser_SyncsBanAndReturnsEarly()
    {
        // Arrange — repository returns a globally banned user
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(BannedTelegramUser);

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — SyncBanToChatAsync must be called once with a SyncBanIntent
        await _moderationService.Received(1).SyncBanToChatAsync(
            Arg.Is<SyncBanIntent>(i =>
                i!.User.Id == TestUserId &&
                i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());

        // Early-exit: mute (RestrictUserAsync) must NOT be called
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());

        // Early-exit: CAS check must NOT be called
        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<UserIdentity>(), Arg.Any<TelegramGroupsAdmin.Configuration.Models.Welcome.CasConfig>(),
            Arg.Any<CancellationToken>());

        // Early-exit: profile scan must NOT be called
        await _profileScanGate.DidNotReceive().ScanIfEligibleAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 2: Normal user — mute step is reached

    [Test]
    public async Task HandleChatMemberUpdate_NormalUser_ProceedsToMuteStep()
    {
        // Arrange — default setup: non-banned user, non-admin status
        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — restrict (mute) must be called exactly once via RestrictUserAsync
        await _moderationService.Received(1).RestrictUserAsync(
            Arg.Is<RestrictIntent>(i =>
                i!.User.Id == TestUserId &&
                i.Chat != null &&
                i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 3: User leaving — GetOrCreateAsync is never called

    [Test]
    public async Task HandleChatMemberUpdate_UserLeaving_HandlesLeaveNotJoin()
    {
        // Arrange — Member → Left (user leaving)
        var update = CreateJoinUpdate(
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Left);

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — the leave path must not attempt to fetch/create a user record
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        // No mute should happen either
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 4: Bot joining — bot protection is consulted, user record is not created

    [Test]
    public async Task HandleChatMemberUpdate_BotJoining_ChecksBotProtection()
    {
        // Arrange — joining user is a bot
        var botUser = new User
        {
            Id = 888_000_111L,
            FirstName = "TestBot",
            Username = "testbot",
            IsBot = true
        };

        _botProtectionService
            .ShouldAllowBotAsync(Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<ChatMemberUpdated?>(), Arg.Any<CancellationToken>())
            .Returns(true); // allowed bot — skip welcome, return early

        var update = CreateJoinUpdate(user: botUser);

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — bot protection consulted exactly once
        await _botProtectionService.Received(1).ShouldAllowBotAsync(
            Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<ChatMemberUpdated?>(), Arg.Any<CancellationToken>());

        // Human join path must not execute — user record must not be fetched
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_DisallowedBotJoining_BansBotAndNeverCreatesUserRecord()
    {
        // Arrange — bot is NOT whitelisted
        var disallowedBot = new User
        {
            Id = 777_000_222L,
            FirstName = "SpamBot",
            Username = "spambot",
            IsBot = true
        };

        _botProtectionService
            .ShouldAllowBotAsync(Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<ChatMemberUpdated?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var update = CreateJoinUpdate(user: disallowedBot);

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — bot protection called, then ban executed
        await _botProtectionService.Received(1).ShouldAllowBotAsync(
            Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<ChatMemberUpdated?>(), Arg.Any<CancellationToken>());

        await _botProtectionService.Received(1).BanBotAsync(
            Arg.Any<Chat>(), Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // User record path must not execute
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 5: Admin/Owner joining — bypass path skips mute and security checks

    [Test]
    public async Task HandleChatMemberUpdate_AdminJoining_BypassesWelcomeViaResolver()
    {
        // Arrange — resolver flags the user as a Telegram chat admin/creator
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Admin, "test-admin"));

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — bypass short-circuits before mute, CAS, and profile scan
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());

        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<UserIdentity>(), Arg.Any<TelegramGroupsAdmin.Configuration.Models.Welcome.CasConfig>(),
            Arg.Any<CancellationToken>());

        await _profileScanGate.DidNotReceive().ScanIfEligibleAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_OwnerJoining_BypassesWelcomeViaResolver()
    {
        // Arrange — resolver treats owner as ChatAdmin decision (Rule 1 covers both)
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Admin, "test-admin"));

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — owner also short-circuits before mute
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Additional edge cases

    [Test]
    public async Task HandleChatMemberUpdate_NonJoinStatusTransition_IsIgnored()
    {
        // Arrange — Restricted → Member is NOT a join (old must be Left or Kicked)
        var update = CreateJoinUpdate(
            oldStatus: ChatMemberStatus.Restricted,
            newStatus: ChatMemberStatus.Member);

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — the early-return guard must fire, nothing processed
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BannedUser_SyncBanIntentCarriesCorrectIdentities()
    {
        // Arrange
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(BannedTelegramUser);

        var update = CreateJoinUpdate();

        SyncBanIntent? capturedIntent = null;
        _moderationService
            .SyncBanToChatAsync(
                Arg.Do<SyncBanIntent>(i => capturedIntent = i),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — intent must carry correct user and chat identities
        Assert.That(capturedIntent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedIntent!.User.Id, Is.EqualTo(TestUserId));
            Assert.That(capturedIntent.Chat.Id, Is.EqualTo(TestChatId));
        }
    }

    #endregion

    #region Bypass path (Step 2.5)

    [Test]
    public async Task HandleChatMemberUpdate_BypassChatAdmin_SkipsSecurityAndConsentFlow()
    {
        // Arrange — resolver decides ChatAdmin bypass
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Admin, "test-admin"));

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — mute and security checks are skipped
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());

        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<UserIdentity>(), Arg.Any<TelegramGroupsAdmin.Configuration.Models.Welcome.CasConfig>(),
            Arg.Any<CancellationToken>());

        await _profileScanGate.DidNotReceive().ScanIfEligibleAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>());

        // User activated and bypass audited
        await _telegramUserRepository.Received(1).ActivateAsync(TestUserId, Arg.Any<CancellationToken>());
        await _auditHandler.Received(1).LogWelcomeBypassAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity>(),
            BypassDecision.Admin,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BypassTrusted_PostsAnnouncementAndSchedulesDelete()
    {
        // Arrange — Trusted bypass, announcement configured with text + positive TTL
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        var config = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessageTrusted = "hello {username}",
                AnnouncementTtlSeconds = 30
            }
        };
        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(config);

        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<TelegramMessage>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 5001, Chat = new Chat { Id = TestChatId } });

        // Act
        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Assert — announcement sent via entity overload containing the configured body text
        await _messageService.Received(1).SendAndSaveMessageAsync(
            chatId: TestChatId,
            message: Arg.Is<TelegramMessage>(m => m!.Text.Contains("hello")),
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        // And its auto-delete is scheduled
        await _jobScheduler.Received(1).ScheduleJobAsync(
            BackgroundJobNames.DeleteMessage,
            Arg.Is<DeleteMessagePayload>(p => p!.ChatId == TestChatId && p.MessageId == 5001),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BypassTrusted_EmptyMessage_DoesNotPostAnnouncement()
    {
        // Arrange — Trusted bypass, announcement message is whitespace-only
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                Enabled = true,
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageTrusted = "  ",
                    AnnouncementTtlSeconds = 30
                }
            });

        // Act
        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Assert — no announcement posted (entity overload), no delete scheduled
        await _messageService.DidNotReceive().SendAndSaveMessageAsync(
            Arg.Any<long>(), Arg.Any<TelegramMessage>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());

        await _jobScheduler.DidNotReceive().ScheduleJobAsync(
            Arg.Any<string>(),
            Arg.Any<DeleteMessagePayload>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BypassTrusted_ZeroTtl_PostsAnnouncementAndSchedulesImmediateDelete()
    {
        // Arrange — Trusted bypass, TTL is zero. New semantics: zero TTL means the
        // announcement is still posted but the delete job fires immediately (ttl=0).
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                Enabled = true,
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageTrusted = "x",
                    AnnouncementTtlSeconds = 0
                }
            });

        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<TelegramMessage>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 5002, Chat = new Chat { Id = TestChatId } });

        // Act
        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Assert — announcement IS posted via entity overload and delete IS scheduled at 0s delay.
        await _messageService.Received(1).SendAndSaveMessageAsync(
            chatId: TestChatId,
            message: Arg.Any<TelegramMessage>(),
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        await _jobScheduler.Received(1).ScheduleJobAsync(
            BackgroundJobNames.DeleteMessage,
            Arg.Is<DeleteMessagePayload>(p => p!.ChatId == TestChatId && p.MessageId == 5002),
            0,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BypassTrusted_ToggleDisabled_DoesNotPostAnnouncement()
    {
        // Arrange — Trusted bypass resolves, but the TrustedBypass toggle is OFF.
        // The announcement must not fire regardless of template/TTL contents.
        _bypassResolver
            .ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                Enabled = true,
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = false,
                    AnnouncementMessageTrusted = "hello {username}",
                    AnnouncementTtlSeconds = 30
                }
            });

        // Act
        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Assert — toggle OFF → no announcement posted (entity overload), no delete scheduled.
        await _messageService.DidNotReceive().SendAndSaveMessageAsync(
            Arg.Any<long>(), Arg.Any<TelegramMessage>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());

        await _jobScheduler.DidNotReceive().ScheduleJobAsync(
            Arg.Any<string>(),
            Arg.Any<DeleteMessagePayload>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_PreBanned_BeatsBypass_BansAndReturns()
    {
        // Arrange — pre-banned user. Resolver should not be consulted at all.
        _telegramUserRepository
            .GetOrCreateAsync(Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(BannedTelegramUser);

        // Act
        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Assert — resolver never called; ban is synced
        await _bypassResolver.DidNotReceive().ResolveAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());

        await _moderationService.Received(1).SyncBanToChatAsync(
            Arg.Any<SyncBanIntent>(), Arg.Any<CancellationToken>());

        // And bypass is not audited
        await _auditHandler.DidNotReceive().LogWelcomeBypassAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<BypassDecision>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Bypass announcement substitution via entity builder (migration from ParseMode.Html)

    /// <summary>
    /// Seeds WelcomeConfig with TrustedBypass enabled and the given templates,
    /// overriding the default config stub from SetUp.
    /// </summary>
    private void SetupTrustedBypass(string adminTemplate, string trustedTemplate)
    {
        var config = new WelcomeConfig
        {
            MainWelcomeMessage = "welcome",
            TrustedBypass =
            {
                Enabled = true,
                AnnouncementMessageAdmin = adminTemplate,
                AnnouncementMessageTrusted = trustedTemplate,
                AnnouncementTtlSeconds = 30,
            }
        };
        _configService.GetEffectiveWelcomeAsync(Arg.Any<long>()).Returns(config);

        // Return a non-null Message so the delete-schedule branch runs (string overload).
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9001, Chat = new Chat { Id = TestChatId } });

        // Return a non-null Message so the delete-schedule branch runs (entity overload).
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<TelegramMessage>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9001, Chat = new Chat { Id = TestChatId } });
    }

    [Test]
    public async Task PostBypassAnnouncement_Substitutes_Username_AsTextMentionEntity_Trusted()
    {
        // Arrange: trusted user with username "alice", template uses {username}. After migration
        // the mention is expressed as a TextMention entity — NOT embedded as @alice in the text.
        SetupTrustedBypass(
            adminTemplate: TrustedBypassConfig.UsernameVariable + " arrived",
            trustedTemplate: TrustedBypassConfig.UsernameVariable + " arrived");
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        TelegramMessage? capturedMessage = null;
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(),
                Arg.Do<TelegramMessage>(m => capturedMessage = m),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9001, Chat = new Chat { Id = TestChatId } });

        await _sut.HandleChatMemberUpdateAsync(
            CreateJoinUpdate(userId: 1L, username: "alice", firstName: "Alice"),
            CancellationToken.None);

        Assert.That(capturedMessage, Is.Not.Null);
        // The literal "arrived" suffix is in the text
        Assert.That(capturedMessage!.Text, Does.Contain(" arrived"));
        // A TextMention entity exists for user 1
        Assert.That(
            capturedMessage.Entities,
            Has.Some.Matches<MessageEntity>(e =>
                e.Type == MessageEntityType.TextMention && e.User!.Id == 1L));
        // No raw @mention or HTML in the text
        Assert.That(capturedMessage.Text, Does.Not.Contain("@alice"));
        Assert.That(capturedMessage.Text, Does.Not.Contain("<a href"));
    }

    [Test]
    public async Task PostBypassAnnouncement_Substitutes_ChatName_AsPlainText()
    {
        // Arrange: trusted bypass with a chat title that previously needed HTML-encoding.
        // After migration there is no HTML parser, so the title appears verbatim in the text.
        SetupTrustedBypass(
            adminTemplate: "Joined " + TrustedBypassConfig.ChatNameVariable,
            trustedTemplate: "Joined " + TrustedBypassConfig.ChatNameVariable);
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        var update = CreateJoinUpdate(chatId: 99L, chatTitle: "<b>pwn</b>");

        TelegramMessage? capturedMessage = null;
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(),
                Arg.Do<TelegramMessage>(m => capturedMessage = m),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9002, Chat = new Chat { Id = 99L } });

        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        Assert.That(capturedMessage, Is.Not.Null);
        // Chat title appears verbatim — no HTML parser, no encoding needed
        Assert.That(capturedMessage!.Text, Does.Contain("<b>pwn</b>"),
            "Chat title is plain text — no HTML encoding applied");
        Assert.That(capturedMessage.Text, Does.Not.Contain("&lt;b&gt;"),
            "No HTML entity escaping in entity-based messages");
    }

    [Test]
    public async Task PostBypassAnnouncement_UserWithNoUsername_UsesTextMentionEntity()
    {
        // Arrange: user with NO username — mention entity still carries the hostile first name
        // as display text inside the entity, not as raw text/HTML in the message body.
        SetupTrustedBypass(
            adminTemplate: TrustedBypassConfig.UsernameVariable + " joined",
            trustedTemplate: TrustedBypassConfig.UsernameVariable + " joined");
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        TelegramMessage? capturedMessage = null;
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(),
                Arg.Do<TelegramMessage>(m => capturedMessage = m),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9003, Chat = new Chat { Id = TestChatId } });

        var update = CreateJoinUpdate(userId: 7L, username: null, firstName: "<b>FAKE ADMIN</b>");
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        Assert.That(capturedMessage, Is.Not.Null);
        // A TextMention entity exists for user 7 — no HTML anchor tag in the raw text
        Assert.That(
            capturedMessage!.Entities,
            Has.Some.Matches<MessageEntity>(e =>
                e.Type == MessageEntityType.TextMention && e.User!.Id == 7L),
            "TextMention entity required for user without username");
        Assert.That(capturedMessage.Text, Does.Not.Contain("<a href"),
            "No raw HTML anchor tag in entity-based message");
    }

    [Test]
    public async Task PostBypassAnnouncement_UsesEntityOverload_WithTextMentionForUser()
    {
        // Arrange: trusted bypass with a user that has a username. After migration the
        // announcement must be sent via the TelegramMessage entity overload, carry a
        // TextMention entity for the user, and not contain any raw HTML <a href> markup.
        SetupTrustedBypass(
            adminTemplate: TrustedBypassConfig.UsernameVariable + " arrived",
            trustedTemplate: TrustedBypassConfig.UsernameVariable + " arrived");
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        TelegramMessage? capturedMessage = null;
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(),
                Arg.Do<TelegramMessage>(m => capturedMessage = m),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9099, Chat = new Chat { Id = TestChatId } });

        await _sut.HandleChatMemberUpdateAsync(
            CreateJoinUpdate(userId: 1L, username: "alice", firstName: "Alice"),
            CancellationToken.None);

        // Entity overload was called and captured a message
        Assert.That(capturedMessage, Is.Not.Null, "Entity overload of SendAndSaveMessageAsync must be called");

        // Text must not contain hand-built HTML mention markup
        Assert.That(capturedMessage!.Text, Does.Not.Contain("<a href"),
            "No raw HTML anchor tags — mention is expressed via entity");

        // Must contain a TextMention entity pointing to the correct user id
        Assert.That(
            capturedMessage.Entities,
            Has.Some.Matches<MessageEntity>(e =>
                e.Type == MessageEntityType.TextMention && e.User!.Id == 1L),
            "TextMention entity required for the bypassing user");

        // String+ParseMode overload must NOT be called for the announcement
        await _messageService.DidNotReceive().SendAndSaveMessageAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Is<ParseMode?>(p => p == ParseMode.Html),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Consumer-side clamping (Task 19 regression anchors)

    [Test]
    public async Task PostBypassAnnouncement_OverLengthTemplate_TruncatesAndSendsTruncatedText()
    {
        // Arrange: template longer than MaxAnnouncementTemplateLength (3500 chars)
        var longTemplate = new string('x', 5000);
        SetupTrustedBypass(adminTemplate: longTemplate, trustedTemplate: longTemplate);
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Text sent must not exceed the template cap.
        // TODO: _logger warning assertion omitted — fixture uses NullLogger<WelcomeService>.Instance
        //       (not a mocked ILogger). To assert the warning, refactor SetUp to inject a
        //       Substitute.For<ILogger<WelcomeService>>() and pass it to the WelcomeService ctor.
        await _messageService.Received(1).SendAndSaveMessageAsync(
            Arg.Any<long>(),
            Arg.Is<TelegramMessage>(m => m!.Text.Length <= TrustedBypassConfig.MaxAnnouncementTemplateLength),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PostBypassAnnouncement_NegativeTtl_ClampsToZero()
    {
        // Arrange: TTL is negative; consumer-side Math.Max should clamp it to 0.
        var config = new WelcomeConfig
        {
            MainWelcomeMessage = "welcome",
            TrustedBypass =
            {
                Enabled = true,
                AnnouncementMessageTrusted = "hello {username}",
                AnnouncementMessageAdmin = "hello {username}",
                AnnouncementTtlSeconds = -5,
            }
        };
        _configService.GetEffectiveWelcomeAsync(Arg.Any<long>()).Returns(config);

        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<TelegramMessage>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 9010, Chat = new Chat { Id = TestChatId } });

        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Delete-job must be scheduled with delaySeconds: 0 (clamped from -5).
        await _jobScheduler.Received(1).ScheduleJobAsync(
            BackgroundJobNames.DeleteMessage,
            Arg.Any<DeleteMessagePayload>(),
            0,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PostBypassAnnouncement_EmptyTemplate_SkipsSendAndSchedule()
    {
        // Arrange: both templates are empty strings — early-return guard fires.
        SetupTrustedBypass(adminTemplate: "", trustedTemplate: "");
        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        await _messageService.DidNotReceive().SendAndSaveMessageAsync(
            Arg.Any<long>(), Arg.Any<TelegramMessage>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
        await _jobScheduler.DidNotReceive().ScheduleJobAsync(
            Arg.Any<string>(),
            Arg.Any<DeleteMessagePayload>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PostBypassAnnouncement_EnabledFalse_SkipsAnnouncement_EvenOnAdminBypass()
    {
        // Arrange: TrustedBypass toggle is OFF; resolver returns Admin bypass.
        // Announcement must be silenced regardless.
        var config = new WelcomeConfig
        {
            MainWelcomeMessage = "welcome",
            TrustedBypass = { Enabled = false },
        };
        _configService.GetEffectiveWelcomeAsync(Arg.Any<long>()).Returns(config);

        _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BypassResolution(BypassDecision.Admin, "Telegram chat admin (2 chats)"));

        await _sut.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

        // Announcement silenced when Enabled=false (entity overload).
        await _messageService.DidNotReceive().SendAndSaveMessageAsync(
            Arg.Any<long>(), Arg.Any<TelegramMessage>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
