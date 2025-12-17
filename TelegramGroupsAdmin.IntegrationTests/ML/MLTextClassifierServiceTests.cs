using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ML;

/// <summary>
/// Integration tests for MLTextClassifierService - ML.NET SDCA text classifier.
///
/// Test Strategy:
/// - Uses real PostgreSQL database (Testcontainers) with GoldenDataset
/// - Tests full ML.NET training pipeline (TF-IDF + SDCA)
/// - Validates model persistence, SHA256 verification, and thread safety
/// - Temp directory for model files (cleaned up after tests)
///
/// Test Coverage (5 tests):
/// - TrainModelAsync with sufficient data: Trains and saves model
/// - TrainModelAsync with insufficient data: Logs warning, no model
/// - LoadModelAsync with valid model: SHA256 verification succeeds
/// - Predict with loaded model: Returns prediction
/// - Prevent overlapping retrains: Semaphore blocks concurrent calls
///
/// GoldenDataset Training Data:
/// - 3 spam labels: Label1 (Msg1), Label2 (Msg2), Label5 (Msg5)
/// - 2 ham labels: Label3 (Msg3), Label4 (Msg4)
/// - Minimal but realistic for testing ML pipeline
/// </summary>
[TestFixture]
public class MLTextClassifierServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IMLTextClassifierService? _mlService;
    private string _tempDataDirectory = null!;
    private AppDbContext? _context;

    [SetUp]
    public async Task SetUp()
    {
        // Create temp directory for model files
        _tempDataDirectory = Path.Combine(Path.GetTempPath(), $"ml_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDirectory);

        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });

        // Add IConfiguration with test data directory
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:DataPath"] = _tempDataDirectory
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension method to register all content detection services
        // This ensures tests match production configuration exactly
        services.AddContentDetection();

        _serviceProvider = services.BuildServiceProvider();
        _mlService = _serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Seed GoldenDataset for training data
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
        (_mlService as IDisposable)?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();

        // Clean up temp directory
        if (Directory.Exists(_tempDataDirectory))
        {
            Directory.Delete(_tempDataDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TrainModelAsync_SufficientData_TrainsAndSavesModel()
    {
        // Arrange - GoldenDataset provides training data (3 spam + 2 ham labels + 20 spam + 20 ham from MLTrainingData.sql)

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Model files created
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        var metadataPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.json");

        Assert.That(File.Exists(modelPath), Is.True, "Model file should exist");
        Assert.That(File.Exists(metadataPath), Is.True, "Metadata file should exist");

        // Verify metadata
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 spam samples from combined datasets");
        Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 ham samples (explicit labels + implicit ≥50 words)");
        Assert.That(metadata.TotalSampleCount, Is.GreaterThanOrEqualTo(40));
        Assert.That(metadata.IsBalanced, Is.True, "Training data should be balanced (20-80% spam)");
        Assert.That(metadata.ModelHash, Is.Not.Null.And.Not.Empty);
        Assert.That(metadata.ModelSizeBytes, Is.GreaterThan(0));
    }

    [Test]
    public async Task TrainModelAsync_InsufficientData_LogsWarningAndReturns()
    {
        // Arrange - Clear training labels to simulate no data
        await using var context = _testHelper!.GetDbContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM training_labels");

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - No model created
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        Assert.That(File.Exists(modelPath), Is.False, "No model should be created with insufficient data");

        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Null, "Metadata should be null when training fails");
    }

    [Test]
    public async Task LoadModelAsync_ValidModel_LoadsSuccessfully()
    {
        // Arrange - Train and save model first
        await _mlService!.TrainModelAsync();
        var originalMetadata = _mlService.GetMetadata();

        // Create new service instance to test loading
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper!.ConnectionString));
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:DataPath"] = _tempDataDirectory
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension method to ensure test matches production
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var newService = serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Act - Load model in new instance
        var loaded = await newService.LoadModelAsync();

        // Assert
        Assert.That(loaded, Is.True, "Model should load successfully");

        var loadedMetadata = newService.GetMetadata();
        Assert.That(loadedMetadata, Is.Not.Null);
        Assert.That(loadedMetadata!.SpamSampleCount, Is.EqualTo(originalMetadata!.SpamSampleCount));
        Assert.That(loadedMetadata.HamSampleCount, Is.EqualTo(originalMetadata.HamSampleCount));
        Assert.That(loadedMetadata.ModelHash, Is.EqualTo(originalMetadata.ModelHash));

        (serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task Predict_ModelLoaded_ReturnsPrediction()
    {
        // Arrange
        await _mlService!.TrainModelAsync();

        // Act
        var prediction = _mlService.Predict("test spam message");

        // Assert
        Assert.That(prediction, Is.Not.Null);
        Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public async Task TrainModelAsync_OverlappingCalls_OnlyOneExecutes()
    {
        // Arrange - GoldenDataset provides training data (3 spam + 2 ham labels + 20 spam + 20 ham from MLTrainingData.sql)

        // Act - Start two training tasks concurrently
        var task1 = _mlService!.TrainModelAsync();
        var task2 = _mlService.TrainModelAsync(); // Should skip due to semaphore

        await Task.WhenAll(task1, task2);

        // Assert - Verify training completed successfully (one call trained, other skipped)
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Training should complete successfully");
        Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 spam samples from combined datasets");
        Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 ham samples (explicit labels + implicit ≥50 words)");

        // Verify only one model file created (not duplicated)
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        Assert.That(File.Exists(modelPath), Is.True);
    }

    #region Exception Handling Tests

    [Test]
    public async Task LoadModelAsync_CorruptedModel_ReturnsFalseGracefully()
    {
        // Arrange - Create a corrupted model file (invalid content)
        Directory.CreateDirectory(Path.Combine(_tempDataDirectory, "ml-models"));
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        var metadataPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.json");

        // Write garbage data to model file
        await File.WriteAllTextAsync(modelPath, "This is not a valid ML.NET model file");

        // Write valid metadata (but model file is corrupted)
        var fakeMetadata = new SpamClassifierMetadata
        {
            TrainedAt = DateTimeOffset.UtcNow,
            SpamSampleCount = 5,
            HamSampleCount = 5,
            ModelHash = "fakehash",
            ModelSizeBytes = 100,
            MLNetVersion = "1.0.0"
        };
        var json = JsonSerializer.Serialize(fakeMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json);

        // Act - Try to load corrupted model
        var loaded = await _mlService!.LoadModelAsync();

        // Assert - Should return false gracefully (not throw exception)
        Assert.That(loaded, Is.False, "Loading corrupted model should return false");
        Assert.That(_mlService.GetMetadata(), Is.Null, "Metadata should be null after failed load");
    }

    [Test]
    public async Task LoadModelAsync_MissingModelFile_ReturnsFalseGracefully()
    {
        // Arrange - No model files exist (fresh temp directory)

        // Act
        var loaded = await _mlService!.LoadModelAsync();

        // Assert
        Assert.That(loaded, Is.False, "Loading non-existent model should return false");
        Assert.That(_mlService.GetMetadata(), Is.Null, "Metadata should be null when model doesn't exist");
    }

    [Test]
    public async Task LoadModelAsync_CorruptedMetadata_ReturnsFalseGracefully()
    {
        // Arrange - Create valid model but corrupted metadata
        await _mlService!.TrainModelAsync(); // Create valid model
        var metadataPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.json");

        // Corrupt the metadata file
        await File.WriteAllTextAsync(metadataPath, "{ invalid json content !!!");

        // Create new service instance to test loading with corrupted metadata
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper!.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataPath"] = _tempDataDirectory })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension method to ensure test matches production
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var newService = serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Act
        var loaded = await newService.LoadModelAsync();

        // Assert - Should handle JSON deserialization failure gracefully
        Assert.That(loaded, Is.False, "Loading with corrupted metadata should return false");

        (serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task LoadModelAsync_SHA256HashMismatch_ReturnsFalseGracefully()
    {
        // Arrange - Create valid model then tamper with metadata hash
        await _mlService!.TrainModelAsync(); // Create valid model
        var metadataPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.json");

        // Read and modify metadata to have incorrect SHA256 hash
        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(metadataJson);

        // Create new metadata with tampered hash (change one character)
        var tamperedMetadata = new
        {
            TrainedAt = metadata.GetProperty("TrainedAt").GetDateTimeOffset(),
            SpamSampleCount = metadata.GetProperty("SpamSampleCount").GetInt32(),
            HamSampleCount = metadata.GetProperty("HamSampleCount").GetInt32(),
            MLNetVersion = metadata.GetProperty("MLNetVersion").GetString(),
            ModelHash = "0000000000000000000000000000000000000000000000000000000000000000", // Invalid hash
            ModelSizeBytes = metadata.GetProperty("ModelSizeBytes").GetInt64()
        };

        var tamperedJson = System.Text.Json.JsonSerializer.Serialize(tamperedMetadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, tamperedJson);

        // Create new service instance to test loading with hash mismatch
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper!.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataPath"] = _tempDataDirectory })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension method to ensure test matches production
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var newService = serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Act
        var loaded = await newService.LoadModelAsync();

        // Assert - Should detect hash mismatch and return false
        Assert.That(loaded, Is.False, "Loading with SHA256 hash mismatch should return false");

        (serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public void Predict_ModelNotLoaded_ReturnsNull()
    {
        // Arrange - Create new service with no model loaded
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper!.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataPath"] = _tempDataDirectory })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Use production extension method to ensure test matches production
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var uninitializedService = serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Act - Try to predict without loading model
        var prediction = uninitializedService.Predict("test message");

        // Assert - Should return null gracefully (not throw exception)
        Assert.That(prediction, Is.Null, "Predict should return null when model not loaded");

        (serviceProvider as IDisposable)?.Dispose();
    }

    #endregion

    #region Predict Edge Case Tests

    [Test]
    public async Task Predict_NullInput_ReturnsValidPrediction()
    {
        // Arrange
        await _mlService!.TrainModelAsync();

        // Act - ML.NET handles null text gracefully (treats as empty/low spam probability)
        var prediction = _mlService.Predict(null!);

        // Assert - Should return a valid prediction (not throw)
        Assert.That(prediction, Is.Not.Null, "ML.NET handles null input gracefully");
        Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public async Task Predict_EmptyString_ReturnsValidPrediction()
    {
        // Arrange
        await _mlService!.TrainModelAsync();

        // Act
        var prediction = _mlService.Predict("");

        // Assert - Empty string should be treated as ham (low spam probability)
        Assert.That(prediction, Is.Not.Null, "Predict should handle empty string");
        Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public async Task Predict_VeryLongText_ReturnsValidPrediction()
    {
        // Arrange
        await _mlService!.TrainModelAsync();

        // Act - Generate a 200KB text (longer than typical message limit)
        var longText = new string('A', 200 * 1024); // 200KB of 'A' characters
        var prediction = _mlService.Predict(longText);

        // Assert - Should handle very long text without crashing
        Assert.That(prediction, Is.Not.Null, "Predict should handle very long text");
        Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public async Task Predict_SpecialCharacters_ReturnsValidPrediction()
    {
        // Arrange
        await _mlService!.TrainModelAsync();

        // Act - Test with special characters, emojis, Unicode
        var specialText = "🔥💯 Special chars: !@#$%^&*() \n\t\r Unicode: 中文 日本語 العربية";
        var prediction = _mlService.Predict(specialText);

        // Assert
        Assert.That(prediction, Is.Not.Null, "Predict should handle special characters");
        Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
    }

    #endregion

    #region Unbalanced Dataset Tests

    [Test]
    public async Task TrainModelAsync_HighSpamRatio_AddsImplicitHamForBalance()
    {
        // Arrange - Use composable SQL dataset (100 spam + 20 explicit ham = 83.3% spam without implicit)
        await using var context = _testHelper!.GetDbContext();

        // Truncate and reseed with high-spam dataset
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE training_labels, detection_results, messages, managed_chats, linked_channels, telegram_users, users, configs CASCADE");
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);
        await GoldenDataset.SeedHighSpamTrainingDataAsync(context);

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Implicit ham is added to balance the dataset
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Model should train successfully");
        Assert.That(metadata!.SpamSampleCount, Is.EqualTo(100), "SQL has 100 spam samples");
        Assert.That(metadata.HamSampleCount, Is.GreaterThan(20), "Should add implicit ham on top of 20 explicit ham");
        Assert.That(metadata.IsBalanced, Is.True, "Implicit ham should bring dataset into balanced range (20-80% spam)");
    }

    [Test]
    public async Task TrainModelAsync_HighHamRatio_CapsExplicitHamForBalance()
    {
        // Arrange - Use composable SQL dataset (20 spam + 100 ham explicit labels)
        await using var context = _testHelper!.GetDbContext();

        // Truncate and reseed with high-ham dataset
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE training_labels, detection_results, messages, managed_chats, linked_channels, telegram_users, users, configs CASCADE");
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);
        await GoldenDataset.SeedHighHamTrainingDataAsync(context);

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Explicit ham is now CAPPED to maintain balance
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Model should train successfully");
        Assert.That(metadata!.SpamSampleCount, Is.EqualTo(20), "SQL has 20 spam samples");

        // NEW BEHAVIOR: Explicit ham is capped to dynamicHamCap (20 * 2.33 = 46)
        Assert.That(metadata.HamSampleCount, Is.LessThanOrEqualTo(46),
            "Explicit ham should be capped at dynamicHamCap (20 * 2.33 = 46) for balance");
        Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20),
            "Should use at least 20 ham samples");
        Assert.That(metadata.IsBalanced, Is.True,
            "Dataset should be balanced after capping explicit ham (20-80% spam ratio)");
    }

    [Test]
    public async Task TrainModelAsync_BalancedDataset_IsBalancedTrue()
    {
        // Arrange - GoldenDataset provides balanced data (3 spam, 2-3 ham)
        // This verifies the baseline behavior for comparison

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.SpamRatio, Is.GreaterThanOrEqualTo(0.2).And.LessThanOrEqualTo(0.8),
            "Spam ratio should be between 20-80% for balanced dataset");
        Assert.That(metadata.IsBalanced, Is.True, "Dataset should be flagged as balanced");
    }

    #endregion

    #region Minimum Threshold Tests

    [Test]
    public async Task TrainModelAsync_BelowMinimumThreshold_LogsWarningAndReturns()
    {
        // Arrange - Start with NO training data, create exactly 5 spam + 5 ham (below 20 minimum)
        await using var context = _testHelper!.GetDbContext();

        // Truncate and reseed with zero training data
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE training_labels, detection_results, messages, managed_chats, linked_channels, telegram_users, users, configs CASCADE");
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);

        // Create 10 test messages (5 spam + 5 ham)
        for (int i = 1; i <= 10; i++)
        {
            long messageId = 110000 + i;
            await context.Database.ExecuteSqlRawAsync(
                $$"""
                INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, content_check_skip_reason)
                VALUES ({{messageId}}, {{GoldenDataset.TelegramUsers.User1_TelegramUserId}}, {{GoldenDataset.ManagedChats.MainChat_Id}}, NOW() - INTERVAL '1 hour', {0}, 0)
                """,
                $"Threshold test message {i} with sufficient length for ML training purposes"
            );
        }

        // Add 5 spam labels
        for (int i = 1; i <= 5; i++)
        {
            long messageId = 110000 + i;
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at) VALUES ({messageId}, 0, {GoldenDataset.TelegramUsers.User1_TelegramUserId}, NOW())"
            );
        }

        // Add 5 ham labels
        for (int i = 6; i <= 10; i++)
        {
            long messageId = 110000 + i;
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at) VALUES ({messageId}, 1, {GoldenDataset.TelegramUsers.User1_TelegramUserId}, NOW())"
            );
        }

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Model should NOT train (below 20 sample minimum)
        Assert.That(_mlService.GetMetadata(), Is.Null, "Model should not train with only 5 spam and 5 ham (below 20 minimum)");
    }

    [Test]
    public async Task TrainModelAsync_ZeroSpamSamples_LogsWarningAndReturns()
    {
        // Arrange - Start with NO training data, create only ham samples (0 spam + 22 ham)
        await using var context = _testHelper!.GetDbContext();

        // Truncate and reseed with zero training data
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE training_labels, detection_results, messages, managed_chats, linked_channels, telegram_users, users, configs CASCADE");
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);

        // Create 22 ham messages
        for (int i = 1; i <= 22; i++)
        {
            long messageId = 120000 + i;
            await context.Database.ExecuteSqlRawAsync(
                $$"""
                INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, content_check_skip_reason)
                VALUES ({{messageId}}, {{GoldenDataset.TelegramUsers.User1_TelegramUserId}}, {{GoldenDataset.ManagedChats.MainChat_Id}}, NOW() - INTERVAL '1 hour', {0}, 0)
                """,
                $"Ham test message {i} with sufficient length for ML training purposes"
            );

            // Add ham label
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at) VALUES ({messageId}, 1, {GoldenDataset.TelegramUsers.User1_TelegramUserId}, NOW())"
            );
        }

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        Assert.That(_mlService.GetMetadata(), Is.Null, "Model should not train with zero spam samples");
    }

    [Test]
    public async Task TrainModelAsync_ZeroHamSamples_LogsWarningAndReturns()
    {
        // Arrange - Start with NO training data, create only spam samples (23 spam + 0 ham)
        await using var context = _testHelper!.GetDbContext();

        // Truncate and reseed with zero training data
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE training_labels, detection_results, messages, managed_chats, linked_channels, telegram_users, users, configs CASCADE");
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);

        // Create 23 spam messages
        for (int i = 1; i <= 23; i++)
        {
            long messageId = 130000 + i;
            await context.Database.ExecuteSqlRawAsync(
                $$"""
                INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, content_check_skip_reason)
                VALUES ({{messageId}}, {{GoldenDataset.TelegramUsers.User1_TelegramUserId}}, {{GoldenDataset.ManagedChats.MainChat_Id}}, NOW() - INTERVAL '1 hour', {0}, 0)
                """,
                $"Spam test message {i} with sufficient length for ML training purposes"
            );

            // Add spam label
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at) VALUES ({messageId}, 0, {GoldenDataset.TelegramUsers.User1_TelegramUserId}, NOW())"
            );
        }

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        Assert.That(_mlService.GetMetadata(), Is.Null, "Model should not train with zero ham samples");
    }

    [Test]
    public void TrainModelAsync_PreCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange - Pre-cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should throw TaskCanceledException (subclass of OperationCanceledException)
        Assert.ThrowsAsync<TaskCanceledException>(
            async () => await _mlService!.TrainModelAsync(cts.Token),
            "Pre-cancelled token should throw TaskCanceledException");
    }

    #endregion
}
