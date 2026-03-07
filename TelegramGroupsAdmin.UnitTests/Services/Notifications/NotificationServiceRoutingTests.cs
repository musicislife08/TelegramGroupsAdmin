using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

/// <summary>
/// Unit tests for NotificationService audience routing.
/// Tests the two routing methods through public API:
/// - SendToChatAudienceAsync: Pool 1 (web users) + Pool 2 (unlinked Telegram admins) with dedup
/// - SendToOwnersAsync: Owner-only filtering at DB level
/// </summary>
[TestFixture]
public class NotificationServiceRoutingTests
{
    private INotificationPreferencesRepository _mockPrefsRepo = null!;
    private IEmailService _mockEmailService = null!;
    private IBotDmService _mockDmService = null!;
    private IWebPushNotificationService _mockWebPushService = null!;
    private ITelegramUserMappingRepository _mockTelegramMappingRepo = null!;
    private IChatAdminsRepository _mockChatAdminsRepo = null!;
    private IUserRepository _mockUserRepo = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;
    private ILogger<NotificationService> _mockLogger = null!;

    private NotificationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _mockPrefsRepo = Substitute.For<INotificationPreferencesRepository>();
        _mockEmailService = Substitute.For<IEmailService>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockWebPushService = Substitute.For<IWebPushNotificationService>();
        _mockTelegramMappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _mockChatAdminsRepo = Substitute.For<IChatAdminsRepository>();
        _mockUserRepo = Substitute.For<IUserRepository>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _mockLogger = Substitute.For<ILogger<NotificationService>>();

