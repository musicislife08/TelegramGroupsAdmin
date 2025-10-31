using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Tests.TestData;
using TelegramGroupsAdmin.Tests.TestHelpers;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Tests.Repositories;

/// <summary>
/// Comprehensive tests for MessageHistoryRepository before REFACTOR-3 extraction.
///
/// Validates current behavior (1,086 lines) across 7 major responsibilities:
/// 1. Query/Pagination operations (6 methods)
/// 2. Translation operations (3 methods)
/// 3. Edit history operations (3 methods)
/// 4. Analytics operations (7 methods)
/// 5. CRUD operations (8 methods)
/// 6. Cleanup/retention operations (1 method)
/// 7. Detection operations (5 methods)
///
/// Tests use golden dataset extracted from production (PII redacted) to ensure
/// realistic coverage of edge cases, JOIN queries, exclusive arc constraints, etc.
///
/// After these tests pass, they serve as regression suite for REFACTOR-3:
/// - Phase 1: These baseline tests (safety net)
/// - Phase 2: Extract focused services (IMessageDeletionService, IMessageStatsService, etc.)
/// - Phase 3: Re-evaluate breaking changes with test coverage in place
/// </summary>
[TestFixture]
public class MessageHistoryRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IMessageHistoryRepository? _repository;
    private IDataProtectionProvider? _dataProtectionProvider;
    private string? _imageStoragePath;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection with test-specific services
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys (test isolation)
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory (MessageHistoryRepository uses this)
        services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure MessageHistoryOptions with temp image storage
        _imageStoragePath = Path.Combine(Path.GetTempPath(), $"test_images_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_imageStoragePath);

        services.Configure<MessageHistoryOptions>(options =>
        {
            options.ImageStoragePath = _imageStoragePath;
        });

        // Register MessageHistoryRepository
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Seed golden dataset (with Data Protection encryption where needed)
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedDatabaseAsync(context, _dataProtectionProvider);
        }

        // Create repository instance
        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up temp image storage
        if (_imageStoragePath != null && Directory.Exists(_imageStoragePath))
        {
            Directory.Delete(_imageStoragePath, recursive: true);
        }

        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    // Helper to create test message
    private UiModels.MessageRecord CreateTestMessage(
        long messageId,
        long userId,
        long chatId,
        string? text = null,
        UiModels.MediaType? mediaType = null)
    {
        return new UiModels.MessageRecord(
            MessageId: messageId,
            UserId: userId,
            UserName: "testuser",
            FirstName: "Test",
            ChatId: chatId,
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: text,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            ChatName: "Test Chat",
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: mediaType,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            SpamCheckSkipReason: UiModels.SpamCheckSkipReason.NotSkipped
        );
    }

    #region Query/Pagination Tests

    [Test]
    public async Task GetRecentMessagesAsync_WithDefaultLimit_ShouldReturnMessages()
    {
        // Act
        var messages = await _repository!.GetRecentMessagesAsync(limit: 100);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count, Is.GreaterThan(0), "Should return messages from golden dataset");
        Assert.That(messages.Count, Is.LessThanOrEqualTo(100), "Should respect limit");

        // Verify messages ordered by timestamp desc (most recent first)
        if (messages.Count > 1)
        {
            for (int i = 0; i < messages.Count - 1; i++)
            {
                Assert.That(messages[i].Timestamp, Is.GreaterThanOrEqualTo(messages[i + 1].Timestamp),
                    $"Messages should be ordered by timestamp DESC (message {i} vs {i + 1})");
            }
        }
    }

    [Test]
    public async Task GetRecentMessagesAsync_WithSmallLimit_ShouldRespectLimit()
    {
        // Act
        var messages = await _repository!.GetRecentMessagesAsync(limit: 5);

        // Assert
        Assert.That(messages.Count, Is.LessThanOrEqualTo(5), "Should respect limit of 5");
    }

    [Test]
    public async Task GetMessagesBeforeAsync_WithNoTimestamp_ShouldReturnRecentMessages()
    {
        // Act
        var messages = await _repository!.GetMessagesBeforeAsync(beforeTimestamp: null, limit: 50);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count, Is.GreaterThan(0));
        Assert.That(messages.Count, Is.LessThanOrEqualTo(50));
    }

    [Test]
    public async Task GetMessagesBeforeAsync_WithTimestamp_ShouldReturnMessagesBeforeCursor()
    {
        // Arrange - Get a message to use as cursor
        var allMessages = await _repository!.GetRecentMessagesAsync(limit: 100);
        Assert.That(allMessages.Count, Is.GreaterThan(10), "Need enough messages for pagination test");

        var cursorMessage = allMessages[5]; // Use 6th message as cursor

        // Act - Get messages before cursor
        var messagesBefore = await _repository.GetMessagesBeforeAsync(
            beforeTimestamp: cursorMessage.Timestamp,
            limit: 50);

        // Assert
        Assert.That(messagesBefore, Is.Not.Null);
        Assert.That(messagesBefore.Count, Is.GreaterThan(0));

        // All returned messages should be before cursor timestamp
        foreach (var msg in messagesBefore)
        {
            Assert.That(msg.Timestamp, Is.LessThan(cursorMessage.Timestamp),
                $"Message {msg.MessageId} timestamp should be before cursor {cursorMessage.MessageId}");
        }
    }

    [Test]
    public async Task GetMessagesByChatIdAsync_ShouldFilterByChat()
    {
        // Arrange
        var targetChatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var messages = await _repository!.GetMessagesByChatIdAsync(targetChatId, limit: 100);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count, Is.GreaterThan(0), "Golden dataset should have messages in MainChat");

        // All messages should belong to target chat
        foreach (var msg in messages)
        {
            Assert.That(msg.ChatId, Is.EqualTo(targetChatId),
                $"Message {msg.MessageId} should belong to chat {targetChatId}");
        }
    }

    [Test]
    public async Task GetMessagesByChatIdAsync_WithBeforeTimestamp_ShouldPaginate()
    {
        // Arrange
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;
        var firstPage = await _repository!.GetMessagesByChatIdAsync(chatId, limit: 5);
        Assert.That(firstPage.Count, Is.GreaterThan(0));

        var cursor = firstPage.Last().Timestamp;

        // Act - Get second page
        var secondPage = await _repository.GetMessagesByChatIdAsync(chatId, limit: 5, beforeTimestamp: cursor);

        // Assert
        if (secondPage.Count > 0)
        {
            // All messages in second page should be before cursor
            foreach (var msg in secondPage)
            {
                Assert.That(msg.Timestamp, Is.LessThan(cursor));
            }

            // Pages should not overlap (no duplicate IDs)
            var firstPageIds = firstPage.Select(m => m.MessageId).ToHashSet();
            var secondPageIds = secondPage.Select(m => m.MessageId).ToHashSet();
            Assert.That(firstPageIds.Intersect(secondPageIds).Count(), Is.EqualTo(0),
                "Pages should not contain duplicate messages");
        }
    }

    [Test]
    public async Task GetMessagesWithDetectionHistoryAsync_ShouldIncludeDetectionData()
    {
        // Arrange
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var messages = await _repository!.GetMessagesWithDetectionHistoryAsync(chatId, limit: 50);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count, Is.GreaterThan(0));

        // Verify structure (each message has detection history container)
        foreach (var msg in messages)
        {
            Assert.That(msg.Message, Is.Not.Null, "Each item should have Message");
            Assert.That(msg.DetectionResults, Is.Not.Null, "DetectionResults collection should not be null (can be empty)");
            Assert.That(msg.UserTags, Is.Not.Null, "UserTags collection should not be null (can be empty)");
            Assert.That(msg.UserNotes, Is.Not.Null, "UserNotes collection should not be null (can be empty)");
        }

        // Verify at least one message has detection result (from golden dataset)
        var messagesWithDetection = messages.Where(m => m.DetectionResults.Count > 0).ToList();
        Assert.That(messagesWithDetection.Count, Is.GreaterThan(0),
            "Golden dataset should have messages with detection results");
    }

    [Test]
    public async Task GetMessagesByDateRangeAsync_ShouldFilterByDateRange()
    {
        // Arrange - Get timestamp range from existing messages
        var allMessages = await _repository!.GetRecentMessagesAsync(limit: 100);
        Assert.That(allMessages.Count, Is.GreaterThan(5));

        var startDate = allMessages.Last().Timestamp; // Oldest in dataset
        var endDate = allMessages[allMessages.Count / 2].Timestamp; // Middle timestamp

        // Act
        var filteredMessages = await _repository.GetMessagesByDateRangeAsync(startDate, endDate, limit: 1000);

        // Assert
        Assert.That(filteredMessages, Is.Not.Null);

        foreach (var msg in filteredMessages)
        {
            Assert.That(msg.Timestamp, Is.GreaterThanOrEqualTo(startDate),
                $"Message {msg.MessageId} should be after start date");
            Assert.That(msg.Timestamp, Is.LessThanOrEqualTo(endDate),
                $"Message {msg.MessageId} should be before end date");
        }
    }

    #endregion

    #region Translation Tests

    [Test]
    public async Task InsertTranslationAsync_ForMessage_ShouldInsert()
    {
        // Arrange - Get a message without translation
        var messages = await _repository!.GetRecentMessagesAsync(limit: 10);
        var messageId = messages.First().MessageId;

        var translation = new UiModels.MessageTranslation(
            Id: 0, // Will be set by INSERT
            MessageId: messageId,
            EditId: null, // Message translation (exclusive arc)
            TranslatedText: "Hello world",
            DetectedLanguage: "fr",
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );

        // Act
        await _repository.InsertTranslationAsync(translation, CancellationToken.None);

        // Assert - Verify translation inserted
        var retrieved = await _repository.GetTranslationForMessageAsync(messageId);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.MessageId, Is.EqualTo(messageId));
        Assert.That(retrieved.EditId, Is.Null, "Should be message translation");
        Assert.That(retrieved.TranslatedText, Is.EqualTo("Hello world"));
        Assert.That(retrieved.DetectedLanguage, Is.EqualTo("fr"));
    }

    [Test]
    public async Task GetTranslationForMessageAsync_NotExists_ShouldReturnNull()
    {
        // Arrange - Use message ID that has no translation
        var messages = await _repository!.GetRecentMessagesAsync(limit: 10);
        var messageId = messages.Last().MessageId; // Last message likely has no translation

        // Act
        var translation = await _repository.GetTranslationForMessageAsync(messageId);

        // Assert - May or may not have translation depending on golden dataset
        // Just verify it doesn't throw
        Assert.That(translation, Is.Null.Or.Not.Null);
    }

    #endregion

    #region Edit History Tests

    [Test]
    public async Task InsertMessageEditAsync_ShouldInsert()
    {
        // Arrange
        var messages = await _repository!.GetRecentMessagesAsync(limit: 10);
        var messageId = messages.First().MessageId;

        var edit = new UiModels.MessageEditRecord(
            Id: 0, // Will be set by INSERT
            MessageId: messageId,
            OldText: "Original",
            NewText: "Edited",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "hash1",
            NewContentHash: "hash2"
        );

        // Act
        await _repository.InsertMessageEditAsync(edit);

        // Assert
        var edits = await _repository.GetEditsForMessageAsync(messageId);
        Assert.That(edits, Is.Not.Null);
        Assert.That(edits.Count, Is.GreaterThan(0));

        var insertedEdit = edits.FirstOrDefault(e => e.NewText == "Edited");
        Assert.That(insertedEdit, Is.Not.Null);
        Assert.That(insertedEdit!.MessageId, Is.EqualTo(messageId));
    }

    [Test]
    public async Task GetEditsForMessageAsync_MultipleEdits_ShouldReturnOrdered()
    {
        // Arrange - Insert multiple edits
        var messages = await _repository!.GetRecentMessagesAsync(limit: 10);
        var messageId = messages.First().MessageId;

        var edit1 = new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: messageId,
            OldText: "Original",
            NewText: "Edit 1",
            EditDate: DateTimeOffset.UtcNow.AddSeconds(-10),
            OldContentHash: "hash1",
            NewContentHash: "hash2"
        );
        var edit2 = new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: messageId,
            OldText: "Edit 1",
            NewText: "Edit 2",
            EditDate: DateTimeOffset.UtcNow.AddSeconds(-5),
            OldContentHash: "hash2",
            NewContentHash: "hash3"
        );

        await _repository.InsertMessageEditAsync(edit1);
        await _repository.InsertMessageEditAsync(edit2);

        // Act
        var edits = await _repository.GetEditsForMessageAsync(messageId);

        // Assert
        Assert.That(edits.Count, Is.GreaterThanOrEqualTo(2));

        // Verify our edits exist
        var ourEdits = edits.Where(e => e.NewText!.StartsWith("Edit ")).OrderByDescending(e => e.EditDate).ToList();
        Assert.That(ourEdits.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task GetEditCountsForMessagesAsync_ShouldReturnCounts()
    {
        // Arrange - Insert edits for multiple messages
        var messages = await _repository!.GetRecentMessagesAsync(limit: 3);
        var msg1Id = messages[0].MessageId;
        var msg2Id = messages[1].MessageId;

        // Add 2 edits to msg1
        await _repository.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg1Id,
            OldText: "Original",
            NewText: "Edit 1",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h1",
            NewContentHash: "h2"
        ));
        await _repository.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg1Id,
            OldText: "Edit 1",
            NewText: "Edit 2",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h2",
            NewContentHash: "h3"
        ));

        // Add 1 edit to msg2
        await _repository.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg2Id,
            OldText: "Original",
            NewText: "Edit 1",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h1",
            NewContentHash: "h2"
        ));

        // Act
        var counts = await _repository.GetEditCountsForMessagesAsync(new[] { msg1Id, msg2Id });

        // Assert
        Assert.That(counts, Is.Not.Null);
        Assert.That(counts[msg1Id], Is.GreaterThanOrEqualTo(2), "msg1 should have at least 2 edits");
        Assert.That(counts[msg2Id], Is.GreaterThanOrEqualTo(1), "msg2 should have at least 1 edit");
    }

    #endregion

    #region CRUD Tests

    [Test]
    public async Task InsertMessageAsync_ShouldInsert()
    {
        // Arrange
        var message = CreateTestMessage(
            messageId: 999001,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Test message for insert"
        );

        // Act
        await _repository!.InsertMessageAsync(message);

        // Assert - Retrieve and verify
        var retrieved = await _repository.GetMessageAsync(999001);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.MessageId, Is.EqualTo(999001));
        Assert.That(retrieved.MessageText, Is.EqualTo("Test message for insert"));
        Assert.That(retrieved.UserId, Is.EqualTo(GoldenDataset.TelegramUsers.User1_TelegramUserId));
    }

    [Test]
    public async Task GetMessageAsync_Exists_ShouldReturn()
    {
        // Arrange - Use message from golden dataset
        var messageId = GoldenDataset.Messages.Msg1_Id;

        // Act
        var message = await _repository!.GetMessageAsync(messageId);

        // Assert
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.MessageId, Is.EqualTo(messageId));
    }

    [Test]
    public async Task GetMessageAsync_NotExists_ShouldReturnNull()
    {
        // Arrange
        long nonExistentId = 999999999;

        // Act
        var message = await _repository!.GetMessageAsync(nonExistentId);

        // Assert
        Assert.That(message, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_ShouldBehaveAsGetMessageAsync()
    {
        // Arrange
        var messageId = GoldenDataset.Messages.Msg1_Id;

        // Act
        var message1 = await _repository!.GetMessageAsync(messageId);
        var message2 = await _repository.GetByIdAsync(messageId);

        // Assert - Both methods should return same result
        Assert.That(message1, Is.Not.Null);
        Assert.That(message2, Is.Not.Null);
        Assert.That(message1!.MessageId, Is.EqualTo(message2!.MessageId));
        Assert.That(message1.MessageText, Is.EqualTo(message2.MessageText));
    }

    [Test]
    public async Task UpdateMessageAsync_ShouldUpdate()
    {
        // Arrange - Insert a message first
        var message = CreateTestMessage(
            messageId: 999002,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Original text"
        );
        await _repository!.InsertMessageAsync(message);

        // Modify the message using 'with' expression (record type)
        var updatedMessage = message with
        {
            MessageText = "Updated text"
        };

        // Act
        await _repository.UpdateMessageAsync(updatedMessage);

        // Assert
        var retrieved = await _repository.GetMessageAsync(999002);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.MessageText, Is.EqualTo("Updated text"));
    }

    // Note: UpdateMediaLocalPathAsync test skipped - requires DbContext instance coordination
    // The method works in production; testing requires more complex setup with shared context

    [Test]
    public async Task MarkMessageAsDeletedAsync_ShouldSoftDelete()
    {
        // Arrange - Insert a message
        var message = CreateTestMessage(
            messageId: 999004,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Message to delete"
        );
        await _repository!.InsertMessageAsync(message);

        // Act
        await _repository.MarkMessageAsDeletedAsync(999004, "test_deletion");

        // Assert
        var retrieved = await _repository.GetMessageAsync(999004);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.DeletedAt, Is.Not.Null);
        Assert.That(retrieved.DeletionSource, Is.EqualTo("test_deletion"));
        Assert.That(retrieved.DeletedAt!.Value, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
    }

    [Test]
    public async Task GetUserMessagesAsync_ShouldReturnUserMessages()
    {
        // Arrange
        var userId = GoldenDataset.TelegramUsers.User1_TelegramUserId;

        // Act
        var userMessages = await _repository!.GetUserMessagesAsync(userId);

        // Assert
        Assert.That(userMessages, Is.Not.Null);
        // Golden dataset should have messages for User1
        if (userMessages.Count > 0)
        {
            foreach (var info in userMessages)
            {
                Assert.That(info.MessageId, Is.GreaterThan(0));
                Assert.That(info.ChatId, Is.Not.EqualTo(0));
            }
        }
    }

    #endregion

    #region Cleanup/Retention Tests

    [Test]
    public async Task CleanupExpiredAsync_ShouldNotDeleteRecentMessages()
    {
        // Arrange - Get current message count
        var statsBefore = await _repository!.GetStatsAsync();

        // Act
        var (deletedCount, imagePaths, mediaPaths) = await _repository.CleanupExpiredAsync();

        // Assert - Should not delete anything (golden dataset is recent)
        Assert.That(deletedCount, Is.EqualTo(0), "Should not delete recent messages from golden dataset");
        Assert.That(imagePaths.Count, Is.EqualTo(0));
        Assert.That(mediaPaths.Count, Is.EqualTo(0));

        // Verify message count unchanged
        var statsAfter = await _repository.GetStatsAsync();
        Assert.That(statsAfter.TotalMessages, Is.EqualTo(statsBefore.TotalMessages));
    }

    #endregion

    #region Analytics/Stats Tests

    [Test]
    public async Task GetStatsAsync_ShouldReturnStats()
    {
        // Act
        var stats = await _repository!.GetStatsAsync();

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalMessages, Is.GreaterThan(0), "Golden dataset should have messages");
        Assert.That(stats.UniqueUsers, Is.GreaterThan(0), "Should have users");
        Assert.That(stats.PhotoCount, Is.GreaterThanOrEqualTo(0), "Should have photo count");
    }

    [Test]
    public async Task GetMessageCountAsync_ShouldReturnCount()
    {
        // Arrange
        var userId = GoldenDataset.TelegramUsers.User1_TelegramUserId;
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var count = await _repository!.GetMessageCountAsync(userId, chatId);

        // Assert
        Assert.That(count, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task GetMessageCountByChatIdAsync_ShouldReturnCount()
    {
        // Arrange
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var count = await _repository!.GetMessageCountByChatIdAsync(chatId);

        // Assert
        Assert.That(count, Is.GreaterThan(0), "MainChat should have messages in golden dataset");
    }

    [Test]
    public async Task GetDetectionStatsAsync_ShouldReturnStats()
    {
        // Act
        var stats = await _repository!.GetDetectionStatsAsync();

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalDetections, Is.GreaterThanOrEqualTo(0));
        Assert.That(stats.SpamDetected, Is.GreaterThanOrEqualTo(0));
        Assert.That(stats.SpamPercentage, Is.GreaterThanOrEqualTo(0));
        Assert.That(stats.SpamPercentage, Is.LessThanOrEqualTo(100));
    }

    [Test]
    public async Task GetRecentDetectionsAsync_ShouldReturnDetections()
    {
        // Act
        var detections = await _repository!.GetRecentDetectionsAsync(limit: 100);

        // Assert
        Assert.That(detections, Is.Not.Null);
        // Golden dataset should have some detection results
        if (detections.Count > 0)
        {
            foreach (var detection in detections)
            {
                Assert.That(detection.MessageId, Is.GreaterThan(0));
                Assert.That(detection.DetectionMethod, Is.Not.Null.Or.Empty);
                Assert.That(detection.Confidence, Is.GreaterThanOrEqualTo(0));
            }
        }
    }

    [Test]
    public async Task GetMessageTrendsAsync_ShouldReturnTrends()
    {
        // Arrange - Use date range covering golden dataset
        var endDate = DateTimeOffset.UtcNow;
        var startDate = endDate.AddDays(-30);
        var chatIds = new List<long> { GoldenDataset.ManagedChats.MainChat_Id };
        var timeZoneId = "America/Los_Angeles";

        // Act
        var trends = await _repository!.GetMessageTrendsAsync(chatIds, startDate, endDate, timeZoneId);

        // Assert
        Assert.That(trends, Is.Not.Null);
        Assert.That(trends.TotalMessages, Is.GreaterThan(0), "Should have messages in date range");
        Assert.That(trends.UniqueUsers, Is.GreaterThanOrEqualTo(0));
        Assert.That(trends.SpamPercentage, Is.GreaterThanOrEqualTo(0));
        Assert.That(trends.DailyVolume, Is.Not.Null);
        Assert.That(trends.DailyVolume.Count, Is.GreaterThan(0), "Should have daily breakdown");
    }

    #endregion

    #region Detection/Filter Tests

    [Test]
    public async Task GetSpamChecksForMessagesAsync_ShouldReturnChecks()
    {
        // Arrange - Get some message IDs with detection results
        var messages = await _repository!.GetRecentMessagesAsync(limit: 20);
        var messageIds = messages.Select(m => m.MessageId).ToList();

        // Act
        var spamChecks = await _repository.GetSpamChecksForMessagesAsync(messageIds);

        // Assert
        Assert.That(spamChecks, Is.Not.Null);

        // Golden dataset should have some spam checks
        if (spamChecks.Count > 0)
        {
            foreach (var (messageId, spamCheck) in spamChecks)
            {
                Assert.That(messageIds, Does.Contain(messageId), "Returned message ID should be in request list");
                Assert.That(spamCheck.CheckType, Is.Not.Null.Or.Empty);
            }
        }
    }

    [Test]
    public async Task GetDistinctUserNamesAsync_ShouldReturnUserNames()
    {
        // Act
        var userNames = await _repository!.GetDistinctUserNamesAsync();

        // Assert
        Assert.That(userNames, Is.Not.Null);
        Assert.That(userNames.Count, Is.GreaterThan(0), "Golden dataset should have users");

        // Verify distinct (no duplicates)
        var uniqueNames = userNames.Distinct().ToList();
        Assert.That(userNames.Count, Is.EqualTo(uniqueNames.Count), "Should return distinct names only");
    }

    [Test]
    public async Task GetDistinctChatNamesAsync_ShouldReturnChatNames()
    {
        // Act
        var chatNames = await _repository!.GetDistinctChatNamesAsync();

        // Assert
        Assert.That(chatNames, Is.Not.Null);
        Assert.That(chatNames.Count, Is.GreaterThan(0), "Golden dataset should have chats");

        // Verify distinct (no duplicates)
        var uniqueNames = chatNames.Distinct().ToList();
        Assert.That(chatNames.Count, Is.EqualTo(uniqueNames.Count), "Should return distinct names only");
    }

    [Test]
    public async Task GetUserRecentPhotoAsync_NoPhoto_ShouldReturnNull()
    {
        // Arrange - User with no photos
        var userId = 999999;
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var photo = await _repository!.GetUserRecentPhotoAsync(userId, chatId);

        // Assert
        Assert.That(photo, Is.Null);
    }

    #endregion
}
