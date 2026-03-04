using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using global::Telegram.Bot.Types;
using global::Telegram.Bot.Types.Enums;
using global::Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

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
    private IProfileScanService _profileScanService = null!;
    private ITelegramSessionManager _sessionManager = null!;
    private IWelcomeAdmissionHandler _admissionHandler = null!;

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
        _profileScanService = Substitute.For<IProfileScanService>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();
        _admissionHandler = Substitute.For<IWelcomeAdmissionHandler>();

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
            .GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long>())
            .Returns(WelcomeConfig.Default);

        // User is a regular member (not admin) by default
        _userService
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ChatMemberMember { User = TestUser });

        // User exists and is not banned
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

        // SendAndSaveMessageAsync returns a minimal Message so verifyingMessageId is set
        _messageService
            .SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
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
            _photoService,
            _profileScanService,
            _sessionManager,
            _admissionHandler,
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
    /// </summary>
    private static ChatMemberUpdated CreateJoinUpdate(
        User? user = null,
        Chat? chat = null,
        ChatMemberStatus oldStatus = ChatMemberStatus.Left,
        ChatMemberStatus newStatus = ChatMemberStatus.Member)
    {
        var resolvedUser = user ?? TestUser;
        var resolvedChat = chat ?? new Chat
        {
            Id = TestChatId,
            Type = ChatType.Supergroup,
            Title = "Test Group"
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
                Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(BannedTelegramUser);

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — SyncBanToChatAsync must be called once with a SyncBanIntent
        await _moderationService.Received(1).SyncBanToChatAsync(
            Arg.Is<SyncBanIntent>(i =>
                i.User.Id == TestUserId &&
                i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());

        // Early-exit: mute (RestrictUserAsync) must NOT be called
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());

        // Early-exit: CAS check must NOT be called
        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<long>(), Arg.Any<TelegramGroupsAdmin.Configuration.Models.Welcome.CasConfig>(),
            Arg.Any<CancellationToken>());

        // Early-exit: profile scan must NOT be called
        await _profileScanService.DidNotReceive().ScanUserProfileAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
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
                i.User.Id == TestUserId &&
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
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

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
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 5: Admin joining — welcome flow is skipped entirely

    [Test]
    public async Task HandleChatMemberUpdate_AdminJoining_SkipsWelcome()
    {
        // Arrange — GetChatMemberAsync returns Administrator status
        var adminUser = new User
        {
            Id = TestUserId,
            FirstName = "Alice",
            Username = "alice_tg",
            IsBot = false
        };

        _userService
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ChatMemberAdministrator { User = adminUser });

        var update = CreateJoinUpdate(user: adminUser);

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — admin short-circuits before GetOrCreateAsync
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        // No mute, no CAS check, no profile scan
        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());

        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<long>(), Arg.Any<TelegramGroupsAdmin.Configuration.Models.Welcome.CasConfig>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_OwnerJoining_SkipsWelcome()
    {
        // Arrange — GetChatMemberAsync returns Creator (owner) status
        _userService
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ChatMemberOwner { User = TestUser });

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — creator also short-circuits before GetOrCreateAsync
        await _telegramUserRepository.DidNotReceive().GetOrCreateAsync(
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

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
            Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        await _moderationService.DidNotReceive().RestrictUserAsync(
            Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdate_BannedUser_SyncBanIntentCarriesCorrectIdentities()
    {
        // Arrange
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
}
