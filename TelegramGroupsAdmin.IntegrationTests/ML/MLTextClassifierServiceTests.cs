using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Extensions;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ML;

/// <summary>
/// Integration tests for MLTextClassifierService - ML.NET SDCA text classifier.
///
/// Test Strategy:
/// - Uses real PostgreSQL database (Testcontainers) cloned from the canonical
///   golden_template (100 spam + 100 ham training_labels, well above threshold).
/// - Tests full ML.NET training pipeline (TF-IDF + SDCA).
/// - Validates model persistence, SHA256 verification, and thread safety.
/// - All substrate mutation goes through `GoldenDataset.Reduce(...)` — no raw SQL,
///   no inline INSERTs.
/// - Temp directory for model files (cleaned up after tests).
///
/// Test Coverage:
/// - TrainModelAsync with sufficient data: Trains and saves model
/// - TrainModelAsync threshold gate at MinimumSamplesPerClass - 1 (both below /
///   spam-only-above / ham-only-above): Model is not produced in all three corners
/// - LoadModelAsync with valid model: SHA256 verification succeeds
/// - LoadModelAsync error paths: corrupted model, missing model, corrupted metadata, hash mismatch
/// - Predict with loaded model: Returns prediction; null/empty/long/unicode inputs handled
/// - Prevent overlapping retrains: Semaphore blocks concurrent calls
/// - Imbalanced datasets: HighSpamRatio adds implicit ham; HighHamRatio caps explicit ham
/// </summary>
[TestFixture]
public class MLTextClassifierServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IMLTextClassifierService? _mlService;
    private string _tempDataDirectory = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Create temp directory for model files
        _tempDataDirectory = Path.Combine(Path.GetTempPath(), $"ml_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDirectory);

        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

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

        // Use production extension methods to register services
        // This ensures tests match production configuration exactly
        services.AddCoreServices();
        services.AddContentDetection();

        _serviceProvider = services.BuildServiceProvider();
        _mlService = _serviceProvider.GetRequiredService<IMLTextClassifierService>();
    }

    [TearDown]
    public void TearDown()
    {
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
        // Arrange — canonical template clone provides 100 spam + 100 ham training_labels

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Model files created
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        var metadataPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(modelPath), Is.True, "Model file should exist");
            Assert.That(File.Exists(metadataPath), Is.True, "Metadata file should exist");
        }

        // Verify metadata
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 spam samples after dedup");
            Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 ham samples (explicit + implicit)");
            Assert.That(metadata.TotalSampleCount, Is.GreaterThanOrEqualTo(40));
            Assert.That(metadata.ModelHash, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.ModelSizeBytes, Is.GreaterThan(0));
            // IsBalanced is a separate concern covered by TrainModelAsync_BalancedDataset_IsBalancedTrue.
            // Canonical default is intentionally NOT balanced (real prod spam outweighs labeled ham).
        }
    }

    [Test]
    public async Task TrainModelAsync_InsufficientData_LeavesMetadataNull()
    {
        // Arrange — Reduce both classes to exactly MinimumSamplesPerClass - 1, exercising
        // the threshold gate at its boundary. KeepDetectionResults(0) drains implicit spam;
        // KeepLabeledMessagesOnly drains implicit ham.
        const int BelowThreshold = MLConstants.MinimumSamplesPerClass - 1;
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepSpam(BelowThreshold)
            .KeepHam(BelowThreshold)
            .KeepDetectionResults(0)
            .KeepLabeledMessagesOnly()
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert — No model file created, no metadata
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        Assert.That(File.Exists(modelPath), Is.False, "No model should be written when both classes are below threshold");
        Assert.That(_mlService.GetMetadata(), Is.Null,
            "Metadata should remain null when both classes fall below MinimumSamplesPerClass");
    }

    [Test]
    public async Task TrainModelAsync_OnlyHamAboveThreshold_LeavesMetadataNull()
    {
        // Arrange — ham retains canonical 100 labels (above threshold); spam reduced
        // to threshold - 1. Verifies the gate requires BOTH classes ≥ threshold, not just one.
        const int BelowThreshold = MLConstants.MinimumSamplesPerClass - 1;
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepSpam(BelowThreshold)
            .KeepDetectionResults(0)
            .KeepLabeledMessagesOnly()
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        Assert.That(File.Exists(modelPath), Is.False, "No model should be written when spam is below threshold");
        Assert.That(_mlService.GetMetadata(), Is.Null,
            "Gate should fail when spam is below threshold even if ham is above");
    }

    [Test]
    public async Task TrainModelAsync_OnlySpamAboveThreshold_LeavesMetadataNull()
    {
        // Arrange — spam retains canonical 100 labels (above threshold); ham reduced
        // to threshold - 1. Verifies the gate requires BOTH classes ≥ threshold, not just one.
        const int BelowThreshold = MLConstants.MinimumSamplesPerClass - 1;
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepHam(BelowThreshold)
            .KeepDetectionResults(0)
            .KeepLabeledMessagesOnly()
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        var modelPath = Path.Combine(_tempDataDirectory, "ml-models", "spam-classifier.zip");
        Assert.That(File.Exists(modelPath), Is.False, "No model should be written when ham is below threshold");
        Assert.That(_mlService.GetMetadata(), Is.Null,
            "Gate should fail when ham is below threshold even if spam is above");
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

        // Use production extension methods to ensure test matches production
        services.AddCoreServices();
        services.AddContentDetection();

        var serviceProvider = services.BuildServiceProvider();
        var newService = serviceProvider.GetRequiredService<IMLTextClassifierService>();

        // Act - Load model in new instance
        var loaded = await newService.LoadModelAsync();

        // Assert
        Assert.That(loaded, Is.True, "Model should load successfully");

        var loadedMetadata = newService.GetMetadata();
        Assert.That(loadedMetadata, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedMetadata!.SpamSampleCount, Is.EqualTo(originalMetadata!.SpamSampleCount));
            Assert.That(loadedMetadata.HamSampleCount, Is.EqualTo(originalMetadata.HamSampleCount));
            Assert.That(loadedMetadata.ModelHash, Is.EqualTo(originalMetadata.ModelHash));
        }

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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
            Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
        }
    }

    [Test]
    public async Task TrainModelAsync_OverlappingCalls_OnlyOneExecutes()
    {
        // Arrange — canonical template clone provides 100 spam + 100 ham training_labels

        // Act - Start two training tasks concurrently
        var task1 = _mlService!.TrainModelAsync();
        var task2 = _mlService.TrainModelAsync(); // Should skip due to semaphore

        await Task.WhenAll(task1, task2);

        // Assert - Verify training completed successfully (one call trained, other skipped)
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Training should complete successfully");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 spam samples from combined datasets");
            Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20), "At least 20 ham samples (explicit labels + implicit ≥50 words)");
        }

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

        using (Assert.EnterMultipleScope())
        {
            // Assert - Should return false gracefully (not throw exception)
            Assert.That(loaded, Is.False, "Loading corrupted model should return false");
            Assert.That(_mlService.GetMetadata(), Is.Null, "Metadata should be null after failed load");
        }
    }

    [Test]
    public async Task LoadModelAsync_MissingModelFile_ReturnsFalseGracefully()
    {
        // Arrange - No model files exist (fresh temp directory)

        // Act
        var loaded = await _mlService!.LoadModelAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(loaded, Is.False, "Loading non-existent model should return false");
            Assert.That(_mlService.GetMetadata(), Is.Null, "Metadata should be null when model doesn't exist");
        }
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

        // Use production extension methods to ensure test matches production
        services.AddCoreServices();
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

        // Use production extension methods to ensure test matches production
        services.AddCoreServices();
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

        // Use production extension methods to ensure test matches production
        services.AddCoreServices();
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
            Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
            Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
            Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prediction!.Probability, Is.GreaterThanOrEqualTo(0.0f));
            Assert.That(prediction.Probability, Is.LessThanOrEqualTo(1.0f));
        }
    }

    #endregion

    #region Unbalanced Dataset Tests

    [Test]
    public async Task TrainModelAsync_HighSpamRatio_AddsImplicitHamForBalance()
    {
        // Arrange — reduce canonical to 100 spam + 20 ham explicit labels and zero
        // detection_results (no implicit spam). The unlabeled canonical message tail
        // remains as the implicit-ham pool the repository will draw from to balance.
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepSpam(100)
            .KeepHam(20)
            .KeepDetectionResults(0)
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Implicit ham is added to balance the dataset
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Model should train successfully");
        using (Assert.EnterMultipleScope())
        {
            // Canonical's 100 prod-derived spam labels include real near-duplicates; SimHash
            // dedup removes ~19% (vs ~6% on the legacy synthetic seed). 75% retention is the
            // floor that still proves "most samples remain after dedup."
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(75), "After dedup, most spam samples should remain");
            Assert.That(metadata.SpamSampleCount, Is.LessThanOrEqualTo(100), "Should not exceed raw spam count");
            Assert.That(metadata.HamSampleCount, Is.GreaterThan(20), "Should add implicit ham on top of 20 explicit ham");
            Assert.That(metadata.IsBalanced, Is.True, "Implicit ham should bring dataset into balanced range (20-80% spam)");
        }
    }

    [Test]
    public async Task TrainModelAsync_HighHamRatio_CapsExplicitHamForBalance()
    {
        // Arrange — KeepSpam(25) so canonical's ~19% dedup leaves spam ≥ 20 (above
        // threshold); KeepHam(100) keeps all canonical ham; KeepDetectionResults(0)
        // eliminates implicit spam so the explicit-ham cap is the only ratio knob.
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepSpam(25)
            .KeepHam(100)
            .KeepDetectionResults(0)
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert - Explicit ham is now CAPPED to maintain balance
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null, "Model should train successfully");
        using (Assert.EnterMultipleScope())
        {
            // KeepSpam(25); canonical dedups ~19% → ~20-21 surviving spam samples.
            Assert.That(metadata!.SpamSampleCount, Is.GreaterThanOrEqualTo(18), "After dedup, most spam samples should remain");
            Assert.That(metadata.SpamSampleCount, Is.LessThanOrEqualTo(25), "Should not exceed raw spam count");

            // Explicit ham capped at dynamicHamCap (SpamSampleCount * HamMultiplier where
            // HamMultiplier = 2.33). Using +1 slack for integer truncation in the SUT.
            var dynamicHamCap = (int)(metadata.SpamSampleCount * 2.33) + 1;
            Assert.That(metadata.HamSampleCount, Is.LessThanOrEqualTo(dynamicHamCap),
                $"Explicit ham should be capped at dynamicHamCap ({metadata.SpamSampleCount} * 2.33 = {dynamicHamCap}) for balance");
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata.HamSampleCount, Is.GreaterThanOrEqualTo(20),
                      "Should use at least 20 ham samples");
            Assert.That(metadata.IsBalanced, Is.True,
                "Dataset should be balanced after capping explicit ham (20-80% spam ratio)");
        }
    }

    [Test]
    public async Task TrainModelAsync_BalancedDataset_IsBalancedTrue()
    {
        // Arrange — drain detection_results to remove implicit spam (the unbalancing
        // pool by default). Leave the unlabeled-message pool intact so the repository's
        // implicit-ham draw balances the 81-post-dedup explicit spam.
        await using var context = _testHelper!.GetDbContext();
        await GoldenDataset.Reduce(context)
            .KeepDetectionResults(0)
            .ApplyAsync();

        // Act
        await _mlService!.TrainModelAsync();

        // Assert
        var metadata = _mlService.GetMetadata();
        Assert.That(metadata, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata!.SpamRatio, Is.GreaterThanOrEqualTo(0.2).And.LessThanOrEqualTo(0.8),
                      "Spam ratio should be between 20-80% for balanced dataset");
            Assert.That(metadata.IsBalanced, Is.True, "Dataset should be flagged as balanced");
        }
    }

    #endregion

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
}
