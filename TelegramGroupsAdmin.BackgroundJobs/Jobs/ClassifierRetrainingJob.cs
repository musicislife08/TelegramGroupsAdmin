using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.ContentDetection.ML;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Merged job that retrains both the ML.NET SDCA text classifier and the Naive Bayes classifier
/// with the latest spam/ham training data. Training data is loaded once and shared between both.
/// Runs on schedule (default: every 8 hours) to keep models fresh with new data.
/// Can be triggered manually via Settings → Background Jobs UI or on-demand after admin corrections.
/// </summary>
[DisallowConcurrentExecution]
public class ClassifierRetrainingJob : IJob
{
    private readonly IMLTextClassifierService _mlClassifier;
    private readonly IBayesClassifierService _bayesClassifier;
    private readonly ILogger<ClassifierRetrainingJob> _logger;
    private readonly JobMetrics _jobMetrics;

    public ClassifierRetrainingJob(
        IMLTextClassifierService mlClassifier,
        IBayesClassifierService bayesClassifier,
        ILogger<ClassifierRetrainingJob> logger,
        JobMetrics jobMetrics)
    {
        _mlClassifier = mlClassifier;
        _bayesClassifier = bayesClassifier;
        _logger = logger;
        _jobMetrics = jobMetrics;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        const string jobName = "ClassifierRetrainingJob";
        var startTimestamp = Stopwatch.GetTimestamp();
        var overallSuccess = false;

        try
        {
            _logger.LogInformation("Starting scheduled classifier retraining (SDCA + Bayes)");

            // Train SDCA text classifier first
            var sdcaSuccess = await TrainSdcaAsync(context.CancellationToken);

            // Train Bayes classifier regardless of SDCA outcome
            var bayesSuccess = await TrainBayesAsync(context.CancellationToken);

            overallSuccess = sdcaSuccess && bayesSuccess;

            if (!overallSuccess)
            {
                _logger.LogWarning(
                    "Classifier retraining completed with partial failures — SDCA: {SdcaSuccess}, Bayes: {BayesSuccess}",
                    sdcaSuccess, bayesSuccess);
            }
            else
            {
                _logger.LogInformation("Classifier retraining completed successfully (SDCA + Bayes)");
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _jobMetrics.RecordJobExecution(jobName, overallSuccess, elapsedMs);
        }
    }

    private async Task<bool> TrainSdcaAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retraining ML.NET SDCA text classifier");

            await _mlClassifier.TrainModelAsync(cancellationToken);

            var metadata = _mlClassifier.GetMetadata();
            if (metadata != null)
            {
                _logger.LogInformation(
                    "ML text classifier retrained successfully: {Spam} spam + {Ham} ham samples (ratio: {Ratio:P1}, balanced: {Balanced})",
                    metadata.SpamSampleCount,
                    metadata.HamSampleCount,
                    metadata.SpamRatio,
                    metadata.IsBalanced);
            }
            else
            {
                _logger.LogWarning("SDCA retraining completed but metadata unavailable (model may not have trained due to insufficient data)");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML text classifier retraining failed");
            return false;
        }
    }

    private async Task<bool> TrainBayesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retraining Bayes classifier");

            await _bayesClassifier.TrainAsync(cancellationToken);

            var metadata = _bayesClassifier.GetMetadata();
            if (metadata != null)
            {
                _logger.LogInformation(
                    "Bayes classifier retrained successfully: {Spam} spam + {Ham} ham samples (ratio: {Ratio:P1})",
                    metadata.SpamSampleCount,
                    metadata.HamSampleCount,
                    metadata.SpamRatio);
            }
            else
            {
                _logger.LogWarning("Bayes retraining completed but metadata unavailable (insufficient data)");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bayes classifier retraining failed");
            return false;
        }
    }
}
