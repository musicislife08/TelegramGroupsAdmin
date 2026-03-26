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
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Tests for username blacklist integration within WelcomeService.HandleChatMemberUpdateAsync.
///
/// Verifies that:
/// - Blacklisted users are banned and short-circuit before CAS check
/// - Non-blacklisted users proceed to CAS as normal
/// - Trusted users skip the blacklist check entirely
/// - Disabled blacklist config skips the check entirely
/// </summary>
[TestFixture]
public class WelcomeServiceBlacklistTests
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
    private IUsernameBlacklistService _usernameBlacklistService = null!;

    // TelegramPhotoService is concrete — built with mocked sub-dependencies.
    private TelegramPhotoService _photoService = null!;

    private WelcomeService _sut = null!;

    // Reusable test user (not a bot, not an admin)
    private static readonly User TestUser = new()
    {
        Id = TestUserId,
        FirstName = "Scarlett",
        LastName = "Lux",
        Username = "scarlett_lux",
        IsBot = false
    };

    private static readonly TelegramUser NonBannedTelegramUser = new(
        TelegramUserId: TestUserId,
        Username: "scarlett_lux",
        FirstName: "Scarlett",
        LastName: "Lux",
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

    private static readonly TelegramUser TrustedTelegramUser = NonBannedTelegramUser with { IsTrusted = true };

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
        _usernameBlacklistService = Substitute.For<IUsernameBlacklistService>();

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

        // BanUserAsync succeeds
        _moderationService
            .BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
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

        // Username blacklist returns no match by default
        _usernameBlacklistService
            .CheckDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UsernameBlacklistEntry?)null);

        // CAS returns not banned by default
        _casCheckService
            .CheckUserAsync(Arg.Any<long>(), Arg.Any<CasConfig>(), Arg.Any<CancellationToken>())
            .Returns(new CasCheckResult(IsBanned: false, Reason: null));

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
            _profileScanService,
            _sessionManager,
            _admissionHandler,
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

    #region Test 1: Blacklisted user — ban and return early (before CAS)

    [Test]
    public async Task HandleChatMemberUpdate_BlacklistedUser_BansAndReturnsEarly()
    {
        // Arrange — blacklist service returns a matched entry
        var matchedEntry = new UsernameBlacklistEntry(
            Id: 1,
            Pattern: "Scarlett Lux",
            MatchType: BlacklistMatchType.Exact,
            Enabled: true,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: Actor.FromSystem("test"),
            Notes: null);

        _usernameBlacklistService
            .CheckDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(matchedEntry);

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — BanUserAsync must be called with reason containing the pattern and correct executor
        await _moderationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(b =>
                b.User.Id == TestUserId &&
                b.Reason.Contains("Scarlett Lux") &&
                b.Executor == Actor.UsernameBlacklist),
            Arg.Any<CancellationToken>());

        // Early-exit: CAS check must NOT be called (short-circuited by blacklist)
        await _casCheckService.DidNotReceive().CheckUserAsync(
            Arg.Any<long>(),
            Arg.Any<CasConfig>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 2: Not blacklisted — proceeds to CAS

    [Test]
    public async Task HandleChatMemberUpdate_NotBlacklisted_ProceedsToCas()
    {
        // Arrange — blacklist returns null (no match), CAS returns not banned
        _usernameBlacklistService
            .CheckDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UsernameBlacklistEntry?)null);

        _casCheckService
            .CheckUserAsync(Arg.Any<long>(), Arg.Any<CasConfig>(), Arg.Any<CancellationToken>())
            .Returns(new CasCheckResult(IsBanned: false, Reason: null));

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — blacklist was consulted
        await _usernameBlacklistService.Received(1).CheckDisplayNameAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        // CAS was called (blacklist didn't short-circuit)
        await _casCheckService.Received(1).CheckUserAsync(
            Arg.Any<long>(),
            Arg.Any<CasConfig>(),
            Arg.Any<CancellationToken>());

        // Ban was NOT called (neither blacklist nor CAS triggered)
        await _moderationService.DidNotReceive().BanUserAsync(
            Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 3: Trusted user — skips blacklist check

    [Test]
    public async Task HandleChatMemberUpdate_TrustedUser_SkipsBlacklistCheck()
    {
        // Arrange — repository returns a trusted user
        _telegramUserRepository
            .GetOrCreateAsync(
                Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(TrustedTelegramUser);

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — blacklist check must NOT be called (trusted users skip it)
        await _usernameBlacklistService.DidNotReceive().CheckDisplayNameAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test 4: Blacklist disabled — skips check

    [Test]
    public async Task HandleChatMemberUpdate_BlacklistDisabled_SkipsCheck()
    {
        // Arrange — override config to disable username blacklist
        var configWithBlacklistDisabled = new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            JoinSecurity = new JoinSecurityConfig
            {
                UsernameBlacklist = new UsernameBlacklistConfig { Enabled = false },
                Cas = new CasConfig { Enabled = false }
            }
        };

        _configService
            .GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long>())
            .Returns(configWithBlacklistDisabled);

        var update = CreateJoinUpdate();

        // Act
        await _sut.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        // Assert — blacklist check must NOT be called (disabled in config)
        await _usernameBlacklistService.DidNotReceive().CheckDisplayNameAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
