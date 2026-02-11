using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services.Bot;

/// <summary>
/// Integration tests for BotMessageService.
/// Tests message sending with DB persistence, edit history tracking, and deletion marking.
///
/// Architecture:
/// - Service sends messages via IBotMessageHandler (mocked Telegram API)
/// - Persists messages to real PostgreSQL via IMessageHistoryRepository
/// - Creates edit history records via IMessageEditService
/// - Upserts bot user to telegram_users table
///
/// Test Strategy:
/// - Real PostgreSQL for message persistence and edit history
/// - Mocked IBotMessageHandler and IBotChatHandler for API responses
/// - Mocked IBotUserService for bot identity (returns cached bot info)
/// </summary>
[TestFixture]
public class BotMessageServiceTests
{
    private const long TestChatId = -100123456789L;
    private const string TestChatName = "Test Group";
    private const long TestBotId = 987654321L;
    private const string TestBotUsername = "test_bot";

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBotMessageService? _service;
    private IMessageHistoryRepository? _messageRepo;
    private IMessageEditService? _editService;
    private IBotMessageHandler _mockMessageHandler = null!;
    private IBotUserService _mockUserService = null!;
    private IBotChatHandler _mockChatHandler = null!;
    private string? _tempMediaPath;

    private static readonly User TestBot = new()
    {
        Id = TestBotId,
        IsBot = true,
        FirstName = "TestBot",
        Username = TestBotUsername
    };

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Create temp directory for media files
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"BotMessageServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempMediaPath);

        // Set up mocks for external services
        _mockMessageHandler = Substitute.For<IBotMessageHandler>();
        _mockUserService = Substitute.For<IBotUserService>();
        _mockChatHandler = Substitute.For<IBotChatHandler>();

