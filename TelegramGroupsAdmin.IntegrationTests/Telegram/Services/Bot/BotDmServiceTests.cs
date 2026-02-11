using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services.Bot;

/// <summary>
/// Integration tests for BotDmService.
/// Tests DM delivery, bot_dm_enabled tracking, queue fallback, and auto-delete scheduling.
///
/// Architecture:
/// - Service sends DMs via IBotMessageHandler (mocked Telegram API)
/// - Tracks bot_dm_enabled status in real PostgreSQL via ITelegramUserRepository
/// - Queues failed notifications via IPendingNotificationsRepository
/// - Schedules auto-delete jobs via IJobScheduler (mocked)
///
/// Test Strategy:
/// - Real PostgreSQL for user status tracking and pending notifications
/// - Mocked IBotMessageHandler for API responses (success, 403, exceptions)
/// - Mocked IJobScheduler for verifying job scheduling
/// </summary>
[TestFixture]
public class BotDmServiceTests
{
    private const long TestUserId = 12345L;
    private const long TestChatId = -100123456789L;
    private const string TestChatName = "Test Group";

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBotDmService? _service;
    private ITelegramUserRepository? _userRepository;
    private IPendingNotificationsRepository? _pendingNotificationsRepository;
    private IBotMessageHandler _mockMessageHandler = null!;
    private IJobScheduler _mockJobScheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up mocks for external services
        _mockMessageHandler = Substitute.For<IBotMessageHandler>();
        _mockJobScheduler = Substitute.For<IJobScheduler>();

        // Set up dependency injection
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Register repositories (real implementations)
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IPendingNotificationsRepository, PendingNotificationsRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();

        // Register mocked external services
        services.AddSingleton(_mockMessageHandler);
        services.AddSingleton(_mockJobScheduler);

        // Register BotDmService
        services.AddScoped<IBotDmService, BotDmService>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _service = scope.ServiceProvider.GetRequiredService<IBotDmService>();
        _userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        _pendingNotificationsRepository = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();

        // Seed test user
        await SeedTestUser(TestUserId, botDmEnabled: false);
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region SendDmAsync Tests

