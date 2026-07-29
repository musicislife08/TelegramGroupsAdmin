using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services.Bot;

/// <summary>
/// Integration tests for BotChatService.
/// Tests bot membership handling, admin status changes, and chat migration.
///
/// Architecture:
/// - Service handles MyChatMember/ChatMember updates from Telegram
/// - Persists chat records to managed_chats table
/// - Maintains admin cache in chat_admins table
/// - Auto-trusts new admins globally
///
/// Test Strategy:
/// - Real PostgreSQL for managed chats, chat admins, and user records
/// - Mocked IBotChatHandler for API responses
/// - Mocked caches (IChatCache, IChatHealthCache) for in-memory state
///
/// Canonical anchors:
/// - MainChat: chat_id = -100026957614982 ("Main Community"), is_admin=true, is_active=true
/// - WorkshopAlumni: chat_id = -100059667856554 ("Workshop Alumni"), 5 admins
/// - WorkshopAlumni admin: telegram_id = 9742468412405
/// - Synthetic chats (outside canonical range): -100123456789, -100111111111, -100222222222
/// - Synthetic users (outside canonical range): 12345, 987654321
/// </summary>
[TestFixture]
public class BotChatServiceTests
{
    // Synthetic chat/user IDs — outside canonical range [-100099999999999, -100000000000000] / [9_000_000_000_000, 10_000_000_000_000)
    private const long SyntheticChatId = -100123456789L;
    private const string SyntheticChatName = "Test Group";
    private const long SyntheticUserId = 12345L;
    private const long TestBotId = 987654321L;

    // Canonical anchors
    private const long MainChatId = -100026957614982L;
    private const string MainChatName = "Main Community";
    private const long WorkshopAlumniChatId = -100059667856554L;
    private const string WorkshopAlumniChatName = "Workshop Alumni";
    private const long WorkshopAlumniAdminId = 9742468412405L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBotChatService? _service;
    private IManagedChatsRepository? _managedChatsRepo;
    private IChatAdminsRepository? _chatAdminsRepo;
    private ITelegramUserRepository? _userRepo;
    private IBotChatHandler _mockChatHandler = null!;
    private IChatCache _mockChatCache = null!;
    private IChatHealthCache _mockHealthCache = null!;
    private INotificationService _mockNotificationService = null!;
    private IConfigService _mockConfigService = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _mockChatHandler = Substitute.For<IBotChatHandler>();
        _mockChatCache = Substitute.For<IChatCache>();
        _mockHealthCache = Substitute.For<IChatHealthCache>();
        _mockNotificationService = Substitute.For<INotificationService>();
        _mockConfigService = Substitute.For<IConfigService>();

        var services = new ServiceCollection();