        // Configure bot identity mock (returns cached bot info)
        _mockUserService.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestBot);

        // Set up dependency injection
        var services = new ServiceCollection();

        // Add NpgsqlDataSource (required by some services)
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure MessageHistoryOptions with temp path
        services.Configure<MessageHistoryOptions>(opt =>
            opt.ImageStoragePath = _tempMediaPath);

        // Register Core services (SimHashService required by MessageHistoryRepository)
        services.AddCoreServices();

        // Register repositories (real implementations)
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddScoped<IMessageEditService, MessageEditService>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        // Register mocked external services
        services.AddSingleton(_mockMessageHandler);
        services.AddSingleton(_mockUserService);
        services.AddSingleton(_mockChatHandler);

        // Register BotMessageService
        services.AddScoped<IBotMessageService, BotMessageService>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _service = scope.ServiceProvider.GetRequiredService<IBotMessageService>();
        _messageRepo = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
        _editService = scope.ServiceProvider.GetRequiredService<IMessageEditService>();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();

        // Clean up temp directory
        if (_tempMediaPath != null && Directory.Exists(_tempMediaPath))
        {
            try { Directory.Delete(_tempMediaPath, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    #region SendAndSaveMessageAsync Tests

    [Test]
    public async Task SendAndSaveMessageAsync_SavesMessageToDatabase()
    {
        // Arrange
        const int messageId = 12345;
        const string text = "Hello, this is a test message!";

        var sentMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup);
        sentMessage.Chat.Title = TestChatName;

        _mockMessageHandler.SendAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(sentMessage);

        // Act
        var result = await _service!.SendAndSaveMessageAsync(TestChatId, text);

        // Assert - Message returned
        Assert.That(result.Id, Is.EqualTo(messageId));

        // Assert - Message saved to database
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        Assert.That(savedMessage, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedMessage!.MessageId, Is.EqualTo(messageId));
            Assert.That(savedMessage.MessageText, Is.EqualTo(text));
            Assert.That(savedMessage.Chat.Id, Is.EqualTo(TestChatId));
            Assert.That(savedMessage.User.Id, Is.EqualTo(TestBotId));
            Assert.That(savedMessage.User.Username, Is.EqualTo(TestBotUsername));
        }
    }

    [Test]
    public async Task SendAndSaveMessageAsync_WithReplyTo_SavesReplyInfo()
    {
        // Arrange
        const int messageId = 12345;
        const int replyToMessageId = 99999;
        const string text = "This is a reply!";

        var sentMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup);
        sentMessage.Chat.Title = TestChatName;

        _mockMessageHandler.SendAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(sentMessage);

        var replyParameters = new ReplyParameters { MessageId = replyToMessageId };

        // Act
        await _service!.SendAndSaveMessageAsync(
            TestChatId,
            text,
            replyParameters: replyParameters);

        // Assert - Reply info saved
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        Assert.That(savedMessage!.ReplyToMessageId, Is.EqualTo(replyToMessageId));
    }

    [Test]
    public async Task SendAndSaveMessageAsync_UpsertsBotUser()
    {
        // Arrange
        const int messageId = 12345;

        var sentMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup);
        sentMessage.Chat.Title = TestChatName;

        _mockMessageHandler.SendAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(sentMessage);

        // Act
        await _service!.SendAndSaveMessageAsync(TestChatId, "Test message");

        // Assert - Bot user was created in telegram_users table
        await using var context = _testHelper!.GetDbContext();
        var botUser = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == TestBotId);

        Assert.That(botUser, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(botUser!.Username, Is.EqualTo(TestBotUsername));
            Assert.That(botUser.IsBot, Is.True);
        }
    }

    #endregion

    #region EditAndUpdateMessageAsync Tests

    [Test]
    public async Task EditAndUpdateMessageAsync_CreatesEditHistoryRecord()
    {
        // Arrange - First create a message in the database
        const int messageId = 12345;
        const string originalText = "Original message text";
        const string editedText = "Updated message text";

        await SeedMessage(messageId, TestChatId, originalText);

        var editedMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup,
            text: editedText);
        editedMessage.EditDate = DateTime.UtcNow;

        _mockMessageHandler.EditTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(editedMessage);

        // Act
        await _service!.EditAndUpdateMessageAsync(TestChatId, messageId, editedText);

        // Assert - Edit history created
        var edits = await _editService!.GetEditsForMessageAsync(messageId);
        Assert.That(edits, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(edits[0].OldText, Is.EqualTo(originalText));
            Assert.That(edits[0].NewText, Is.EqualTo(editedText));
        }
    }

    [Test]
    public async Task EditAndUpdateMessageAsync_UpdatesMessageInDatabase()
    {
        // Arrange - First create a message in the database
        const int messageId = 12345;
        const string originalText = "Original message text";
        const string editedText = "Updated message text";

        await SeedMessage(messageId, TestChatId, originalText);

        var editedMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup,
            text: editedText);
        editedMessage.EditDate = DateTime.UtcNow;

        _mockMessageHandler.EditTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(editedMessage);

        // Act
        await _service!.EditAndUpdateMessageAsync(TestChatId, messageId, editedText);

        // Assert - Message text updated in database
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedMessage!.MessageText, Is.EqualTo(editedText));
            Assert.That(savedMessage.EditDate, Is.Not.Null);
        }
    }

    [Test]
    public void EditAndUpdateMessageAsync_MessageNotFound_ThrowsException()
    {
        // Arrange - No message in database
        const int messageId = 99999;
        const string editedText = "Updated text";

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service!.EditAndUpdateMessageAsync(TestChatId, messageId, editedText));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    #endregion

    #region DeleteAndMarkMessageAsync Tests

    [Test]
    public async Task DeleteAndMarkMessageAsync_MarksMessageAsDeleted()
    {
        // Arrange
        const int messageId = 12345;
        await SeedMessage(messageId, TestChatId, "Message to delete");

        _mockMessageHandler.DeleteAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        ).Returns(Task.CompletedTask);

        // Act
        await _service!.DeleteAndMarkMessageAsync(TestChatId, messageId, "test_deletion");

        // Assert - Message marked as deleted in database
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedMessage!.DeletedAt, Is.Not.Null);
            Assert.That(savedMessage.DeletionSource, Is.EqualTo("test_deletion"));
        }
    }

    [Test]
    public async Task DeleteAndMarkMessageAsync_TelegramApiFails_StillMarksAsDeleted()
    {
        // Arrange
        const int messageId = 12345;
        await SeedMessage(messageId, TestChatId, "Message to delete");

        _mockMessageHandler.DeleteAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new Exception("Message already deleted"));

        // Act & Assert - Throws but still marks as deleted
        Assert.ThrowsAsync<Exception>(async () =>
            await _service!.DeleteAndMarkMessageAsync(TestChatId, messageId, "test_deletion"));

        // Assert - Message still marked as deleted (with "_failed" suffix)
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedMessage!.DeletedAt, Is.Not.Null);
            Assert.That(savedMessage.DeletionSource, Is.EqualTo("test_deletion_failed"));
        }
    }

    #endregion

    #region SendAndSaveAnimationAsync Tests

    [Test]
    public async Task SendAndSaveAnimationAsync_SavesAnimationToDatabase()
    {
        // Arrange
        const int messageId = 12345;
        const string caption = "Ban celebration!";

        var sentMessage = TelegramTestFactory.CreateMessage(
            messageId: messageId,
            chatId: TestChatId,
            chatType: ChatType.Supergroup);
        sentMessage.Chat.Title = TestChatName;
        sentMessage.Animation = new Animation
        {
            FileId = "test_animation_file_id",
            FileSize = 1024,
            Duration = 3
        };

        _mockMessageHandler.SendAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string?>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>()
        ).Returns(sentMessage);

        // Act
        var result = await _service!.SendAndSaveAnimationAsync(
            TestChatId,
            InputFile.FromFileId("test_file_id"),
            caption);

        // Assert - Message returned with animation
        Assert.That(result.Animation, Is.Not.Null);

        // Assert - Message saved with media metadata
        var savedMessage = await _messageRepo!.GetMessageAsync(messageId);
        Assert.That(savedMessage, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedMessage!.MessageText, Is.EqualTo(caption));
            Assert.That(savedMessage.MediaType, Is.EqualTo(TelegramGroupsAdmin.Telegram.Models.MediaType.Animation));
            Assert.That(savedMessage.MediaFileId, Is.EqualTo("test_animation_file_id"));
        }
    }

    #endregion

    #region Helper Methods

    private async Task SeedMessage(int messageId, long chatId, string text)
    {
        await using var context = _testHelper!.GetDbContext();

        // Ensure bot user exists first (required for foreign key)
        var existingBot = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == TestBotId);

        if (existingBot == null)
        {
            context.TelegramUsers.Add(new Data.Models.TelegramUserDto
            {
                TelegramUserId = TestBotId,
                FirstName = "TestBot",
                Username = TestBotUsername,
                IsBot = true,
                IsTrusted = false,
                IsBanned = false,
                BotDmEnabled = false,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        context.Messages.Add(new Data.Models.MessageRecordDto
        {
            MessageId = messageId,
            UserId = TestBotId,
            ChatId = chatId,
            MessageText = text,
            Timestamp = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }

    #endregion
}