    [Test]
    public async Task SendDmAsync_Success_SetsBotDmEnabledTrue()
    {
        // Arrange
        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 999, chatId: TestUserId));

        // Act
        var result = await _service!.SendDmAsync(TestUserId, "Test message");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.Failed, Is.False);
            Assert.That(result.MessageId, Is.EqualTo(999));
        }

        // Verify user's bot_dm_enabled was updated
        var user = await _userRepository!.GetByTelegramIdAsync(TestUserId);
        Assert.That(user!.BotDmEnabled, Is.True);
    }

    [Test]
    public async Task SendDmAsync_Blocked403_SetsBotDmEnabledFalse()
    {
        // Arrange - First enable DMs to verify it gets disabled
        await _userRepository!.SetBotDmEnabledAsync(TestUserId, true);

        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new ApiRequestException("Forbidden", 403));

        // Act
        var result = await _service!.SendDmAsync(TestUserId, "Test message");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.ErrorMessage, Does.Contain("not enabled DMs"));
        }

        // Verify user's bot_dm_enabled was set to false
        var user = await _userRepository.GetByTelegramIdAsync(TestUserId);
        Assert.That(user!.BotDmEnabled, Is.False);
    }

    [Test]
    public async Task SendDmAsync_Blocked403WithFallback_SendsToChatAndSchedulesDelete()
    {
        // Arrange
        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new ApiRequestException("Forbidden", 403));

        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestChatId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 888, chatId: TestChatId));

        // Seed managed chat for fallback
        await SeedManagedChat(TestChatId, TestChatName);

        // Act
        var result = await _service!.SendDmAsync(
            TestUserId,
            "Test message",
            fallbackChatId: TestChatId,
            autoDeleteSeconds: 30);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.FallbackUsed, Is.True);
            Assert.That(result.Failed, Is.False);
            Assert.That(result.FallbackMessageId, Is.EqualTo(888));
        }

        // Verify auto-delete job was scheduled
        await _mockJobScheduler.Received(1).ScheduleJobAsync(
            "DeleteMessage",
            Arg.Is<DeleteMessagePayload>(p =>
                p.ChatId == TestChatId &&
                p.MessageId == 888 &&
                p.Reason == "dm_fallback"),
            delaySeconds: 30,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendDmWithQueueAsync Tests

    [Test]
    public async Task SendDmWithQueueAsync_Success_SetsBotDmEnabledTrue()
    {
        // Arrange
        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 999, chatId: TestUserId));

        // Act
        var result = await _service!.SendDmWithQueueAsync(
            TestUserId,
            "test_notification",
            "Test message");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.Failed, Is.False);
        }

        // Verify user's bot_dm_enabled was updated
        var user = await _userRepository!.GetByTelegramIdAsync(TestUserId);
        Assert.That(user!.BotDmEnabled, Is.True);
    }

    [Test]
    public async Task SendDmWithQueueAsync_Blocked403_QueuesNotificationForLater()
    {
        // Arrange
        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new ApiRequestException("Forbidden", 403));

        // Act
        var result = await _service!.SendDmWithQueueAsync(
            TestUserId,
            "report_resolved",
            "Your report was resolved!");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.ErrorMessage, Does.Contain("queued for later"));
        }

        // Verify notification was queued
        var pendingNotifications = await _pendingNotificationsRepository!
            .GetPendingNotificationsForUserAsync(TestUserId);

        Assert.That(pendingNotifications, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pendingNotifications[0].NotificationType, Is.EqualTo("report_resolved"));
            Assert.That(pendingNotifications[0].MessageText, Is.EqualTo("Your report was resolved!"));
        }
    }

    #endregion

    #region SendDmWithKeyboardAsync Tests

    [Test]
    public async Task SendDmWithKeyboardAsync_Success_ReturnsDmSent()
    {
        // Arrange
        var keyboard = new global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
            new[]
            {
                global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("Accept", "accept"),
                global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("Decline", "decline")
            }
        );

        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 777, chatId: TestUserId));

        // Act
        var result = await _service!.SendDmWithKeyboardAsync(
            TestUserId,
            "Choose an option:",
            keyboard);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.Failed, Is.False);
            Assert.That(result.MessageId, Is.EqualTo(777));
        }

        // Verify user's bot_dm_enabled was updated
        var user = await _userRepository!.GetByTelegramIdAsync(TestUserId);
        Assert.That(user!.BotDmEnabled, Is.True);
    }

    [Test]
    public async Task SendDmWithKeyboardAsync_Blocked403_DoesNotQueue()
    {
        // Arrange - Keyboards can't be queued (stateful/interactive)
        var keyboard = new global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
            global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("Button", "callback")
        );

        _mockMessageHandler.SendAsync(
            Arg.Is<long>(id => id == TestUserId),
            Arg.Any<string>(),
            Arg.Any<global::Telegram.Bot.Types.Enums.ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new ApiRequestException("Forbidden", 403));

        // Act
        var result = await _service!.SendDmWithKeyboardAsync(
            TestUserId,
            "Choose an option:",
            keyboard);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.ErrorMessage, Does.Contain("cannot be queued"));
        }

        // Verify NO notification was queued (keyboard messages can't be queued)
        var pendingNotifications = await _pendingNotificationsRepository!
            .GetPendingNotificationsForUserAsync(TestUserId);
        Assert.That(pendingNotifications, Is.Empty);
    }

    #endregion

    #region DeleteDmMessageAsync Tests

    [Test]
    public async Task DeleteDmMessageAsync_CallsHandler()
    {
        // Arrange
        const int messageId = 12345;

        // Act
        await _service!.DeleteDmMessageAsync(TestUserId, messageId);

        // Assert
        await _mockMessageHandler.Received(1).DeleteAsync(
            TestUserId,
            messageId,
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private async Task SeedTestUser(long userId, bool botDmEnabled)
    {
        await using var context = _testHelper!.GetDbContext();

        context.TelegramUsers.Add(new Data.Models.TelegramUserDto
        {
            TelegramUserId = userId,
            FirstName = "TestUser",
            Username = "testuser",
            IsBot = false,
            IsTrusted = false,
            IsBanned = false,
            BotDmEnabled = botDmEnabled,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private async Task SeedManagedChat(long chatId, string chatName)
    {
        await using var context = _testHelper!.GetDbContext();

        context.ManagedChats.Add(new Data.Models.ManagedChatRecordDto
        {
            ChatId = chatId,
            ChatName = chatName,
            ChatType = Data.Models.ManagedChatType.Supergroup,
            BotStatus = Data.Models.BotChatStatus.Administrator,
            IsAdmin = true,
            IsActive = true,
            AddedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }

    #endregion
}
