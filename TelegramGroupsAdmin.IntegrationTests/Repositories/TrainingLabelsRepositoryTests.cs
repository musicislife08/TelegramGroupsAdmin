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
/// - Unique PostgreSQL database per test (test_db_xxx)
/// - GoldenDataset provides realistic test data with 5 training labels
/// - Tests use existing messages (Msg1-11) for FK validation
/// </summary>
[TestFixture]
public class TrainingLabelsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITrainingLabelsRepository? _repository;
    private AppDbContext? _context;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Add DbContextFactory (TrainingLabelsRepository uses this)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        // Add logging with test-specific suppressions
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        // Register repository
        services.AddScoped<ITrainingLabelsRepository, TrainingLabelsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<ITrainingLabelsRepository>();

        // Seed GoldenDataset test data
        _context = _testHelper.GetDbContext();
        await GoldenDataset.SeedDatabaseAsync(_context);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region UpsertLabelAsync Tests

    [Test]
    public async Task UpsertLabelAsync_NewSpamLabel_ShouldInsert()
    {
        // Arrange - Use GoldenDataset message without existing label
        long messageId = GoldenDataset.Messages.Msg6_Id;
        long userId = GoldenDataset.TelegramUsers.User3_TelegramUserId;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            TrainingLabel.Spam,
            userId,
            "Manual spam marking by admin",
            auditLogId: 123);

        // Assert - Verify inserted
        var label = await _repository.GetByMessageIdAsync(messageId);
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.MessageId, Is.EqualTo(messageId));
        Assert.That(label.Label, Is.EqualTo(TrainingLabel.Spam));
        Assert.That(label.LabeledByUserId, Is.EqualTo(userId));
        Assert.That(label.Reason, Is.EqualTo("Manual spam marking by admin"));
        Assert.That(label.AuditLogId, Is.EqualTo(123));
        Assert.That(label.LabeledAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Test]
    public async Task UpsertLabelAsync_NewHamLabel_ShouldInsert()
    {
        // Arrange - Use GoldenDataset message without existing label
        long messageId = GoldenDataset.Messages.Msg7_Id;

        // Act
        await _repository!.UpsertLabelAsync(
            messageId,
            TrainingLabel.Ham,
            labeledByUserId: null, // System-generated
            "False positive correction");

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId);
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.Label, Is.EqualTo(TrainingLabel.Ham));
        Assert.That(label.LabeledByUserId, Is.Null);
    }

    [Test]
    public async Task UpsertLabelAsync_ExistingLabel_ShouldUpdate()
    {
        // Arrange - Use existing GoldenDataset label (Msg1 = spam)
        long messageId = GoldenDataset.TrainingLabels.Label1_MessageId;

        // Act - Update to ham (false positive correction)
        await _repository!.UpsertLabelAsync(
            messageId,
            TrainingLabel.Ham,
            GoldenDataset.TelegramUsers.User4_TelegramUserId,
            "Corrected to ham");

        // Assert - Should update, not duplicate
        var label = await _repository.GetByMessageIdAsync(messageId);
        Assert.That(label!.Label, Is.EqualTo(TrainingLabel.Ham));
        Assert.That(label.LabeledByUserId, Is.EqualTo(GoldenDataset.TelegramUsers.User4_TelegramUserId));
        Assert.That(label.Reason, Is.EqualTo("Corrected to ham"));

        // Verify only ONE row in database
        var count = await _testHelper!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM training_labels WHERE message_id = {messageId}");
        Assert.That(count, Is.EqualTo(1), "Should update existing row, not create duplicate");
    }

    [Test]
    public async Task UpsertLabelAsync_ConcurrentUpserts_ShouldHandleRaceCondition()
    {
        // Arrange - Use GoldenDataset message without existing label
        long messageId = GoldenDataset.Messages.Msg11_Id;
        long userId1 = GoldenDataset.TelegramUsers.User3_TelegramUserId;
        long userId2 = GoldenDataset.TelegramUsers.User4_TelegramUserId;

        // Act - Fire 10 concurrent upserts to same message_id (race condition)
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_repository!.UpsertLabelAsync(messageId, TrainingLabel.Spam, userId1, "Concurrent spam"));
            tasks.Add(_repository!.UpsertLabelAsync(messageId, TrainingLabel.Ham, userId2, "Concurrent ham"));
        }
        await Task.WhenAll(tasks);

        // Assert - Verify only ONE row exists (PostgreSQL ON CONFLICT handles race correctly)
        var count = await _testHelper!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM training_labels WHERE message_id = {messageId}");
        Assert.That(count, Is.EqualTo(1), "Concurrent upserts should result in exactly one row (last writer wins)");

        // Verify the final label is valid (either spam or ham, depending on last writer)
        var label = await _repository.GetByMessageIdAsync(messageId);
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.Label, Is.AnyOf(TrainingLabel.Spam, TrainingLabel.Ham), "Final label should be valid");
    }

    #endregion

    #region GetByMessageIdAsync Tests

    [Test]
    public async Task GetByMessageIdAsync_LabelExists_ShouldReturnLabel()
    {
        // Arrange - Use existing GoldenDataset label
        long messageId = GoldenDataset.TrainingLabels.Label2_MessageId;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId);

        // Assert
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.MessageId, Is.EqualTo(messageId));
        Assert.That(label.Label, Is.EqualTo(TrainingLabel.Spam));
    }

    [Test]
    public async Task GetByMessageIdAsync_LabelNotExists_ShouldReturnNull()
    {
        // Arrange - Use message without label
        long messageId = GoldenDataset.Messages.Msg8_Id;

        // Act
        var label = await _repository!.GetByMessageIdAsync(messageId);

        // Assert
        Assert.That(label, Is.Null);
    }

    #endregion

    #region DeleteLabelAsync Tests

    [Test]
    public async Task DeleteLabelAsync_ExistingLabel_ShouldDelete()
    {
        // Arrange - Use existing GoldenDataset label
        long messageId = GoldenDataset.TrainingLabels.Label3_MessageId;

        // Act
        await _repository!.DeleteLabelAsync(messageId);

        // Assert
        var label = await _repository.GetByMessageIdAsync(messageId);
        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task DeleteLabelAsync_NonExistentLabel_ShouldNotThrow()
    {
        // Arrange - Use message without label
        long messageId = GoldenDataset.Messages.Msg9_Id;

        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () =>
        {
            await _repository!.DeleteLabelAsync(messageId);
        });
    }

    #endregion

    #region Database Constraint Tests

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceUniqueMessageId()
    {
        // Arrange - Use existing GoldenDataset label
        long messageId = GoldenDataset.TrainingLabels.Label4_MessageId;

        // Raw SQL insert should violate PK constraint
        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, label, labeled_at)
                VALUES ({messageId}, 0, NOW())
            ");
        });
    }

    [Test]
    public async Task Database_TrainingLabels_ShouldEnforceCheckConstraint()
    {
        // Arrange - Use message without existing label
        long messageId = GoldenDataset.Messages.Msg10_Id;

        // Verify check constraint: label IN (0, 1)
        Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await _testHelper!.ExecuteSqlAsync($@"
                INSERT INTO training_labels (message_id, label, labeled_at)
                VALUES ({messageId}, 99, NOW())
            ");
        });
    }

    #endregion
}
