using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.ML;
using System.Security.Cryptography;
using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// ML.NET SDCA text classifier for spam detection.
/// Thread-safe Singleton service with immutable container pattern for atomic model swapping.
/// Uses ObjectPool&lt;PredictionEngine&gt; for thread-safe concurrent predictions.
/// </summary>
public class MLTextClassifierService : IMLTextClassifierService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MLTextClassifierService> _logger;
    private readonly string _dataDirectory;

    // Shared MLContext for training and saving (protected by _retrainingSemaphore)
    private readonly MLContext _mlContext = new(seed: MLConstants.MlNetSeed);

    // Thread-safe retraining semaphore (prevents overlapping retrains)
    private readonly SemaphoreSlim _retrainingSemaphore = new(1, 1);

    // Immutable container for atomic model swapping (replaces multi-field volatile race condition)
    private volatile ModelContainer? _currentModel;

    /// <summary>
    /// Immutable container for thread-safe atomic model swapping.
    /// Uses ObjectPool for thread-safe PredictionEngine access (PredictionEngine is NOT thread-safe).
    /// </summary>
    private sealed record ModelContainer(
        ITransformer Model,
        ObjectPool<PredictionEngine<SpamTextFeatures, SpamPrediction>> EnginePool,
        SpamClassifierMetadata Metadata);

    /// <summary>
    /// ObjectPool policy that creates PredictionEngine instances from a given MLContext + ITransformer.
    /// </summary>
    private sealed class PredictionEnginePoolPolicy(MLContext mlContext, ITransformer model)
        : PooledObjectPolicy<PredictionEngine<SpamTextFeatures, SpamPrediction>>
    {
        public override PredictionEngine<SpamTextFeatures, SpamPrediction> Create()
            => mlContext.Model.CreatePredictionEngine<SpamTextFeatures, SpamPrediction>(model);

        public override bool Return(PredictionEngine<SpamTextFeatures, SpamPrediction> obj) => true;
    }

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

            // Load training samples from repository (encapsulates multi-table queries)
            var spamSamples = await trainingDataRepository.GetSpamSamplesAsync(cancellationToken);
            var hamSamples = await trainingDataRepository.GetHamSamplesAsync(spamSamples.Count, cancellationToken);

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

            // Load data using shared MLContext (protected by _retrainingSemaphore)
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            // Build pipeline: TF-IDF → SDCA Logistic Regression
            var pipeline = _mlContext.Transforms.Text
                .FeaturizeText("Features", nameof(SpamTextFeatures.MessageText))  // TF-IDF
                .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
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

            // Create thread-safe prediction engine pool and atomically swap container
            var enginePool = new DefaultObjectPool<PredictionEngine<SpamTextFeatures, SpamPrediction>>(
                new PredictionEnginePoolPolicy(_mlContext, model));
            var newContainer = new ModelContainer(model, enginePool, metadata);
            var oldContainer = Interlocked.Exchange(ref _currentModel, newContainer);

            // Dispose old transformer if it supports IDisposable (ITransformer does not extend IDisposable)
            (oldContainer?.Model as IDisposable)?.Dispose();

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

        // Save model to disk (uses shared _mlContext, always called under _retrainingSemaphore)
        _mlContext.Model.Save(model, null, ModelPath);

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

            // Load model (local MLContext: runs once at startup, avoids race with shared field)
            var mlContext = new MLContext();
            var model = mlContext.Model.Load(ModelPath, out var _);

            // Create thread-safe prediction engine pool and atomically swap container
            var enginePool = new DefaultObjectPool<PredictionEngine<SpamTextFeatures, SpamPrediction>>(
                new PredictionEnginePoolPolicy(mlContext, model));
            var newContainer = new ModelContainer(model, enginePool, metadata);
            var oldContainer = Interlocked.Exchange(ref _currentModel, newContainer);

            // Dispose old transformer if it supports IDisposable (ITransformer does not extend IDisposable)
            (oldContainer?.Model as IDisposable)?.Dispose();

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
    /// Thread-safe: borrows a PredictionEngine from the ObjectPool for each call.
    /// </summary>
    public SpamPrediction? Predict(string messageText)
    {
        var container = _currentModel;
        if (container == null)
        {
            _logger.LogWarning("Prediction engine not loaded - model needs training");
            return null;
        }

        var engine = container.EnginePool.Get();
        try
        {
            var features = new SpamTextFeatures { MessageText = messageText };
            return engine.Predict(features);
        }
        finally
        {
            container.EnginePool.Return(engine);
        }
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
        (_currentModel?.Model as IDisposable)?.Dispose();
    }
}
