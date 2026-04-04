using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Singleton Bayes classifier service with immutable container pattern for thread-safe atomic model swapping.
/// Follows the same architecture as <see cref="MLTextClassifierService"/>.
/// Unlike ML.NET, Bayes doesn't persist to disk — it's fast enough to retrain on startup.
/// </summary>
public sealed class BayesClassifierService : IBayesClassifierService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BayesClassifierService> _logger;

    private readonly SemaphoreSlim _retrainingSemaphore = new(1, 1);

    /// <summary>
    /// Immutable container for thread-safe atomic model swapping.
    /// Classifier + metadata swap together via Interlocked.Exchange.
    /// </summary>
    private sealed record BayesModelContainer(BayesClassifier Classifier, BayesClassifierMetadata Metadata);

    private volatile BayesModelContainer? _currentModel;

    public BayesClassifierService(
        IServiceProvider serviceProvider,
        ILogger<BayesClassifierService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task TrainAsync(CancellationToken cancellationToken = default)
    {
        if (!await _retrainingSemaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Bayes training already in progress, skipping this trigger");
            return;
        }

        try
        {
            _logger.LogInformation("Starting Bayes classifier training...");

            // Create scope to resolve Scoped repository from Singleton service
            using var scope = _serviceProvider.CreateScope();
            var trainingDataRepository = scope.ServiceProvider.GetRequiredService<IMLTrainingDataRepository>();
            var tokenizerService = scope.ServiceProvider.GetRequiredService<ITokenizerService>();

            var spamSamples = await trainingDataRepository.GetSpamSamplesAsync(cancellationToken);
            var hamSamples = await trainingDataRepository.GetHamSamplesAsync(spamSamples.Count, cancellationToken);

            if (spamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass ||
                hamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass)
            {
                _logger.LogWarning(
                    "Insufficient training data for Bayes (spam: {Spam}, ham: {Ham}, minimum: {Min})",
                    spamSamples.Count, hamSamples.Count, SpamClassifierMetadata.MinimumSamplesPerClass);
                return; // _currentModel stays null → Classify() returns null → check abstains
            }

            // Build word-count dictionaries from training data, then construct an immutable
            // BayesClassifier with frozen snapshots and pre-computed aggregates.
            // CRITICAL: Never mutate a live classifier — FrozenDictionary fields are read-only by design.
            var spamCounts = new Dictionary<string, int>();
            var hamCounts = new Dictionary<string, int>();

            var spamCount = 0;
            var hamCount = 0;
            var explicitCount = 0;

            foreach (var sample in spamSamples.Concat(hamSamples))
            {
                var words = tokenizerService.Tokenize(sample.Text);

                if (sample.Label == TrainingLabel.Spam)
                {
                    foreach (var word in words)
                        spamCounts[word] = spamCounts.GetValueOrDefault(word, 0) + 1;
                    spamCount++;
                }
                else
                {
                    foreach (var word in words)
                        hamCounts[word] = hamCounts.GetValueOrDefault(word, 0) + 1;
                    hamCount++;
                }

                if (sample.Source == TrainingSampleSource.Explicit)
                    explicitCount++;
            }

            var newClassifier = new BayesClassifier(spamCounts, hamCounts, spamCount, hamCount);

            var metadata = new BayesClassifierMetadata
            {
                TrainedAt = DateTimeOffset.UtcNow,
                SpamSampleCount = spamCount,
                HamSampleCount = hamCount,
                SpamVocabularySize = newClassifier.SpamVocabularySize,
                HamVocabularySize = newClassifier.HamVocabularySize
            };

            // Log balance warning
            var totalSamples = spamCount + hamCount;
            var spamRatio = totalSamples > 0 ? (double)spamCount / totalSamples : 0.0;
            var isBalanced = spamRatio >= SpamClassifierMetadata.MinBalancedSpamRatio &&
                             spamRatio <= SpamClassifierMetadata.MaxBalancedSpamRatio;

            if (!isBalanced)
            {
                _logger.LogWarning(
                    "Bayes classifier training with imbalanced data: {Spam} spam + {Ham} ham = {SpamRatio:P1} spam ratio " +
                    "(recommended: 20-80%). Accuracy may be reduced.",
                    spamCount, hamCount, spamRatio);
            }

            // Atomically swap in the new model
            var newContainer = new BayesModelContainer(newClassifier, metadata);
            Interlocked.Exchange(ref _currentModel, newContainer);

            _logger.LogInformation(
                "Bayes classifier trained with {Total} samples ({Explicit} explicit labels, {Spam} spam, {Ham} ham)",
                totalSamples, explicitCount, spamCount, hamCount);
        }
        finally
        {
            _retrainingSemaphore.Release();
        }
    }

    public BayesClassificationResult? Classify(string message)
    {
        var model = _currentModel;
        if (model is null)
            return null;

        // BayesClassifier.ClassifyMessage does its own span-based tokenization inline —
        // emoji removal is unnecessary (emojis don't match the \b[\w']+\b regex).
        return model.Classifier.ClassifyMessage(message);
    }

    public BayesClassifierMetadata? GetMetadata() => _currentModel?.Metadata;

    public void Dispose()
    {
        _retrainingSemaphore.Dispose();
    }
}
