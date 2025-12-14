using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.Security.Cryptography;
using System.Text.Json;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// ML.NET SDCA text classifier for spam detection.
/// Thread-safe Singleton service with volatile fields for atomic model swapping.
/// </summary>
public class MLTextClassifierService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MLTextClassifierService> _logger;
    private readonly string _dataDirectory;

    // Thread-safe retraining semaphore (prevents overlapping retrains)
    private readonly SemaphoreSlim _retrainingSemaphore = new(1, 1);

    // Volatile fields for atomic reads during prediction
    private volatile ITransformer? _model;
    private volatile PredictionEngine<SpamTextFeatures, SpamPrediction>? _predictionEngine;
    private volatile SpamClassifierMetadata? _metadata;

    public MLTextClassifierService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MLTextClassifierService> logger,
        IConfiguration configuration)
    {
        _contextFactory = contextFactory;
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
    /// Loads spam training samples from training_labels + detection_results.
    /// Explicit labels (training_labels) override auto-detection.
    /// </summary>
    public async Task<List<string>> LoadSpamTrainingSamplesAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Explicit spam labels (admin decisions override auto-detection)
        var explicitSpam = await context.TrainingLabels
            .Where(tl => tl.Label == "spam")
            .Join(context.Messages, tl => tl.MessageId, m => m.MessageId, (tl, m) => m)
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       m => m.MessageId, mt => mt.MessageId,
                       (m, mts) => new { m, mt = mts.FirstOrDefault() })
            .Select(x => x.mt != null ? x.mt.TranslatedText : x.m.MessageText)
            .Where(text => text != null && text.Length > 10)
            .ToListAsync(ct);

        // Implicit spam (high-confidence auto, not corrected)
        var explicitLabeledIds = await context.TrainingLabels.Select(tl => tl.MessageId).ToListAsync(ct);
        var implicitSpam = await context.DetectionResults
            .Where(dr => dr.IsSpam && dr.UsedForTraining && !explicitLabeledIds.Contains(dr.MessageId))
            .Join(context.Messages, dr => dr.MessageId, m => m.MessageId, (dr, m) => m)
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       m => m.MessageId, mt => mt.MessageId,
                       (m, mts) => new { m, mt = mts.FirstOrDefault() })
            .Select(x => x.mt != null ? x.mt.TranslatedText : x.m.MessageText)
            .Where(text => text != null && text.Length > 10)
            .ToListAsync(ct);

        var allSpam = explicitSpam.Concat(implicitSpam).OfType<string>().Distinct().OrderBy(x => x).ToList();

        _logger.LogInformation(
            "Loaded {Count} spam training samples ({Explicit} explicit + {Implicit} implicit)",
            allSpam.Count, explicitSpam.Count, implicitSpam.Count);

        return allSpam;
    }

    /// <summary>
    /// Loads ham training samples from training_labels + never-flagged messages.
    /// Capped at 1000 samples, quality filtered (≥50 words, 7-90 days old, not banned).
    /// </summary>
    public async Task<List<string>> LoadHamTrainingSamplesAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Explicit ham labels (admin corrections) - ALWAYS included
        var explicitHam = await context.TrainingLabels
            .Where(tl => tl.Label == "ham")
            .Join(context.Messages, tl => tl.MessageId, m => m.MessageId, (tl, m) => m)
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       m => m.MessageId, mt => mt.MessageId,
                       (m, mts) => new { m, mt = mts.FirstOrDefault() })
            .Select(x => x.mt != null ? x.mt.TranslatedText : x.m.MessageText)
            .Where(text => text != null && text.Length > 10)
            .ToListAsync(ct);

        // Implicit ham (never flagged, quality filtered, capped at 1000)
        // Use subqueries to avoid pulling large ID sets client-side
        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var ninetyDaysAgo = DateTimeOffset.UtcNow.AddDays(-90);

        var implicitHam = await (
            from m in context.Messages
            where !context.TrainingLabels.Any(tl => tl.MessageId == m.MessageId)  // Subquery
               && !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId && dr.IsSpam)  // Subquery
               && !context.TelegramUsers.Any(tu => tu.TelegramUserId == m.UserId && tu.IsBanned)  // Subquery
               && m.Timestamp >= ninetyDaysAgo
               && m.Timestamp <= sevenDaysAgo
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > 10
               && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 50
            orderby m.MessageId  // Deterministic (not random) - stable across runs
            select text
        ).Take(1000).ToListAsync(ct);

        var allHam = explicitHam.Concat(implicitHam).OfType<string>().Distinct().ToList();

        _logger.LogInformation(
            "Loaded {Count} ham training samples ({Explicit} explicit + {Implicit} implicit, capped at 1000)",
            allHam.Count, explicitHam.Count, implicitHam.Count);

        return allHam;
    }

    /// <summary>
    /// Trains the SDCA model with TF-IDF features.
    /// Uses SemaphoreSlim to prevent overlapping retraining.
    /// </summary>
    public async Task TrainModelAsync(CancellationToken ct = default)
    {
        // Prevent overlapping retrains
        if (!await _retrainingSemaphore.WaitAsync(0, ct))
        {
            _logger.LogWarning("Training already in progress, skipping this trigger");
            return;
        }

        try
        {
            _logger.LogInformation("Starting SDCA model training...");
            var startTime = DateTimeOffset.UtcNow;

            // Load training samples
            var spamSamples = await LoadSpamTrainingSamplesAsync(ct);
            var hamSamples = await LoadHamTrainingSamplesAsync(ct);

            if (spamSamples.Count == 0 || hamSamples.Count == 0)
            {
                _logger.LogWarning("Insufficient training data (spam: {Spam}, ham: {Ham})",
                    spamSamples.Count, hamSamples.Count);
                return;
            }

            // Build training data
            var trainingData = spamSamples
                .Select(text => new SpamTextFeatures { MessageText = text, IsSpam = true })
                .Concat(hamSamples.Select(text => new SpamTextFeatures { MessageText = text, IsSpam = false }))
                .ToList();

            // Create ML.NET context and load data
            var mlContext = new MLContext(seed: 42);
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

            // Save model and metadata
            await SaveModelAsync(model, metadata, ct);

            // Atomically swap volatile fields (thread-safe)
            _model = model;
            _predictionEngine = mlContext.Model.CreatePredictionEngine<SpamTextFeatures, SpamPrediction>(model);
            _metadata = metadata;

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
    private async Task SaveModelAsync(ITransformer model, SpamClassifierMetadata metadata, CancellationToken ct)
    {
        // Ensure directory exists (should be created by Program.cs, but double-check)
        Directory.CreateDirectory(ModelDirectory);

        // Save model to disk
        var mlContext = new MLContext();
        mlContext.Model.Save(model, null, ModelPath);

        // Compute SHA256 hash
        await using (var stream = File.OpenRead(ModelPath))
        {
            var hash = await SHA256.HashDataAsync(stream, ct);
            metadata.ModelHash = Convert.ToHexString(hash);
        }

        // Get model file size
        var fileInfo = new FileInfo(ModelPath);
        metadata.ModelSizeBytes = fileInfo.Length;

        // Save metadata as JSON
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(MetadataPath, json, ct);

        _logger.LogInformation(
            "Model saved: {Size} bytes, SHA256: {Hash}",
            metadata.ModelSizeBytes, metadata.ModelHash);
    }

    /// <summary>
    /// Loads model and metadata from disk with SHA256 hash verification.
    /// Returns true if successful, false if model doesn't exist or verification fails.
    /// </summary>
    public async Task<bool> LoadModelAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ModelPath) || !File.Exists(MetadataPath))
        {
            _logger.LogInformation("No persisted model found at {Path}", ModelPath);
            return false;
        }

        try
        {
            // Load metadata
            var json = await File.ReadAllTextAsync(MetadataPath, ct);
            var metadata = JsonSerializer.Deserialize<SpamClassifierMetadata>(json);

            if (metadata == null)
            {
                _logger.LogWarning("Failed to deserialize metadata from {Path}", MetadataPath);
                return false;
            }

            // Verify SHA256 hash
            await using (var stream = File.OpenRead(ModelPath))
            {
                var hash = await SHA256.HashDataAsync(stream, ct);
                var computedHash = Convert.ToHexString(hash);

                if (computedHash != metadata.ModelHash)
                {
                    _logger.LogWarning(
                        "Model hash mismatch (expected: {Expected}, got: {Actual}), retraining required",
                        metadata.ModelHash, computedHash);
                    return false;
                }
            }

            // Load model
            var mlContext = new MLContext();
            var model = mlContext.Model.Load(ModelPath, out var _);

            // Create prediction engine and update volatile fields atomically
            _model = model;
            _predictionEngine = mlContext.Model.CreatePredictionEngine<SpamTextFeatures, SpamPrediction>(model);
            _metadata = metadata;

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
    /// Thread-safe: uses volatile fields for atomic reads.
    /// </summary>
    public SpamPrediction? Predict(string messageText)
    {
        var engine = _predictionEngine;
        if (engine == null)
        {
            _logger.LogWarning("Prediction engine not loaded - model needs training");
            return null;
        }

        var features = new SpamTextFeatures { MessageText = messageText };
        return engine.Predict(features);
    }

    /// <summary>
    /// Gets current model metadata (training stats, timestamp, hash).
    /// </summary>
    public SpamClassifierMetadata? GetMetadata() => _metadata;
}