        services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider);

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddCoreServices();

        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        services.AddSingleton(_mockChatHandler);
        services.AddSingleton(_mockChatCache);
        services.AddSingleton(_mockHealthCache);
        services.AddSingleton(_mockNotificationService);
        services.AddSingleton(_mockConfigService);

        services.AddScoped<IBotChatService, BotChatService>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _service = scope.ServiceProvider.GetRequiredService<IBotChatService>();
        _managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        _chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        _userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region HandleBotMembershipUpdateAsync Tests

    [Test]
    public async Task HandleBotMembershipUpdateAsync_BotAddedAsAdmin_CreatesManagedChatRecord()
    {
        // Arrange — SyntheticChatId is outside canonical range; SUT will INSERT a new row.
        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(SyntheticChatId, ChatType.Supergroup, SyntheticChatName),
            oldStatus: ChatMemberStatus.Left,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Managed chat created
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(SyntheticChatId);
        Assert.That(managedChat, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(managedChat!.Identity.Id, Is.EqualTo(SyntheticChatId));
            Assert.That(managedChat.Identity.ChatName, Is.EqualTo(SyntheticChatName));
            Assert.That(managedChat.IsAdmin, Is.True);
            Assert.That(managedChat.IsActive, Is.True);
            Assert.That(managedChat.IsDeleted, Is.False);
        }
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_BotKicked_MarksAsInactiveAndDeleted()
    {
        // Arrange — MainChat already exists in the canonical template (is_admin=true, is_active=true).
        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(MainChatId, ChatType.Supergroup, MainChatName),
            oldStatus: ChatMemberStatus.Administrator,
            newStatus: ChatMemberStatus.Kicked,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Chat marked as inactive (soft deleted)
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(MainChatId);
        Assert.That(managedChat, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(managedChat!.BotStatus, Is.EqualTo(BotChatStatus.Kicked));
            Assert.That(managedChat.IsActive, Is.False);
        }
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChat_SkipsProcessing()
    {
        // Arrange - Private chat should be ignored; SyntheticChatId used (no DB interaction expected).
        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(SyntheticChatId, ChatType.Private, null),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - No managed chat created for this synthetic ID
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(SyntheticChatId);
        Assert.That(managedChat, Is.Null);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_BotPromotedToAdmin_RefreshesChatAdmins()
    {
        // Arrange — Seed a synthetic chat with isAdmin=false so the bot promotion path triggers.
        // SyntheticChatId is outside canonical range so no canonical row collision.
        await SeedManagedChat(SyntheticChatId, SyntheticChatName, isAdmin: false);

        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(SyntheticChatId, ChatType.Supergroup, SyntheticChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        var adminUser = CreateUser(SyntheticUserId, "AdminUser");
        _mockChatHandler.GetChatAsync(SyntheticChatId, Arg.Any<CancellationToken>())
            .Returns(TelegramTestFactory.CreateChatFullInfo(SyntheticChatId, ChatType.Supergroup, SyntheticChatName));
        _mockChatHandler.GetChatAdministratorsAsync(SyntheticChatId, Arg.Any<CancellationToken>())
            .Returns(new ChatMember[]
            {
                new ChatMemberAdministrator { User = adminUser }
            });

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Admin list was refreshed
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(SyntheticChatId);
        Assert.That(admins, Has.Count.EqualTo(1));
        Assert.That(admins[0].User.Id, Is.EqualTo(SyntheticUserId));
    }

    #endregion

    #region HandleAdminStatusChangeAsync Tests

    [Test]
    public async Task HandleAdminStatusChangeAsync_UserPromoted_CreatesAdminRecordAndTrusts()
    {
        // Arrange — MainChat exists in canonical template; SyntheticUserId is new (outside canonical range).
        var promotedUser = CreateUser(SyntheticUserId, "NewAdmin");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(MainChatId, ChatType.Supergroup, MainChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: promotedUser);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - Admin record created for SyntheticUserId in MainChat
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(MainChatId);
        Assert.That(admins.Any(a => a.User.Id == SyntheticUserId), Is.True);

        // Assert - User was auto-trusted
        var user = await _userRepo!.GetByTelegramIdAsync(SyntheticUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.IsTrusted, Is.True);
    }

    [Test]
    public async Task HandleAdminStatusChangeAsync_UserDemoted_DeactivatesAdminRecord()
    {
        // Arrange — WorkshopAlumni exists in canonical template with 5 active admins.
        // WorkshopAlumniAdminId (9742468412405) is one of those canonical admins.
        var demotedUser = CreateUser(WorkshopAlumniAdminId, "FormerAdmin");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(WorkshopAlumniChatId, ChatType.Supergroup, WorkshopAlumniChatName),
            oldStatus: ChatMemberStatus.Administrator,
            newStatus: ChatMemberStatus.Member,
            user: demotedUser);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - The demoted admin is no longer in the active admin list
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(WorkshopAlumniChatId);
        Assert.That(admins.Any(a => a.User.Id == WorkshopAlumniAdminId), Is.False,
            "Demoted admin should no longer appear in the active admin list");
    }

    [Test]
    public async Task HandleAdminStatusChangeAsync_NoAdminChange_DoesNothing()
    {
        // Arrange — MainChat exists in canonical template; Member → Restricted is not an admin change.
        var user = CreateUser(SyntheticUserId, "RegularUser");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(MainChatId, ChatType.Supergroup, MainChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Restricted,
            user: user);

        // Record how many admins MainChat has before the no-op call.
        var adminsBefore = await _chatAdminsRepo!.GetChatAdminsAsync(MainChatId);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - Admin count unchanged; SyntheticUserId was never inserted.
        var adminsAfter = await _chatAdminsRepo!.GetChatAdminsAsync(MainChatId);
        Assert.That(adminsAfter, Has.Count.EqualTo(adminsBefore.Count));
        Assert.That(adminsAfter.Any(a => a.User.Id == SyntheticUserId), Is.False);
    }

    #endregion

    #region HandleChatMigrationAsync Tests

    [Test]
    public async Task HandleChatMigrationAsync_MarksOldChatAsDeleted()
    {
        // Arrange — Synthetic IDs outside canonical range; seed old chat inline.
        const long oldChatId = -100111111111L;
        const long newChatId = -100222222222L;
        await SeedManagedChat(oldChatId, "Old Group");

        // Act
        await _service!.HandleChatMigrationAsync(oldChatId, newChatId);

        // Assert - Old chat soft-deleted (IsDeleted = true)
        var oldChat = await _managedChatsRepo!.GetByChatIdAsync(oldChatId);
        if (oldChat != null)
        {
            Assert.That(oldChat.IsDeleted, Is.True, "Old chat should be marked as deleted");
        }
        // If null, that's also acceptable (hard delete per SUT implementation)
    }

    #endregion

    #region GetHealthyChatIdentities Tests

    [Test]
    public void GetHealthyChatIdentities_ReturnsFromHealthCache()
    {
        // Arrange
        var expectedChatIdentities = new List<ChatIdentity>
        {
            new(-100001, "Chat 1"),
            new(-100002, "Chat 2"),
            new(-100003, "Chat 3")
        }.AsReadOnly();
        _mockHealthCache.GetHealthyChatIdentities().Returns(expectedChatIdentities);

        // Act
        var result = _service!.GetHealthyChatIdentities();

        // Assert
        Assert.That(result, Is.EqualTo(expectedChatIdentities));
    }

    #endregion

    #region RefreshChatAdminsAsync Trust Reconciliation Tests

    [Test]
    public async Task RefreshChatAdmins_ExistingAdminNotTrusted_ReconcilesTrust()
    {
        // Admin auto-trust originally fired only on the promotion event, so an
        // admin promoted before that feature existed was never back-filled.
        await SetAdminTrustAsync(WorkshopAlumniAdminId, isTrusted: false);

        await RefreshAdminsAsync(WorkshopAlumniChatId, WorkshopAlumniAdminId);

        var user = await GetTelegramUserAsync(WorkshopAlumniAdminId);
        Assert.That(user!.IsTrusted, Is.True);
    }

    [Test]
    public async Task RefreshChatAdmins_AdminAlreadyTrusted_DoesNotDuplicateTrustAction()
    {
        await SetAdminTrustAsync(WorkshopAlumniAdminId, isTrusted: true);
        var before = await CountTrustActionsAsync(WorkshopAlumniAdminId);

        await RefreshAdminsAsync(WorkshopAlumniChatId, WorkshopAlumniAdminId);

        var after = await CountTrustActionsAsync(WorkshopAlumniAdminId);
        Assert.That(after, Is.EqualTo(before));
    }

    #endregion

    #region Helper Methods

    private static User CreateBotUser() => new()
    {
        Id = TestBotId,
        IsBot = true,
        FirstName = "TestBot",
        Username = "test_bot"
    };

    private static User CreateUser(long id, string firstName, string? username = null) => new()
    {
        Id = id,
        IsBot = false,
        FirstName = firstName,
        Username = username
    };

    private static Chat CreateChat(long id, ChatType type, string? title) => new()
    {
        Id = id,
        Type = type,
        Title = title
    };

    private static ChatMemberUpdated CreateChatMemberUpdated(
        Chat chat,
        ChatMemberStatus oldStatus,
        ChatMemberStatus newStatus,
        User user)
    {
        return new ChatMemberUpdated
        {
            Chat = chat,
            From = user,
            Date = DateTime.UtcNow,
            OldChatMember = CreateChatMember(oldStatus, user),
            NewChatMember = CreateChatMember(newStatus, user)
        };
    }

    private static ChatMember CreateChatMember(ChatMemberStatus status, User user)
    {
        return status switch
        {
            ChatMemberStatus.Creator => new ChatMemberOwner { User = user },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = user },
            ChatMemberStatus.Member => new ChatMemberMember { User = user },
            ChatMemberStatus.Restricted => new ChatMemberRestricted { User = user },
            ChatMemberStatus.Left => new ChatMemberLeft { User = user },
            ChatMemberStatus.Kicked => new ChatMemberBanned { User = user },
            _ => new ChatMemberMember { User = user }
        };
    }

    // Sets telegram_users.is_trusted for the given user via the repository.
    private async Task SetAdminTrustAsync(long telegramUserId, bool isTrusted)
    {
        if (isTrusted)
        {
            await _userRepo!.TrustUserAsync(telegramUserId);
        }
        else
        {
            await _userRepo!.UntrustUserAsync(telegramUserId);
        }
    }

    // Arranges the mocked IBotChatHandler to return adminUserId as an admin of chatId,
    // then invokes the same public refresh entry point the fixture's other admin tests call.
    private async Task RefreshAdminsAsync(long chatId, long adminUserId)
    {
        var adminUser = CreateUser(adminUserId, "ExistingAdmin");

        _mockChatHandler.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(TelegramTestFactory.CreateChatFullInfo(chatId, ChatType.Supergroup, WorkshopAlumniChatName));
        _mockChatHandler.GetChatAdministratorsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(new ChatMember[]
            {
                new ChatMemberAdministrator { User = adminUser }
            });

        await _service!.RefreshChatAdminsAsync(new ChatIdentity(chatId, WorkshopAlumniChatName));
    }

    // Reads the user row back through ITelegramUserRepository.GetByTelegramIdAsync.
    private async Task<TelegramUser?> GetTelegramUserAsync(long telegramUserId)
    {
        return await _userRepo!.GetByTelegramIdAsync(telegramUserId);
    }

    // Counts user_actions rows with ActionType == UserActionType.Trust for the user.
    private async Task<int> CountTrustActionsAsync(long telegramUserId)
    {
        await using var context = _testHelper!.GetDbContext();

        return await context.UserActions
            .Where(a => a.UserId == telegramUserId && a.ActionType == (int)UserActionType.Trust)
            .CountAsync();
    }

    private async Task SeedManagedChat(long chatId, string chatName, bool isAdmin = true)
    {
        await using var context = _testHelper!.GetDbContext();

        context.ManagedChats.Add(new Data.Models.ManagedChatRecordDto
        {
            ChatId = chatId,
            ChatName = chatName,
            ChatType = Data.Models.ManagedChatType.Supergroup,
            BotStatus = isAdmin
                ? Data.Models.BotChatStatus.Administrator
                : Data.Models.BotChatStatus.Member,
            IsAdmin = isAdmin,
            IsActive = true,
            AddedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }

    #endregion
}