        // Default: notification preferences return all-disabled config (no channels deliver)
        _mockPrefsRepo.GetOrCreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationConfig());

        // Default: DM delivery succeeds (prevents NRE from null DmDeliveryResult)
        _mockDmService.SendDmWithQueueAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Default: no telegram mappings (prevents N+1 from returning unexpected data)
        _mockTelegramMappingRepo.GetTelegramIdsByUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<long>());

        _service = new NotificationService(
            _mockPrefsRepo,
            _mockEmailService,
            _mockDmService,
            _mockWebPushService,
            _mockTelegramMappingRepo,
            _mockChatAdminsRepo,
            _mockUserRepo,
            _mockCallbackContextRepo,
            _mockLogger);
    }

    // ── Helpers ──

    private static UserRecord CreateTestUser(string id, PermissionLevel level = PermissionLevel.Owner) =>
        new(
            WebUser: new WebUserIdentity(id, $"{id}@test.com", level),
            NormalizedEmail: $"{id}@TEST.COM",
            PasswordHash: "hash",
            SecurityStamp: "stamp",
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: false,
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: true,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null);

    private static ChatAdmin CreateTestChatAdmin(
        long chatId,
        long telegramId,
        bool botDmEnabled = true,
        UserRecord? linkedWebUser = null) =>
        new()
        {
            Id = telegramId,
            ChatId = chatId,
            User = UserIdentity.FromId(telegramId),
            IsCreator = false,
            PromotedAt = DateTimeOffset.UtcNow,
            LastVerifiedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            BotDmEnabled = botDmEnabled,
            LinkedWebUser = linkedWebUser
        };

    // ── SendToOwnersAsync Tests (via SendBackupFailedAsync) ──

    [Test]
    public async Task SendToOwnersAsync_CallsGetOwnerUsersAsync()
    {
        // Arrange — no owners
        _mockUserRepo.GetOwnerUsersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord>());

        // Act
        await _service.SendBackupFailedAsync("users", "Connection timeout");

        // Assert — uses the new filtered method, not GetAllAsync
        await _mockUserRepo.Received(1).GetOwnerUsersAsync(Arg.Any<CancellationToken>());
        await _mockUserRepo.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendToOwnersAsync_DeliversToEachOwner()
    {
        // Arrange
        var owner1 = CreateTestUser("owner-1");
        var owner2 = CreateTestUser("owner-2");
        _mockUserRepo.GetOwnerUsersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord> { owner1, owner2 });

        // Act
        var results = await _service.SendBackupFailedAsync("users", "Connection timeout");

        // Assert — preferences fetched for each owner
        await _mockPrefsRepo.Received(1).GetOrCreateAsync("owner-1", Arg.Any<CancellationToken>());
        await _mockPrefsRepo.Received(1).GetOrCreateAsync("owner-2", Arg.Any<CancellationToken>());
        Assert.That(results, Has.Count.EqualTo(2));
    }

    // ── SendToChatAudienceAsync Tests (via SendMalwareDetectedAsync — simple 3-param method) ──

    [Test]
    public async Task SendToChatAudienceAsync_Pool1_DeliversToWebUsers()
    {
        // Arrange
        var chat = new ChatIdentity(-1001234567890L, "Test Chat");
        var user = UserIdentity.FromId(999L);
        var webUser = CreateTestUser("web-1", PermissionLevel.Admin);

        _mockUserRepo.GetWebUsersWithChatAccessAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord> { webUser });

        _mockChatAdminsRepo.GetChatAdminsAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin>());

        // Act
        var results = await _service.SendMalwareDetectedAsync(chat, user, "Trojan.GenericKD", CancellationToken.None);

        // Assert — web user received notification attempt
        await _mockPrefsRepo.Received(1).GetOrCreateAsync("web-1", Arg.Any<CancellationToken>());
        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SendToChatAudienceAsync_Pool2_DeliversDmToUnlinkedAdmins()
    {
        // Arrange
        var chat = new ChatIdentity(-1001234567890L, "Test Chat");
        var user = UserIdentity.FromId(999L);

        _mockUserRepo.GetWebUsersWithChatAccessAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord>());

        var unlinkedAdmin = CreateTestChatAdmin(chat.Id, telegramId: 555L, botDmEnabled: true);
        _mockChatAdminsRepo.GetChatAdminsAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin> { unlinkedAdmin });

        // Act
        await _service.SendMalwareDetectedAsync(chat, user, "Trojan.GenericKD", CancellationToken.None);

        // Assert — DM sent to unlinked admin via SendDmWithQueueAsync (plain text, no media/keyboard)
        await _mockDmService.Received(1).SendDmWithQueueAsync(
            555L, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendToChatAudienceAsync_LinkedAdmin_SkippedInPool2()
    {
        // Arrange
        var chat = new ChatIdentity(-1001234567890L, "Test Chat");
        var user = UserIdentity.FromId(999L);
        var webUser = CreateTestUser("web-1", PermissionLevel.Admin);

        _mockUserRepo.GetWebUsersWithChatAccessAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord> { webUser });

        // Admin has linked web account — should be skipped in pool 2
        var linkedAdmin = CreateTestChatAdmin(chat.Id, telegramId: 555L, botDmEnabled: true, linkedWebUser: webUser);
        _mockChatAdminsRepo.GetChatAdminsAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin> { linkedAdmin });

        _mockTelegramMappingRepo.GetTelegramIdsByUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<long> { 555L });

        // Act
        await _service.SendMalwareDetectedAsync(chat, user, "Trojan.GenericKD", CancellationToken.None);

        // Assert — no DM sent to admin (already in pool 1 via web account)
        await _mockDmService.DidNotReceive().SendDmWithQueueAsync(
            555L, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendToChatAudienceAsync_AdminWithDmDisabled_SkippedInPool2()
    {
        // Arrange
        var chat = new ChatIdentity(-1001234567890L, "Test Chat");
        var user = UserIdentity.FromId(999L);

        _mockUserRepo.GetWebUsersWithChatAccessAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord>());

        // Admin hasn't run /start — BotDmEnabled is false
        var admin = CreateTestChatAdmin(chat.Id, telegramId: 555L, botDmEnabled: false);
        _mockChatAdminsRepo.GetChatAdminsAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin> { admin });

        // Act
        await _service.SendMalwareDetectedAsync(chat, user, "Trojan.GenericKD", CancellationToken.None);

        // Assert — no DM sent (bot DM not enabled)
        await _mockDmService.DidNotReceive().SendDmWithQueueAsync(
            555L, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendToChatAudienceAsync_DedupByTelegramId_PreventsDuplicateDm()
    {
        // Arrange
        var chat = new ChatIdentity(-1001234567890L, "Test Chat");
        var user = UserIdentity.FromId(999L);
        var webUser = CreateTestUser("web-1", PermissionLevel.Admin);

        _mockUserRepo.GetWebUsersWithChatAccessAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<UserRecord> { webUser });

        // Web user's linked Telegram ID = 555
        _mockTelegramMappingRepo.GetTelegramIdsByUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<long> { 555L });

        // Same Telegram user is also a chat admin (unlinked in admin record, but Telegram ID matches)
        var admin = CreateTestChatAdmin(chat.Id, telegramId: 555L, botDmEnabled: true);
        _mockChatAdminsRepo.GetChatAdminsAsync(chat.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin> { admin });

        // Act
        await _service.SendMalwareDetectedAsync(chat, user, "Trojan.GenericKD", CancellationToken.None);

        // Assert — no DM sent (deduped by Telegram ID from pool 1)
        await _mockDmService.DidNotReceive().SendDmWithQueueAsync(
            555L, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }
}
