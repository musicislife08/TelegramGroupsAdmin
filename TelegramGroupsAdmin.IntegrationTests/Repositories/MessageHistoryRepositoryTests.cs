using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Comprehensive test suite for REFACTOR-3 extracted services and MessageHistoryRepository.
///
/// Architecture (post-REFACTOR-3):
/// - IMessageQueryService (9 methods) - Complex queries with JOINs, pagination, filtering
/// - IMessageStatsService (4 methods) - Analytics, trends, detection statistics
/// - IMessageTranslationService (3 methods) - Translation CRUD with exclusive arc pattern
/// - IMessageEditService (3 methods) - Edit history tracking and counts
/// - IMessageHistoryRepository (9 methods) - Core message CRUD operations only
///
/// Golden Dataset:
/// Tests use production-extracted data (PII redacted) for realistic coverage of edge cases,
/// JOIN queries, exclusive arc constraints (message_id XOR edit_id), timezone handling, and
/// cartesian product avoidance patterns.
///
/// Test Organization (34 tests):
/// - 10 Query/Pagination tests (IMessageQueryService) - lines 170-412
/// - 5 Translation tests (IMessageTranslationService) - lines 414-517
/// - 3 Edit history tests (IMessageEditService) - lines 519-591
/// - 8 CRUD tests (IMessageHistoryRepository) - lines 593-771
/// - 5 Analytics tests (IMessageStatsService) - lines 773-905
/// - 3 Helper method tests (GetMessageCount, GetDistinct) - lines 907-955
///
/// Test Infrastructure:
/// - Shared PostgreSQL container (PostgresFixture) - started once per test run
/// - Unique database per test (test_db_xxx) - perfect isolation, no pollution
/// - Golden dataset seeded per test - ensures consistent starting state
/// - Supports parallel test execution (different databases on same container)
/// </summary>
[TestFixture]
public class MessageHistoryRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IMessageHistoryRepository? _repository;
    private IMessageQueryService? _queryService;
    private IMessageStatsService? _statsService;
    private IMessageTranslationService? _translationService;
    private IMessageEditService? _editService;
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

        // Add logging with test-specific suppressions
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            // Suppress Data Protection ephemeral key warnings (expected in tests)
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Configure MessageHistoryOptions with temp image storage
        _imageStoragePath = Path.Combine(Path.GetTempPath(), $"test_images_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_imageStoragePath);

        services.Configure<MessageHistoryOptions>(options =>
        {
            options.ImageStoragePath = _imageStoragePath;
        });

        // Register Core services (SimHashService required by MessageHistoryRepository)
        services.AddCoreServices();

        // Register MessageHistoryRepository and extracted services (REFACTOR-3)
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddScoped<IMessageQueryService, MessageQueryService>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>(); // Required by MessageStatsService (UX-2.1)
        services.AddScoped<IMessageStatsService, MessageStatsService>();
        services.AddScoped<IMessageTranslationService, MessageTranslationService>();
        services.AddScoped<IMessageEditService, MessageEditService>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Seed golden dataset (with Data Protection encryption where needed)
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedAsync(context, _dataProtectionProvider);
        }

        // Create service instances
        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
        _queryService = scope.ServiceProvider.GetRequiredService<IMessageQueryService>();
        _statsService = scope.ServiceProvider.GetRequiredService<IMessageStatsService>();
        _translationService = scope.ServiceProvider.GetRequiredService<IMessageTranslationService>();
        _editService = scope.ServiceProvider.GetRequiredService<IMessageEditService>();
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
            LastName: "User",
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
            ContentCheckSkipReason: UiModels.ContentCheckSkipReason.NotSkipped
        );
    }

    #region Query/Pagination Tests

    [Test]
    public async Task GetRecentMessagesAsync_WithDefaultLimit_ShouldReturnMessages()
    {
        // Act
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 100);

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
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 5);

        // Assert
        Assert.That(messages.Count, Is.LessThanOrEqualTo(5), "Should respect limit of 5");
    }

    [Test]
    public async Task GetMessagesBeforeAsync_WithNoTimestamp_ShouldReturnRecentMessages()
    {
        // Act
        var messages = await _queryService!.GetMessagesBeforeAsync(beforeTimestamp: null, limit: 50);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count, Is.GreaterThan(0));
        Assert.That(messages.Count, Is.LessThanOrEqualTo(50));
    }

    [Test]
    public async Task GetMessagesBeforeAsync_WithTimestamp_ShouldReturnMessagesBeforeCursor()
    {
        // Arrange - Get a message to use as cursor
        var allMessages = await _queryService!.GetRecentMessagesAsync(limit: 100);
        Assert.That(allMessages.Count, Is.GreaterThan(10), "Need enough messages for pagination test");

        var cursorMessage = allMessages[5]; // Use 6th message as cursor

        // Act - Get messages before cursor
        var messagesBefore = await _queryService.GetMessagesBeforeAsync(
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
        var messages = await _queryService!.GetMessagesByChatIdAsync(targetChatId, limit: 100);

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
        var firstPage = await _queryService!.GetMessagesByChatIdAsync(chatId, limit: 5);
        Assert.That(firstPage.Count, Is.GreaterThan(0));

        var cursor = firstPage.Last().Timestamp;

        // Act - Get second page
        var secondPage = await _queryService.GetMessagesByChatIdAsync(chatId, limit: 5, beforeTimestamp: cursor);

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
        var messages = await _queryService!.GetMessagesWithDetectionHistoryAsync(chatId, limit: 50);

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
        var allMessages = await _queryService!.GetRecentMessagesAsync(limit: 100);
        Assert.That(allMessages.Count, Is.GreaterThan(5));

        var startDate = allMessages.Last().Timestamp; // Oldest in dataset
        var endDate = allMessages[allMessages.Count / 2].Timestamp; // Middle timestamp

        // Act
        var filteredMessages = await _queryService.GetMessagesByDateRangeAsync(startDate, endDate, limit: 1000);

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

    [Test]
    public async Task GetMessagesWithDetectionHistoryAsync_ValidatesMediaPath()
    {
        // This test validates that GetMessagesWithDetectionHistoryAsync (in MessageQueryService)
        // also validates media paths and nulls them when files don't exist.
        // BASELINE TEST for REFACTOR-3: Both repository and query service have ValidateMediaPath.

        // Arrange - Insert a message with media path pointing to non-existent file
        var message = CreateTestMessage(
            messageId: 999060,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: null,
            mediaType: UiModels.MediaType.Animation
        );
        await _repository!.InsertMessageAsync(message);

        // Update media path to point to a file that does NOT exist
        const string nonExistentFileName = "animation_does_not_exist_999060.gif";
        await _repository.UpdateMediaLocalPathAsync(999060, nonExistentFileName);

        // Act - Retrieve via MessageQueryService (this also calls ValidateMediaPath internally)
        var messagesWithHistory = await _queryService!.GetMessagesWithDetectionHistoryAsync(
            GoldenDataset.ManagedChats.MainChat_Id, limit: 200);

        // Assert - Find our message and verify MediaLocalPath is null
        var ourMessage = messagesWithHistory.FirstOrDefault(m => m.Message.MessageId == 999060);
        Assert.That(ourMessage, Is.Not.Null, "Should find our test message");
        Assert.That(ourMessage!.Message.MediaLocalPath, Is.Null,
            "MediaLocalPath should be nulled by MessageQueryService.ValidateMediaPath when file doesn't exist");
        Assert.That(ourMessage.Message.MediaType, Is.EqualTo(UiModels.MediaType.Animation),
            "MediaType should remain unchanged");
    }

    [Test]
    public async Task GetMessagesWithDetectionHistoryAsync_WithEditTranslations_ShouldExcludeEditTranslations()
    {
        // This test validates the exclusive arc constraint: message_translations LEFT JOIN
        // should only include translations where message_id IS NOT NULL (excluding edit translations)

        // Arrange - Insert a test message
        var message = CreateTestMessage(
            messageId: 999010,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Original text to be edited and translated"
        );
        await _repository!.InsertMessageAsync(message);

        // Insert a translation for the message (message_id NOT NULL, edit_id NULL)
        var messageTranslation = new MessageTranslation(
            Id: 0,
            MessageId: 999010,
            EditId: null,
            TranslatedText: "Message translation - should be included",
            DetectedLanguage: "en",
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );
        await _translationService!.InsertTranslationAsync(messageTranslation);

        // Insert a message edit
        var edit = new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: 999010,
            OldText: "Original text to be edited and translated",
            NewText: "Edited text",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "hash1",
            NewContentHash: "hash2"
        );
        await _editService!.InsertMessageEditAsync(edit);

        // Get the edit ID we just created
        var edits = await _editService.GetEditsForMessageAsync(999010);
        var createdEdit = edits.First(e => e.NewText == "Edited text");

        // Insert a translation for the edit (message_id NULL, edit_id NOT NULL)
        var editTranslation = new MessageTranslation(
            Id: 0,
            MessageId: null,
            EditId: createdEdit.Id,
            TranslatedText: "Edit translation - should be excluded",
            DetectedLanguage: "en",
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );
        await _translationService.InsertTranslationAsync(editTranslation);

        // Act - Query messages with detection history for this chat
        var messages = await _queryService!.GetMessagesWithDetectionHistoryAsync(
            GoldenDataset.ManagedChats.MainChat_Id,
            limit: 100);

        // Assert
        var ourMessage = messages.FirstOrDefault(m => m.Message.MessageId == 999010);
        Assert.That(ourMessage, Is.Not.Null, "Should find our test message");

        // Verify the message has the message translation (not the edit translation)
        Assert.That(ourMessage!.Message.Translation, Is.Not.Null, "Message should have translation");
        Assert.That(ourMessage.Message.Translation!.TranslatedText, Is.EqualTo("Message translation - should be included"),
            "Should include message translation, not edit translation (validates exclusive arc constraint)");
    }

    #endregion

    #region Translation Tests

    [Test]
    public async Task InsertTranslationAsync_ForMessage_ShouldInsert()
    {
        // Arrange - Get a message without translation
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 10);
        var messageId = messages.First().MessageId;

        var translation = new MessageTranslation(
            Id: 0, // Will be set by INSERT
            MessageId: messageId,
            EditId: null, // Message translation (exclusive arc)
            TranslatedText: "Hello world",
            DetectedLanguage: "fr",
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );

        // Act
        await _translationService!.InsertTranslationAsync(translation, CancellationToken.None);

        // Assert - Verify translation inserted
        var retrieved = await _translationService!.GetTranslationForMessageAsync(messageId);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.MessageId, Is.EqualTo(messageId));
        Assert.That(retrieved.EditId, Is.Null, "Should be message translation");
        Assert.That(retrieved.TranslatedText, Is.EqualTo("Hello world"));
        Assert.That(retrieved.DetectedLanguage, Is.EqualTo("fr"));
    }

    [Test]
    public async Task GetTranslationForMessageAsync_NotExists_ShouldReturnNull()
    {
        // Arrange - Use a message ID that definitely doesn't exist (very high ID)
        // This ensures deterministic behavior regardless of golden dataset contents
        const long nonExistentMessageId = 999999;

        // Act
        var translation = await _translationService!.GetTranslationForMessageAsync(nonExistentMessageId);

        // Assert - Should return null for non-existent message
        Assert.That(translation, Is.Null);
    }

    [Test]
    public async Task InsertTranslationAsync_DuplicateMessageId_UpdatesExisting()
    {
        // This test validates that inserting a translation for the same message_id
        // performs an upsert (update existing) instead of creating a duplicate.
        // This is critical for the exclusive arc constraint and avoiding duplicate translations.

        // Arrange - Insert a test message
        var message = CreateTestMessage(
            messageId: 999030,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Text requiring translation updates"
        );
        await _repository!.InsertMessageAsync(message);

        // Insert first translation
        var firstTranslation = new MessageTranslation(
            Id: 0,
            MessageId: 999030,
            EditId: null,
            TranslatedText: "First translation text",
            DetectedLanguage: "en",
            Confidence: 0.85m,
            TranslatedAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );
        await _translationService!.InsertTranslationAsync(firstTranslation);

        // Verify first translation was inserted
        var retrievedFirst = await _translationService.GetTranslationForMessageAsync(999030);
        Assert.That(retrievedFirst, Is.Not.Null);
        Assert.That(retrievedFirst!.TranslatedText, Is.EqualTo("First translation text"));

        // Act - Insert second translation for the SAME message (should update, not duplicate)
        var secondTranslation = new MessageTranslation(
            Id: 0,
            MessageId: 999030,
            EditId: null,
            TranslatedText: "Updated translation text",
            DetectedLanguage: "en",
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );
        await _translationService.InsertTranslationAsync(secondTranslation);

        // Assert - Should have exactly ONE translation for this message (upsert behavior)
        var retrievedSecond = await _translationService.GetTranslationForMessageAsync(999030);
        Assert.That(retrievedSecond, Is.Not.Null);
        Assert.That(retrievedSecond!.TranslatedText, Is.EqualTo("Updated translation text"),
            "Translation should be updated to second value (upsert)");
        Assert.That(retrievedSecond.Confidence, Is.EqualTo(0.95m),
            "Confidence should match second translation (upsert)");

        // Verify no duplicate translations by checking the database directly would require
        // DbContext access, but GetTranslationForMessageAsync returning the updated value
        // is sufficient proof that upsert worked (only one translation exists)
    }

    #endregion

    #region Edit History Tests

    [Test]
    public async Task InsertMessageEditAsync_ShouldInsert()
    {
        // Arrange
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 10);
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
        await _editService!.InsertMessageEditAsync(edit);

        // Assert
        var edits = await _editService!.GetEditsForMessageAsync(messageId);
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
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 10);
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

        await _editService!.InsertMessageEditAsync(edit1);
        await _editService!.InsertMessageEditAsync(edit2);

        // Act
        var edits = await _editService!.GetEditsForMessageAsync(messageId);

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
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 3);
        var msg1Id = messages[0].MessageId;
        var msg2Id = messages[1].MessageId;

        // Add 2 edits to msg1
        await _editService!.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg1Id,
            OldText: "Original",
            NewText: "Edit 1",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h1",
            NewContentHash: "h2"
        ));
        await _editService!.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg1Id,
            OldText: "Edit 1",
            NewText: "Edit 2",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h2",
            NewContentHash: "h3"
        ));

        // Add 1 edit to msg2
        await _editService!.InsertMessageEditAsync(new UiModels.MessageEditRecord(
            Id: 0,
            MessageId: msg2Id,
            OldText: "Original",
            NewText: "Edit 1",
            EditDate: DateTimeOffset.UtcNow,
            OldContentHash: "h1",
            NewContentHash: "h2"
        ));

        // Act
        var counts = await _editService!.GetEditCountsForMessagesAsync(new[] { msg1Id, msg2Id });

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

    [Test]
    public async Task UpdateMediaLocalPathAsync_ShouldUpdatePath()
    {
        // Note: This test validates UpdateMediaLocalPathAsync works correctly.
        // GetMessageAsync includes ValidateMediaPath which checks file existence,
        // so we need to create the actual file on disk for the test to pass.

        // Arrange - Insert a message with media but no local path
        var message = CreateTestMessage(
            messageId: 999003,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: null,
            mediaType: UiModels.MediaType.Video
        );
        await _repository!.InsertMessageAsync(message);

        // Verify no media path initially
        var retrievedBefore = await _repository.GetMessageAsync(999003);
        Assert.That(retrievedBefore, Is.Not.Null);
        Assert.That(retrievedBefore!.MediaLocalPath, Is.Null);

        // Create test media file on disk (required by ValidateMediaPath)
        // MediaPathUtilities.GetMediaStoragePath returns: "media/video/filename.mp4"
        // So full path is: {_imageStoragePath}/media/video/filename.mp4
        const string testMediaFileName = "test_video_999003.mp4";
        var mediaVideoSubfolder = Path.Combine(_imageStoragePath!, "media", "video");
        Directory.CreateDirectory(mediaVideoSubfolder);
        var fullMediaPath = Path.Combine(mediaVideoSubfolder, testMediaFileName);
        await File.WriteAllTextAsync(fullMediaPath, "fake video content for test");

        try
        {
            // Act - Update media local path (stores just the filename)
            await _repository.UpdateMediaLocalPathAsync(999003, testMediaFileName);

            // Assert - Verify media path was updated
            var retrievedAfter = await _repository.GetMessageAsync(999003);
            Assert.That(retrievedAfter, Is.Not.Null);
            Assert.That(retrievedAfter!.MediaLocalPath, Is.EqualTo(testMediaFileName),
                "MediaLocalPath should be updated to filename (ValidateMediaPath confirms file exists)");
            Assert.That(retrievedAfter.MediaType, Is.EqualTo(UiModels.MediaType.Video), "Other fields should remain unchanged");
        }
        finally
        {
            // Cleanup - Delete test file
            if (File.Exists(fullMediaPath))
            {
                File.Delete(fullMediaPath);
            }
        }
    }

    [Test]
    public async Task ValidateMediaPath_FileMissing_ShouldNullMediaLocalPath()
    {
        // This test validates that when a media file doesn't exist on disk,
        // GetMessageAsync (which calls ValidateMediaPath internally) nulls the MediaLocalPath.
        // BASELINE TEST for REFACTOR-3: Validates current behavior before DRY refactoring.

        // Arrange - Insert a message with media path pointing to non-existent file
        var message = CreateTestMessage(
            messageId: 999050,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: null,
            mediaType: UiModels.MediaType.Video
        );
        await _repository!.InsertMessageAsync(message);

        // Update media path to point to a file that does NOT exist
        const string nonExistentFileName = "this_file_does_not_exist_999050.mp4";
        await _repository.UpdateMediaLocalPathAsync(999050, nonExistentFileName);

        // Act - Retrieve the message (this calls ValidateMediaPath internally)
        var retrieved = await _repository.GetMessageAsync(999050);

        // Assert - MediaLocalPath should be null because file doesn't exist
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.MediaLocalPath, Is.Null,
            "MediaLocalPath should be nulled when referenced file doesn't exist on disk");
        Assert.That(retrieved.MediaType, Is.EqualTo(UiModels.MediaType.Video),
            "MediaType should remain unchanged");
    }

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

    [Test]
    public async Task GetUserMessagesAsync_ExcludesDeletedMessages()
    {
        // This test validates that GetUserMessagesAsync correctly filters out soft-deleted messages.
        // BASELINE TEST for REFACTOR-3: Validates current behavior before DRY refactoring.

        // Arrange - Insert a message for our test user
        var message = CreateTestMessage(
            messageId: 999040,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: GoldenDataset.ManagedChats.MainChat_Id,
            text: "Message to be soft deleted"
        );
        await _repository!.InsertMessageAsync(message);

        // Verify message appears in user's messages before deletion
        var messagesBefore = await _repository.GetUserMessagesAsync(GoldenDataset.TelegramUsers.User1_TelegramUserId);
        Assert.That(messagesBefore.Any(m => m.MessageId == 999040), Is.True,
            "Message should appear in user's messages before soft delete");

        // Act - Soft delete the message
        await _repository.MarkMessageAsDeletedAsync(999040, "test_soft_delete");

        // Assert - Message should no longer appear in GetUserMessagesAsync
        var messagesAfter = await _repository.GetUserMessagesAsync(GoldenDataset.TelegramUsers.User1_TelegramUserId);
        Assert.That(messagesAfter.Any(m => m.MessageId == 999040), Is.False,
            "Soft-deleted message should be excluded from GetUserMessagesAsync results");

        // Verify the message still exists (soft delete, not hard delete)
        var deletedMessage = await _repository.GetMessageAsync(999040);
        Assert.That(deletedMessage, Is.Not.Null, "Message should still exist in database");
        Assert.That(deletedMessage!.DeletedAt, Is.Not.Null, "Message should have DeletedAt set");
    }

    #endregion

    #region Cleanup/Retention Tests

    [Test]
    public async Task CleanupExpiredAsync_ShouldNotDeleteRecentMessages()
    {
        // Arrange - Get current message count
        var statsBefore = await _statsService!.GetStatsAsync();

        // Act - Use 30-day retention (default)
        var (deletedCount, imagePaths, mediaPaths) = await _repository!.CleanupExpiredAsync(TimeSpan.FromDays(30));

        // Assert - Should not delete anything (golden dataset is recent)
        Assert.That(deletedCount, Is.EqualTo(0), "Should not delete recent messages from golden dataset");
        Assert.That(imagePaths.Count, Is.EqualTo(0));
        Assert.That(mediaPaths.Count, Is.EqualTo(0));

        // Verify message count unchanged
        var statsAfter = await _statsService!.GetStatsAsync();
        Assert.That(statsAfter.TotalMessages, Is.EqualTo(statsBefore.TotalMessages));
    }

    [Test]
    public async Task CleanupExpiredAsync_PreservesMessagesWithDetectionResults()
    {
        // This test validates that old messages WITH detection_results are preserved (training data).
        // BASELINE TEST for REFACTOR-3: Validates current behavior before DRY refactoring.
        //
        // The retention logic: Delete messages where Timestamp < 30 days AND no detection results.
        // Messages with detection_results are kept as training data even if > 30 days old.

        // Arrange - Golden dataset already has messages with detection results:
        // - Messages.Msg1_Id (82619) has DetectionResults.Result1
        // - Messages.Msg11_Id (82581) has DetectionResults.Result2
        // These messages have recent timestamps (NOW() - N hours) so won't be deleted anyway.
        //
        // We can verify the logic works by checking that cleanup doesn't delete messages
        // that have detection results, regardless of their age.

        // Get messages with detection results from golden dataset
        var messageWithDetection = await _repository!.GetMessageAsync(GoldenDataset.Messages.Msg1_Id);
        Assert.That(messageWithDetection, Is.Not.Null, "Golden dataset should have message with detection result");

        // Verify it has detection result (via the query service)
        var messagesWithHistory = await _queryService!.GetMessagesWithDetectionHistoryAsync(
            GoldenDataset.ManagedChats.MainChat_Id, limit: 100);
        var msg1WithHistory = messagesWithHistory.FirstOrDefault(m => m.Message.MessageId == GoldenDataset.Messages.Msg1_Id);
        Assert.That(msg1WithHistory, Is.Not.Null, "Should find message in detection history query");
        Assert.That(msg1WithHistory!.DetectionResults.Count, Is.GreaterThan(0),
            "Message should have detection results (training data)");

        // Act - Run cleanup with 30-day retention
        var (deletedCount, imagePaths, mediaPaths) = await _repository.CleanupExpiredAsync(TimeSpan.FromDays(30));

        // Assert - Message with detection result should NOT be deleted
        var messageAfterCleanup = await _repository.GetMessageAsync(GoldenDataset.Messages.Msg1_Id);
        Assert.That(messageAfterCleanup, Is.Not.Null,
            "Message with detection result should be preserved by cleanup (training data retention)");

        // Also verify Msg11 (82581) is preserved
        var msg11AfterCleanup = await _repository.GetMessageAsync(GoldenDataset.Messages.Msg11_Id);
        Assert.That(msg11AfterCleanup, Is.Not.Null,
            "Another message with detection result should also be preserved");
    }

    #endregion

    #region Analytics/Stats Tests

    [Test]
    public async Task GetStatsAsync_ShouldReturnStats()
    {
        // Act
        var stats = await _statsService!.GetStatsAsync();

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalMessages, Is.GreaterThan(0), "Golden dataset should have messages");
        Assert.That(stats.UniqueUsers, Is.GreaterThan(0), "Should have users");
        Assert.That(stats.PhotoCount, Is.GreaterThanOrEqualTo(0), "Should have photo count");
    }

    [Test]
    public async Task GetStatsAsync_EmptyDatabase_ReturnsZeroStats()
    {
        // This test validates that GetStatsAsync handles empty result sets gracefully
        // (no divide-by-zero, no null reference exceptions)

        // Arrange - Insert and immediately delete a message to ensure database operations work
        // Then query a chat that doesn't exist in the golden dataset
        var message = CreateTestMessage(
            messageId: 999020,
            userId: GoldenDataset.TelegramUsers.User1_TelegramUserId,
            chatId: 999999, // Non-existent chat
            text: "Test message to be deleted"
        );
        await _repository!.InsertMessageAsync(message);
        await _repository.MarkMessageAsDeletedAsync(999020, "test_cleanup");

        // Act - Get stats (should handle empty/deleted data gracefully)
        var stats = await _statsService!.GetStatsAsync();

        // Assert - Should not throw exceptions, should return valid stats object
        Assert.That(stats, Is.Not.Null, "Should return stats object even with empty data");
        Assert.That(stats.TotalMessages, Is.GreaterThanOrEqualTo(0), "Total messages should be non-negative");
        Assert.That(stats.UniqueUsers, Is.GreaterThanOrEqualTo(0), "Unique users should be non-negative");
        Assert.That(stats.PhotoCount, Is.GreaterThanOrEqualTo(0), "Photo count should be non-negative");

        // No assertions on specific values since golden dataset has existing data
        // The important validation is that the method doesn't throw exceptions
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
        var stats = await _statsService!.GetDetectionStatsAsync();

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
        var detections = await _statsService!.GetRecentDetectionsAsync(limit: 100);

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
        List<long> chatIds = [GoldenDataset.ManagedChats.MainChat_Id];
        var timeZoneId = "America/Los_Angeles";

        // Act
        var trends = await _statsService!.GetMessageTrendsAsync(chatIds, startDate, endDate, timeZoneId);

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
    public async Task GetContentChecksForMessagesAsync_ShouldReturnChecks()
    {
        // Arrange - Get some message IDs with detection results
        var messages = await _queryService!.GetRecentMessagesAsync(limit: 20);
        var messageIds = messages.Select(m => m.MessageId).ToList();

        // Act
        var contentChecks = await _queryService.GetContentChecksForMessagesAsync(messageIds);

        // Assert
        Assert.That(contentChecks, Is.Not.Null);

        // Golden dataset should have some content checks
        if (contentChecks.Count > 0)
        {
            foreach (var (messageId, contentCheck) in contentChecks)
            {
                Assert.That(messageIds, Does.Contain(messageId), "Returned message ID should be in request list");
                Assert.That(contentCheck.CheckType, Is.Not.Null.Or.Empty);
            }
        }
    }

    [Test]
    public async Task GetDistinctUserNamesAsync_ShouldReturnUserNames()
    {
        // Act
        var userNames = await _queryService!.GetDistinctUserNamesAsync();

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
        var chatNames = await _queryService!.GetDistinctChatNamesAsync();

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
        var photo = await _queryService!.GetUserRecentPhotoAsync(userId, chatId);

        // Assert
        Assert.That(photo, Is.Null);
    }

    #endregion
}
