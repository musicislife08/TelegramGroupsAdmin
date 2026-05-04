using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for TrainingLabelsRepository - ML training label CRUD operations.
///
/// Architecture:
/// - TrainingLabelsRepository handles explicit ML training labels (spam/ham classifications)
/// - Separate from detection_results (history) to avoid conflating "what happened" vs "what to learn"
/// - Uses training_labels table with PK on message_id (one label per message)
/// - Supports ON CONFLICT DO UPDATE pattern for admin corrections
///
/// Test Coverage (10 tests):
/// - UpsertLabelAsync: Insert new spam/ham labels, update existing labels, concurrent upserts
/// - GetByMessageIdAsync: Retrieve existing/non-existing labels
/// - DeleteLabelAsync: Delete existing/non-existing labels
/// - Database constraints: Verify PK uniqueness and check constraint enforcement
///
/// Test Infrastructure:
/// - Unique PostgreSQL database per test (cloned from golden_template)
/// - Canonical dataset provides 200 training labels
///
/// Canonical anchors:
/// - Existing spam label:    message_id=4575,  chat_id=-100048429560480
/// - Existing ham label:     message_id=4602,  chat_id=-100048429560480
/// - Another existing label: message_id=4655,  chat_id=-100048429560480 (PK constraint test)
/// - Unlabeled messages (FK-valid, no training_labels row):
///     message_id=4620,  chat_id=-100048429560480
///     message_id=7789,  chat_id=-100017312732389
///     message_id=7834,  chat_id=-100017312732389
///     message_id=7836,  chat_id=-100017312732389
///     message_id=7853,  chat_id=-100017312732389
///     message_id=8095,  chat_id=-100017312732389
/// - Telegram users used as actors: 9084745993769, 9921676191756, 9960171136314
/// </summary>
[TestFixture]
public class TrainingLabelsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITrainingLabelsRepository? _repository;

    // Canonical: existing spam label (label=0, labeled_by=9084745993769)
    private const int ExistingSpamMsgId = 4575;
    private const long ExistingSpamChatId = -100048429560480L;

    // Canonical: existing ham label (label=1)
    private const int ExistingHamMsgId = 4602;
    private const long ExistingHamChatId = -100048429560480L;

    // Canonical: second spam label — used for PK-uniqueness enforcement test
    private const int ExistingSpam2MsgId = 4655;
    private const long ExistingSpam2ChatId = -100048429560480L;

    // Canonical: unlabeled messages (no training_labels row; valid FK to messages)
    private const int UnlabeledMsg1Id = 4620;
    private const long UnlabeledMsg1ChatId = -100048429560480L;

    private const int UnlabeledMsg2Id = 7789;
    private const long UnlabeledMsg2ChatId = -100017312732389L;

    private const int UnlabeledMsg3Id = 7834;
    private const long UnlabeledMsg3ChatId = -100017312732389L;

    private const int UnlabeledMsg4Id = 7836;
    private const long UnlabeledMsg4ChatId = -100017312732389L;

    private const int UnlabeledMsg5Id = 7853;
    private const long UnlabeledMsg5ChatId = -100017312732389L;

    private const int UnlabeledMsg6Id = 8095;
    private const long UnlabeledMsg6ChatId = -100017312732389L;

    // Canonical telegram users used as actor identities
    private const long SpamActorUserId = 9084745993769L;   // appears in training_labels as labeled_by_user_id
    private const long HamActorUserId = 9921676191756L;    // top MainChat author
    private const long ConcurrentActor2UserId = 9960171136314L; // second active MainChat author

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<ITrainingLabelsRepository, TrainingLabelsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<ITrainingLabelsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region UpsertLabelAsync Tests

    [Test]
    public async Task UpsertLabelAsync_NewSpamLabel_ShouldInsert()
    {
        // Arrange - canonical message with no existing training_label
        int messageId = UnlabeledMsg1Id;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            UnlabeledMsg1ChatId,
            TrainingLabel.Spam,
            Actor.FromTelegramUser(SpamActorUserId),
            "Manual spam marking by admin",
            auditLogId: 123);

        // Assert - Verify inserted
        var label = await _repository.GetByMessageIdAsync(messageId, UnlabeledMsg1ChatId);
        Assert.That(label, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.MessageId, Is.EqualTo(messageId));
            Assert.That(label.Label, Is.EqualTo(TrainingLabel.Spam));
            Assert.That(label.LabeledByUserId, Is.EqualTo(SpamActorUserId));
            Assert.That(label.Reason, Is.EqualTo("Manual spam marking by admin"));
            Assert.That(label.AuditLogId, Is.EqualTo(123));
            Assert.That(label.LabeledAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
    }

    [Test]
    public async Task UpsertLabelAsync_NewHamLabel_ShouldInsert()
    {
        // Arrange - canonical message with no existing training_label
        int messageId = UnlabeledMsg2Id;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            UnlabeledMsg2ChatId,
            TrainingLabel.Ham,
            actor: Actor.SystemSeed, // System-generated
            "False positive correction");

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId, UnlabeledMsg2ChatId);
        Assert.That(label, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.Label, Is.EqualTo(TrainingLabel.Ham));
            Assert.That(label.LabeledByUserId, Is.Null);
        }
    }

    [Test]
    public async Task UpsertLabelAsync_ExistingLabel_ShouldUpdate()
    {
        // Arrange - canonical spam label (label=0); update to ham (false positive correction)
        int messageId = ExistingSpamMsgId;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            ExistingSpamChatId,
            TrainingLabel.Ham,
            Actor.FromTelegramUser(HamActorUserId),
            "Corrected to ham");

        // Assert - Should update, not duplicate
        var label = await _repository.GetByMessageIdAsync(messageId, ExistingSpamChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.Label, Is.EqualTo(TrainingLabel.Ham));
            Assert.That(label.LabeledByUserId, Is.EqualTo(HamActorUserId));
            Assert.That(label.Reason, Is.EqualTo("Corrected to ham"));
        }

        // Verify only ONE row in database
        var count = await _testHelper!.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM training_labels WHERE message_id = {messageId}");
        Assert.That(count, Is.EqualTo(1), "Should update existing row, not create duplicate");
    }

    [Test]
    public async Task UpsertLabelAsync_ConcurrentUpserts_ShouldHandleRaceCondition()
    {
        // Arrange - canonical message with no existing training_label
        int messageId = UnlabeledMsg3Id;

        // Act - Fire 10 concurrent upserts to same message_id (race condition)
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_repository!.UpsertLabelAsync(messageId, UnlabeledMsg3ChatId, TrainingLabel.Spam, Actor.FromTelegramUser(SpamActorUserId), "Concurrent spam"));
            tasks.Add(_repository!.UpsertLabelAsync(messageId, UnlabeledMsg3ChatId, TrainingLabel.Ham, Actor.FromTelegramUser(ConcurrentActor2UserId), "Concurrent ham"));
        }
        await Task.WhenAll(tasks);

        // Assert - Verify only ONE row exists (PostgreSQL ON CONFLICT handles race correctly)
        var count = await _testHelper!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM training_labels WHERE message_id = {messageId}");
        Assert.That(count, Is.EqualTo(1), "Concurrent upserts should result in exactly one row (last writer wins)");

        // Verify the final label is valid (either spam or ham, depending on last writer)
        var label = await _repository!.GetByMessageIdAsync(messageId, UnlabeledMsg3ChatId);
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.Label, Is.AnyOf(TrainingLabel.Spam, TrainingLabel.Ham), "Final label should be valid");
    }

    #endregion

    #region GetByMessageIdAsync Tests

    [Test]
    public async Task GetByMessageIdAsync_LabelExists_ShouldReturnLabel()
    {
        // Arrange - canonical spam label (label=0)
        int messageId = ExistingSpamMsgId;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId, ExistingSpamChatId);

        // Assert
        Assert.That(label, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.MessageId, Is.EqualTo(messageId));
            Assert.That(label.Label, Is.EqualTo(TrainingLabel.Spam));
        }
    }

    [Test]
    public async Task GetByMessageIdAsync_LabelNotExists_ShouldReturnNull()
    {
        // Arrange - canonical message with no training_label
        int messageId = UnlabeledMsg4Id;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId, UnlabeledMsg4ChatId);

        // Assert
        Assert.That(label, Is.Null);
    }

    #endregion

    #region DeleteLabelAsync Tests

    [Test]
    public async Task DeleteLabelAsync_ExistingLabel_ShouldDelete()
    {
        // Arrange - canonical ham label (label=1)
        int messageId = ExistingHamMsgId;

        // Act
        await _repository!.DeleteLabelAsync(messageId, ExistingHamChatId);

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId, ExistingHamChatId);
        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task DeleteLabelAsync_NonExistentLabel_ShouldNotThrow()
    {
        // Arrange - canonical message with no training_label
        int messageId = UnlabeledMsg5Id;

        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () =>
        {
            await _repository!.DeleteLabelAsync(messageId, UnlabeledMsg5ChatId);
        });
    }

    #endregion

    #region Database Constraint Tests

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceUniqueMessageId()
    {
        // Arrange - canonical spam label (label=0) — raw insert should violate PK constraint
        int messageId = ExistingSpam2MsgId;

        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, chat_id, label, labeled_at)
                VALUES ({messageId}, {ExistingSpam2ChatId}, 0, NOW())
            ");
        });
    }

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceCheckConstraint()
    {
        // Arrange - canonical message with no training_label; invalid label value (99) should fail
        int messageId = UnlabeledMsg6Id;

        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, chat_id, label, labeled_at)
                VALUES ({messageId}, {UnlabeledMsg6ChatId}, 99, NOW())
            ");
        });
    }

    #endregion
}
