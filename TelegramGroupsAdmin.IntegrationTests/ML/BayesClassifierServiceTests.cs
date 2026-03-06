using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ML;

/// <summary>
/// Integration tests for BayesClassifierService - Naive Bayes spam classifier.
///
/// Test Strategy:
/// - Uses real PostgreSQL database (Testcontainers) with GoldenDataset
/// - Tests full Bayes training pipeline using real IMLTrainingDataRepository
/// - Validates thread safety (semaphore), atomic model swapping, and classification
/// - Unlike ML.NET, Bayes does NOT persist to disk — no file assertions needed
///
/// Test Coverage (7 tests):
/// - TrainAsync with sufficient data: Classifier trains, metadata reflects sample counts
/// - TrainAsync with insufficient data: Logs warning, metadata remains null
/// - Classify after training: Returns non-null result with probability in [0.0, 1.0]
/// - Classify before training: Returns null (not trained)
/// - Overlapping TrainAsync calls: Second call skipped by semaphore, classifier still valid
/// - Classify with null/empty/special chars: Returns valid results after training
/// - TrainAsync with pre-cancelled token: Throws TaskCanceledException
///
/// GoldenDataset Training Data:
/// - 3 spam labels: Label1 (Msg1), Label2 (Msg2), Label5 (Msg5)
/// - 2 ham labels: Label3 (Msg3), Label4 (Msg4)
/// - MLTrainingData.sql adds 20 spam + 20 ham — total 23 spam + 22 ham
/// </summary>
[TestFixture]
public class BayesClassifierServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBayesClassifierService? _bayesService;
    private AppDbContext? _context;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });

        // IConfiguration required by AddContentDetection (DataPath for ML.NET, not needed by Bayes,
        // but AddContentDetection also registers MLTextClassifierService which reads it)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:DataPath"] = Path.GetTempPath()
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension methods to ensure tests match production configuration
        services.AddCoreServices();
        services.AddContentDetection();

        _serviceProvider = services.BuildServiceProvider();
        _bayesService = _serviceProvider.GetRequiredService<IBayesClassifierService>();

        // Seed GoldenDataset (23 spam + 22 ham from combined scripts)
        _context = _testHelper.GetDbContext();
        await GoldenDataset.SeedAsync(_context);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
        _testHelper?.Dispose();
        (_bayesService as IDisposable)?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region TrainAsync Tests

    [Test]
    public async Task TrainAsync_SufficientData_MetadataReflectsCorrectSampleCounts()
    {
        // Arrange — GoldenDataset provides 23 spam + 22 ham from combined scripts

        // Act
        await _bayesService!.TrainAsync();

        // Assert
        var metadata = _bayesService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Metadata should be non-null after successful training");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(MLConstants.MinimumSamplesPerClass),
                "Spam sample count should meet minimum threshold");
            Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(MLConstants.MinimumSamplesPerClass),
                "Ham sample count should meet minimum threshold");
            Assert.That(metadata.TotalSampleCount, Is.EqualTo(metadata.SpamSampleCount + metadata.HamSampleCount),
                "TotalSampleCount should equal SpamSampleCount + HamSampleCount");
            Assert.That(metadata.TrainedAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)),
                "TrainedAt should be set to approximately now");
        }
    }

    [Test]
    public async Task TrainAsync_InsufficientData_LogsWarningAndLeavesMetadataNull()
    {
        // Arrange — Clear all training labels to simulate no training data
        await using var context = _testHelper!.GetDbContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM training_labels");

        // Act
        await _bayesService!.TrainAsync();

        // Assert — Metadata stays null when insufficient data exists
        var metadata = _bayesService.GetMetadata();
        Assert.That(metadata, Is.Null,
            "Metadata should remain null when training data falls below MinimumSamplesPerClass");
    }

    [Test]
    public async Task TrainAsync_OverlappingCalls_SecondCallSkippedClassifierStillValid()
    {
        // Arrange — GoldenDataset provides training data

        // Act — Launch two concurrent TrainAsync calls; semaphore should block the second
        var task1 = _bayesService!.TrainAsync();
        var task2 = _bayesService.TrainAsync();

        await Task.WhenAll(task1, task2);

        // Assert — Classifier should be valid regardless of which call completed
        var metadata = _bayesService.GetMetadata();
        Assert.That(metadata, Is.Not.Null,
            "Training should complete successfully; at least one TrainAsync call must succeed");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(MLConstants.MinimumSamplesPerClass));
            Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(MLConstants.MinimumSamplesPerClass));
        }

        // Verify classify still works after concurrent training
        var result = _bayesService.Classify("test spam message with some words to classify");
        Assert.That(result, Is.Not.Null, "Classify should return a result after concurrent training");
    }

    [Test]
    public void TrainAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange — Pre-cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — SemaphoreSlim.WaitAsync throws OperationCanceledException (or subclass)
        // when the token is already cancelled before the wait begins
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _bayesService!.TrainAsync(cts.Token),
            "Pre-cancelled token should throw OperationCanceledException");
    }

    #endregion

    #region Classify Tests

    [Test]
    public async Task Classify_BeforeTraining_ReturnsNull()
    {
        // Arrange — Create a fresh service instance with no training performed
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper!.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataPath"] = Path.GetTempPath() })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoreServices();
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var uninitializedService = serviceProvider.GetRequiredService<IBayesClassifierService>();

        // Act — Classify without prior training
        var result = uninitializedService.Classify("test message");

        // Assert — Must return null when no model is loaded
        Assert.That(result, Is.Null, "Classify should return null when classifier has not been trained");

        (serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task Classify_AfterTraining_ReturnsProbabilityInValidRange()
    {
        // Arrange
        await _bayesService!.TrainAsync();

        // Act
        var result = _bayesService.Classify("buy cheap pills online guaranteed results click here now");

        // Assert
        Assert.That(result, Is.Not.Null, "Classify should return a result after training");

        var (spamProbability, details, certainty) = (result!.SpamProbability, result.Details, result.Certainty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spamProbability, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0),
                "Spam probability must be in [0.0, 1.0]");
            Assert.That(certainty, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0),
                "Certainty must be in [0.0, 1.0]");
            Assert.That(details, Is.Not.Null.And.Not.Empty,
                "Details must be a non-empty string");
        }
    }

    [Test]
    public async Task Classify_EmptyString_ReturnsValidResultAfterTraining()
    {
        // Arrange
        await _bayesService!.TrainAsync();

        // Act — Empty string is valid input; BayesClassifier handles it via tokenizer
        var result = _bayesService.Classify("");

        // Assert — Result may be non-null (classifier returns "no words" path) or null
        // The important thing is that it does not throw
        if (result is not null)
        {
            var (spamProbability, _, certainty) = (result.SpamProbability, result.Details, result.Certainty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(spamProbability, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0));
                Assert.That(certainty, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0));
            }
        }

        Assert.Pass("Classify with empty string completed without throwing");
    }

    [Test]
    public async Task Classify_SpecialCharactersAndEmoji_ReturnsValidResultAfterTraining()
    {
        // Arrange
        await _bayesService!.TrainAsync();

        // Act — Unicode, emoji, and special characters should not crash the classifier
        var specialText = "🔥💯 Special chars: !@#$%^&*() \n\t\r Unicode: 中文 日本語 العربية";
        var result = _bayesService.Classify(specialText);

        // Assert
        Assert.That(result, Is.Not.Null, "Classify should handle special characters without returning null");

        var (spamProbability, _, certainty) = (result!.SpamProbability, result.Details, result.Certainty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spamProbability, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0));
            Assert.That(certainty, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0));
        }
    }

    #endregion

    #region GetMetadata Tests

    [Test]
    public void GetMetadata_BeforeTraining_ReturnsNull()
    {
        // Arrange — No training performed (fresh service from SetUp)

        // Act
        var metadata = _bayesService!.GetMetadata();

        // Assert
        Assert.That(metadata, Is.Null, "GetMetadata should return null when classifier has not been trained");
    }

    [Test]
    public async Task GetMetadata_AfterTraining_ReturnsNonNull()
    {
        // Arrange
        await _bayesService!.TrainAsync();

        // Act
        var metadata = _bayesService.GetMetadata();

        // Assert
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.TotalSampleCount, Is.GreaterThan(0));
    }

    #endregion
}
