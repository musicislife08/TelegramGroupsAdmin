using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
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
/// All anchor IDs are pinned via <see cref="GoldenDatasetConstants.TrainingLabels"/>,
/// <see cref="GoldenDatasetConstants.Chats"/>, and <see cref="GoldenDatasetConstants.TelegramUsers"/>.
/// </summary>
[TestFixture]
public class TrainingLabelsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITrainingLabelsRepository? _repository;

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
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg1Id;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            GoldenDatasetConstants.Chats.TrainingFixturesChatId,
            TrainingLabel.Spam,
            Actor.FromTelegramUser(GoldenDatasetConstants.TelegramUsers.TrainingLabelActorId),
            "Manual spam marking by admin",
            auditLogId: 123);

        // Assert - Verify inserted
        var label = await _repository.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.TrainingFixturesChatId);
        Assert.That(label, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.MessageId, Is.EqualTo(messageId));
            Assert.That(label.Label, Is.EqualTo(TrainingLabel.Spam));
            Assert.That(label.LabeledByUserId, Is.EqualTo(GoldenDatasetConstants.TelegramUsers.TrainingLabelActorId));
            Assert.That(label.Reason, Is.EqualTo("Manual spam marking by admin"));
            Assert.That(label.AuditLogId, Is.EqualTo(123));
            Assert.That(label.LabeledAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
    }

    [Test]
    public async Task UpsertLabelAsync_NewHamLabel_ShouldInsert()
    {
        // Arrange - canonical message with no existing training_label
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg2Id;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            GoldenDatasetConstants.Chats.LandOwnersChatId,
            TrainingLabel.Ham,
            actor: Actor.SystemSeed, // System-generated
            "False positive correction");

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.LandOwnersChatId);
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
        int messageId = GoldenDatasetConstants.TrainingLabels.ExistingSpamMsgId;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            GoldenDatasetConstants.Chats.TrainingFixturesChatId,
            TrainingLabel.Ham,
            Actor.FromTelegramUser(GoldenDatasetConstants.TelegramUsers.TopMainChatHamAuthorId),
            "Corrected to ham");

        // Assert - Should update, not duplicate
        var label = await _repository.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.TrainingFixturesChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label!.Label, Is.EqualTo(TrainingLabel.Ham));
            Assert.That(label.LabeledByUserId, Is.EqualTo(GoldenDatasetConstants.TelegramUsers.TopMainChatHamAuthorId));
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
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg3Id;
        long chatId = GoldenDatasetConstants.Chats.LandOwnersChatId;
        long spamActor = GoldenDatasetConstants.TelegramUsers.TrainingLabelActorId;
        long hamActor = GoldenDatasetConstants.TelegramUsers.SecondMainChatHamAuthorId;

        // Act - Fire 10 concurrent upserts to same message_id (race condition)
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_repository!.UpsertLabelAsync(messageId, chatId, TrainingLabel.Spam, Actor.FromTelegramUser(spamActor), "Concurrent spam"));
            tasks.Add(_repository!.UpsertLabelAsync(messageId, chatId, TrainingLabel.Ham, Actor.FromTelegramUser(hamActor), "Concurrent ham"));
        }
        await Task.WhenAll(tasks);

        // Assert - Verify only ONE row exists (PostgreSQL ON CONFLICT handles race correctly)
        var count = await _testHelper!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM training_labels WHERE message_id = {messageId}");
        Assert.That(count, Is.EqualTo(1), "Concurrent upserts should result in exactly one row (last writer wins)");

        // Verify the final label is valid (either spam or ham, depending on last writer)
        var label = await _repository!.GetByMessageIdAsync(messageId, chatId);
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.Label, Is.AnyOf(TrainingLabel.Spam, TrainingLabel.Ham), "Final label should be valid");
    }

    #endregion

    #region GetByMessageIdAsync Tests

    [Test]
    public async Task GetByMessageIdAsync_LabelExists_ShouldReturnLabel()
    {
        // Arrange - canonical spam label (label=0)
        int messageId = GoldenDatasetConstants.TrainingLabels.ExistingSpamMsgId;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.TrainingFixturesChatId);

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
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg4Id;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.LandOwnersChatId);

        // Assert
        Assert.That(label, Is.Null);
    }

    #endregion

    #region DeleteLabelAsync Tests

    [Test]
    public async Task DeleteLabelAsync_ExistingLabel_ShouldDelete()
    {
        // Arrange - canonical ham label (label=1)
        int messageId = GoldenDatasetConstants.TrainingLabels.ExistingHamMsgId;

        // Act
        await _repository!.DeleteLabelAsync(messageId, GoldenDatasetConstants.Chats.TrainingFixturesChatId);

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId, GoldenDatasetConstants.Chats.TrainingFixturesChatId);
        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task DeleteLabelAsync_NonExistentLabel_ShouldNotThrow()
    {
        // Arrange - canonical message with no training_label
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg5Id;

        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () =>
        {
            await _repository!.DeleteLabelAsync(messageId, GoldenDatasetConstants.Chats.LandOwnersChatId);
        });
    }

    #endregion

    #region Database Constraint Tests

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceUniqueMessageId()
    {
        // Arrange - canonical spam label (label=0) — raw insert should violate PK constraint
        int messageId = GoldenDatasetConstants.TrainingLabels.ExistingSpam2MsgId;

        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, chat_id, label, labeled_at)
                VALUES ({messageId}, {GoldenDatasetConstants.Chats.TrainingFixturesChatId}, 0, NOW())
            ");
        });
    }

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceCheckConstraint()
    {
        // Arrange - canonical message with no training_label; invalid label value (99) should fail
        int messageId = GoldenDatasetConstants.TrainingLabels.UnlabeledMsg6Id;

        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, chat_id, label, labeled_at)
                VALUES ({messageId}, {GoldenDatasetConstants.Chats.LandOwnersChatId}, 99, NOW())
            ");
        });
    }

    #endregion
}
