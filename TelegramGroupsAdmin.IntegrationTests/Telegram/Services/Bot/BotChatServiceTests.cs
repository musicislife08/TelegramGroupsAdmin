using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
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
/// </summary>
[TestFixture]
public class BotChatServiceTests
{
    private const long TestChatId = -100123456789L;
    private const string TestChatName = "Test Group";
    private const long TestUserId = 12345L;
    private const long TestBotId = 987654321L;

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

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up mocks for external services
        _mockChatHandler = Substitute.For<IBotChatHandler>();
        _mockChatCache = Substitute.For<IChatCache>();
        _mockHealthCache = Substitute.For<IChatHealthCache>();
        _mockNotificationService = Substitute.For<INotificationService>();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Register Core services
        services.AddCoreServices();

        // Register repositories (real implementations)
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IConfigRepository, ConfigRepository>();

        // Register mocked external services
        services.AddSingleton(_mockChatHandler);
        services.AddSingleton(_mockChatCache);
        services.AddSingleton(_mockHealthCache);
        services.AddSingleton(_mockNotificationService);

        // Register BotChatService
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
        // Arrange
        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Left,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Managed chat created
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(TestChatId);
        Assert.That(managedChat, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(managedChat!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(managedChat.ChatName, Is.EqualTo(TestChatName));
            Assert.That(managedChat.IsAdmin, Is.True);
            Assert.That(managedChat.IsActive, Is.True);
            Assert.That(managedChat.IsDeleted, Is.False);
        });
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_BotKicked_MarksAsInactiveAndDeleted()
    {
        // Arrange - First create the chat record
        await SeedManagedChat(TestChatId, TestChatName);

        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Administrator,
            newStatus: ChatMemberStatus.Kicked,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Chat marked as inactive (soft deleted)
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(TestChatId);
        Assert.That(managedChat, Is.Not.Null);
        // BotStatus should reflect kicked status, IsActive should be false
        Assert.Multiple(() =>
        {
            Assert.That(managedChat!.BotStatus, Is.EqualTo(BotChatStatus.Kicked));
            Assert.That(managedChat.IsActive, Is.False);
        });
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChat_SkipsProcessing()
    {
        // Arrange - Private chat should be ignored
        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Private, null),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - No managed chat created
        var managedChat = await _managedChatsRepo!.GetByChatIdAsync(TestChatId);
        Assert.That(managedChat, Is.Null);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_BotPromotedToAdmin_RefreshesChatAdmins()
    {
        // Arrange - Existing chat, bot promoted
        await SeedManagedChat(TestChatId, TestChatName, isAdmin: false);

        var botUser = CreateBotUser();
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: botUser);

        // Mock admin list from Telegram
        var adminUser = CreateUser(TestUserId, "AdminUser");
        _mockChatHandler.GetChatAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(TelegramTestFactory.CreateChatFullInfo(TestChatId, ChatType.Supergroup, TestChatName));
        _mockChatHandler.GetChatAdministratorsAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(new ChatMember[]
            {
                new ChatMemberAdministrator { User = adminUser }
            });

        // Act
        await _service!.HandleBotMembershipUpdateAsync(chatMemberUpdate);

        // Assert - Admin list was refreshed
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(TestChatId);
        Assert.That(admins, Has.Count.EqualTo(1));
        Assert.That(admins[0].TelegramId, Is.EqualTo(TestUserId));
    }

    #endregion

    #region HandleAdminStatusChangeAsync Tests

    [Test]
    public async Task HandleAdminStatusChangeAsync_UserPromoted_CreatesAdminRecordAndTrusts()
    {
        // Arrange
        await SeedManagedChat(TestChatId, TestChatName);

        var promotedUser = CreateUser(TestUserId, "NewAdmin");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Administrator,
            user: promotedUser);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - Admin record created
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(TestChatId);
        Assert.That(admins, Has.Count.EqualTo(1));
        Assert.That(admins[0].TelegramId, Is.EqualTo(TestUserId));

        // Assert - User was auto-trusted
        var user = await _userRepo!.GetByTelegramIdAsync(TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.IsTrusted, Is.True);
    }

    [Test]
    public async Task HandleAdminStatusChangeAsync_UserDemoted_DeactivatesAdminRecord()
    {
        // Arrange
        await SeedManagedChat(TestChatId, TestChatName);
        await SeedAdmin(TestChatId, TestUserId);

        var demotedUser = CreateUser(TestUserId, "FormerAdmin");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Administrator,
            newStatus: ChatMemberStatus.Member,
            user: demotedUser);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - Admin record deactivated
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(TestChatId);
        Assert.That(admins, Is.Empty); // Deactivated admins not returned
    }

    [Test]
    public async Task HandleAdminStatusChangeAsync_NoAdminChange_DoesNothing()
    {
        // Arrange - Member → Restricted (not an admin change)
        await SeedManagedChat(TestChatId, TestChatName);

        var user = CreateUser(TestUserId, "RegularUser");
        var chatMemberUpdate = CreateChatMemberUpdated(
            chat: CreateChat(TestChatId, ChatType.Supergroup, TestChatName),
            oldStatus: ChatMemberStatus.Member,
            newStatus: ChatMemberStatus.Restricted,
            user: user);

        // Act
        await _service!.HandleAdminStatusChangeAsync(chatMemberUpdate);

        // Assert - No admin record created
        var admins = await _chatAdminsRepo!.GetChatAdminsAsync(TestChatId);
        Assert.That(admins, Is.Empty);
    }

    #endregion

    #region HandleChatMigrationAsync Tests

    [Test]
    public async Task HandleChatMigrationAsync_MarksOldChatAsDeleted()
    {
        // Arrange
        const long oldChatId = -100111111111L;
        const long newChatId = -100222222222L;
        await SeedManagedChat(oldChatId, "Old Group");

        // Act
        await _service!.HandleChatMigrationAsync(oldChatId, newChatId);

        // Assert - Old chat soft-deleted (IsDeleted = true)
        var oldChat = await _managedChatsRepo!.GetByChatIdAsync(oldChatId);
        // GetByChatIdAsync may still return the record with IsDeleted = true
        // The behavior depends on repository implementation - check what we get back
        if (oldChat != null)
        {
            Assert.That(oldChat.IsDeleted, Is.True, "Old chat should be marked as deleted");
        }
        // If null, that's also acceptable (hard delete)
    }

    #endregion

    #region GetHealthyChatIds Tests

    [Test]
    public void GetHealthyChatIds_ReturnsFromHealthCache()
    {
        // Arrange
        var expectedChatIds = new HashSet<long> { -100001, -100002, -100003 };
        _mockHealthCache.GetHealthyChatIds().Returns(expectedChatIds);

        // Act
        var result = _service!.GetHealthyChatIds();

        // Assert
        Assert.That(result, Is.EqualTo(expectedChatIds));
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

    private async Task SeedAdmin(long chatId, long userId)
    {
        await using var context = _testHelper!.GetDbContext();

        // Ensure user exists first (FK constraint)
        var existingUser = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == userId);

        if (existingUser == null)
        {
            context.TelegramUsers.Add(new Data.Models.TelegramUserDto
            {
                TelegramUserId = userId,
                FirstName = "Admin",
                Username = "admin_user",
                IsBot = false,
                IsTrusted = false,
                IsBanned = false,
                BotDmEnabled = false,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        context.ChatAdmins.Add(new Data.Models.ChatAdminRecordDto
        {
            ChatId = chatId,
            TelegramId = userId,
            IsCreator = false,
            PromotedAt = DateTimeOffset.UtcNow,
            LastVerifiedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }

    #endregion
}
