using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.Security.Cryptography;
using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// ML.NET SDCA text classifier for spam detection.
/// Thread-safe Singleton service with immutable container pattern for atomic model swapping.
/// </summary>
public class MLTextClassifierService : IMLTextClassifierService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MLTextClassifierService> _logger;
    private readonly string _dataDirectory;

    // Thread-safe retraining semaphore (prevents overlapping retrains)
    private readonly SemaphoreSlim _retrainingSemaphore = new(1, 1);

    // Immutable container for atomic model swapping (replaces multi-field volatile race condition)
    private volatile ModelContainer? _currentModel;

    /// <summary>
    /// Immutable container for thread-safe atomic model swapping.
    /// All three fields swap together via Interlocked.Exchange.
    /// </summary>
    private sealed record ModelContainer(
        ITransformer Model,
        PredictionEngine<SpamTextFeatures, SpamPrediction> PredictionEngine,
        SpamClassifierMetadata Metadata);

    public MLTextClassifierService(
        IServiceProvider serviceProvider,
        ILogger<MLTextClassifierService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dataDirectory = configuration["App:DataPath"] ?? "/data";
    }

    /// <summary>
    /// Model directory path: {DataDirectory}/ml-models/
    /// </summary>
    private string ModelDirectory => Path.Combine(_dataDirectory, "ml-models");

    /// <summary>
    /// Model file path: {DataDirectory}/ml-models/spam-classifier.zip
    /// </summary>
    private string ModelPath => Path.Combine(ModelDirectory, "spam-classifier.zip");

    /// <summary>
    /// Metadata file path: {DataDirectory}/ml-models/spam-classifier.json
    /// </summary>
    private string MetadataPath => Path.Combine(ModelDirectory, "spam-classifier.json");

    /// <summary>
    /// Trains the SDCA model with TF-IDF features.
    /// Uses SemaphoreSlim to prevent overlapping retraining.
    /// </summary>
    public async Task TrainModelAsync(CancellationToken cancellationToken = default)
    {
        // Prevent overlapping retrains
        if (!await _retrainingSemaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Training already in progress, skipping this trigger");
            return;
        }

        try
        {
            _logger.LogInformation("Starting SDCA model training...");
            var startTime = DateTimeOffset.UtcNow;

            // Create scope to resolve Scoped repository from Singleton service
            using var scope = _serviceProvider.CreateScope();
            var trainingDataRepository = scope.ServiceProvider.GetRequiredService<IMLTrainingDataRepository>();

            // Load labeled message IDs once (reused by both spam and ham queries to avoid duplication)
            var labeledMessageIds = await trainingDataRepository.GetLabeledMessageIdsAsync(cancellationToken);

            // Load training samples from repository (encapsulates multi-table queries)
            var spamSamples = await trainingDataRepository.GetSpamSamplesAsync(labeledMessageIds, cancellationToken);
            var hamSamples = await trainingDataRepository.GetHamSamplesAsync(spamSamples.Count, labeledMessageIds, cancellationToken);

            if (spamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass || hamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass)
            {
                _logger.LogWarning(
                    "Insufficient training data (spam: {Spam}, ham: {Ham}, minimum: {Min})",
                    spamSamples.Count, hamSamples.Count, SpamClassifierMetadata.MinimumSamplesPerClass);
                return;
            }

            // Build training data from domain models
            var trainingData = spamSamples
                .Select(sample => new SpamTextFeatures { MessageText = sample.Text, IsSpam = true })
                .Concat(hamSamples.Select(sample => new SpamTextFeatures { MessageText = sample.Text, IsSpam = false }))
                .ToList();

            // Create ML.NET context and load data
            var mlContext = new MLContext(seed: MLConstants.MlNetSeed);
            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            // Build pipeline: TF-IDF → SDCA Logistic Regression
            var pipeline = mlContext.Transforms.Text
                .FeaturizeText("Features", nameof(SpamTextFeatures.MessageText))  // TF-IDF
                .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: "Label",
                    featureColumnName: "Features"));

            // Train the model
            var model = pipeline.Fit(dataView);

            var trainingDuration = DateTimeOffset.UtcNow - startTime;
            _logger.LogInformation("Model training completed in {Duration}ms", trainingDuration.TotalMilliseconds);

            // Create metadata
            var metadata = new SpamClassifierMetadata
            {
                TrainedAt = DateTimeOffset.UtcNow,
                SpamSampleCount = spamSamples.Count,
                HamSampleCount = hamSamples.Count,
                MLNetVersion = typeof(MLContext).Assembly.GetName().Version?.ToString() ?? "unknown"
            };

            // Log balance warning if data is imbalanced (but continue training)
            if (!metadata.IsBalanced)
            {
                _logger.LogWarning(
                    "Training with imbalanced data: {Spam} spam + {Ham} ham = {SpamRatio:P1} spam ratio " +
                    "(recommended: 20-80%). Model accuracy may be reduced. " +
                    "Ideal balance: Add {SpamNeeded} spam labels OR remove {HamExcess} ham labels.",
                    metadata.SpamSampleCount, metadata.HamSampleCount, metadata.SpamRatio,
                    CalculateSpamNeeded(metadata), CalculateHamExcess(metadata));
            }

            // Save model and metadata
            await SaveModelAsync(model, metadata, cancellationToken);

            // Create new container and atomically swap (thread-safe)
            var predictionEngine = mlContext.Model.CreatePredictionEngine<SpamTextFeatures, SpamPrediction>(model);
            var newContainer = new ModelContainer(model, predictionEngine, metadata);
            var oldContainer = Interlocked.Exchange(ref _currentModel, newContainer);

            // Dispose old prediction engine if it exists
            oldContainer?.PredictionEngine.Dispose();

            _logger.LogInformation(
                "Model deployed: {Spam} spam + {Ham} ham = {Total} samples (spam ratio: {Ratio:P1}, balanced: {Balanced})",
                metadata.SpamSampleCount, metadata.HamSampleCount, metadata.TotalSampleCount,
                metadata.SpamRatio, metadata.IsBalanced);
        }
        finally
        {
            _retrainingSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves model and metadata to disk with SHA256 hash verification.
    /// </summary>
    private async Task SaveModelAsync(ITransformer model, SpamClassifierMetadata metadata, CancellationToken cancellationToken)
    {
        // Ensure directory exists (should be created by Program.cs, but double-check)
        Directory.CreateDirectory(ModelDirectory);

        // Save model to disk
        var mlContext = new MLContext();
        mlContext.Model.Save(model, null, ModelPath);

        // Compute SHA256 hash
        await using (var stream = File.OpenRead(ModelPath))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            metadata.ModelHash = Convert.ToHexString(hash);
        }

        // Get model file size
        var fileInfo = new FileInfo(ModelPath);
        metadata.ModelSizeBytes = fileInfo.Length;

        // Save metadata as JSON
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(MetadataPath, json, cancellationToken);

        _logger.LogInformation(
            "Model saved: {Size} bytes, SHA256: {Hash}",
            metadata.ModelSizeBytes, metadata.ModelHash);
    }

    /// <summary>
    /// Loads model and metadata from disk with SHA256 hash verification.
    /// Returns true if successful, false if model doesn't exist or verification fails.
    /// </summary>
    public async Task<bool> LoadModelAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ModelPath) || !File.Exists(MetadataPath))
        {
            _logger.LogInformation("No persisted model found at {Path}", ModelPath);
            return false;
        }

        try
        {
            // Load metadata
            var json = await File.ReadAllTextAsync(MetadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<SpamClassifierMetadata>(json);

            if (metadata == null)
            {
                _logger.LogWarning("Failed to deserialize metadata from {Path}", MetadataPath);
                return false;
            }

            // Verify SHA256 hash
            await using (var stream = File.OpenRead(ModelPath))
            {
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                var computedHash = Convert.ToHexString(hash);

                if (computedHash != metadata.ModelHash)
                {
                    _logger.LogWarning(
                        "Model hash mismatch (expected: {Expected}, got: {Actual}), deleting corrupted files",
                        metadata.ModelHash, computedHash);

                    // Delete corrupted files so next training attempt can succeed
                    File.Delete(ModelPath);
                    File.Delete(MetadataPath);
                    return false;
                }
            }

            // Load model
            var mlContext = new MLContext();
            var model = mlContext.Model.Load(ModelPath, out var _);

            // Create new container and atomically swap (thread-safe)
            var predictionEngine = mlContext.Model.CreatePredictionEngine<SpamTextFeatures, SpamPrediction>(model);
            var newContainer = new ModelContainer(model, predictionEngine, metadata);
            var oldContainer = Interlocked.Exchange(ref _currentModel, newContainer);

            // Dispose old prediction engine if it exists
            oldContainer?.PredictionEngine.Dispose();

            _logger.LogInformation(
                "Model loaded successfully: trained {TrainedAt}, {Spam} spam + {Ham} ham samples",
                metadata.TrainedAt, metadata.SpamSampleCount, metadata.HamSampleCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model from {Path}", ModelPath);
            return false;
        }
    }

    /// <summary>
    /// Predicts spam probability for a message.
    /// Thread-safe: uses volatile container for atomic reads.
    /// </summary>
    public SpamPrediction? Predict(string messageText)
    {
        var container = _currentModel;
        if (container == null)
        {
            _logger.LogWarning("Prediction engine not loaded - model needs training");
            return null;
        }

        var features = new SpamTextFeatures { MessageText = messageText };
        return container.PredictionEngine.Predict(features);
    }

    /// <summary>
    /// Gets current model metadata (training stats, timestamp, hash).
    /// </summary>
    public SpamClassifierMetadata? GetMetadata() => _currentModel?.Metadata;

    private static int CalculateSpamNeeded(SpamClassifierMetadata metadata)
    {
        // If spam ratio < 20%, calculate spam needed to reach 30% target
        if (metadata.SpamRatio < SpamClassifierMetadata.MinBalancedSpamRatio)
        {
            var targetTotal = (int)(metadata.HamSampleCount / (1 - SpamClassifierMetadata.TargetSpamRatio));
            return Math.Max(0, (int)(targetTotal * SpamClassifierMetadata.TargetSpamRatio) - metadata.SpamSampleCount);
        }
        return 0;
    }

    private static int CalculateHamExcess(SpamClassifierMetadata metadata)
    {
        // If spam ratio > 80%, calculate ham excess beyond balanced ratio
        if (metadata.SpamRatio > SpamClassifierMetadata.MaxBalancedSpamRatio)
        {
            var maxHamForBalance = (int)(metadata.SpamSampleCount * (1 - SpamClassifierMetadata.TargetSpamRatio) / SpamClassifierMetadata.TargetSpamRatio);
            return Math.Max(0, metadata.HamSampleCount - maxHamForBalance);
        }
        return 0;
    }

    public void Dispose()
    {
        _retrainingSemaphore.Dispose();
        _currentModel?.PredictionEngine.Dispose();
    }
}
