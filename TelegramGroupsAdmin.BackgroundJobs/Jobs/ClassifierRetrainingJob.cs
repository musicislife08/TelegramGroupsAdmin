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
public class ClassifierRetrainingJob(
    IMLTextClassifierService mlClassifier,
    IBayesClassifierService bayesClassifier,
    ILogger<ClassifierRetrainingJob> logger,
    JobMetrics jobMetrics) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        const string jobName = "ClassifierRetrainingJob";
        var startTimestamp = Stopwatch.GetTimestamp();
        var overallSuccess = false;

        try
        {
            logger.LogInformation("Starting scheduled classifier retraining (SDCA + Bayes)");

            // Train SDCA text classifier first
            var sdcaSuccess = await TrainSdcaAsync(context.CancellationToken);

            // Train Bayes classifier regardless of SDCA outcome
            var bayesSuccess = await TrainBayesAsync(context.CancellationToken);

            overallSuccess = sdcaSuccess && bayesSuccess;

            if (!overallSuccess)
            {
                logger.LogWarning(
                    "Classifier retraining completed with partial failures — SDCA: {SdcaSuccess}, Bayes: {BayesSuccess}",
                    sdcaSuccess, bayesSuccess);
            }
            else
            {
                logger.LogInformation("Classifier retraining completed successfully (SDCA + Bayes)");
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            jobMetrics.RecordJobExecution(jobName, overallSuccess, elapsedMs);
        }
    }

    private async Task<bool> TrainSdcaAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Retraining ML.NET SDCA text classifier");

            await mlClassifier.TrainModelAsync(cancellationToken);

            var metadata = mlClassifier.GetMetadata();
            if (metadata != null)
            {
                logger.LogInformation(
                    "ML text classifier retrained successfully: {Spam} spam + {Ham} ham samples (ratio: {Ratio:P1}, balanced: {Balanced})",
                    metadata.SpamSampleCount,
                    metadata.HamSampleCount,
                    metadata.SpamRatio,
                    metadata.IsBalanced);
            }
            else
            {
                logger.LogWarning("SDCA retraining completed but metadata unavailable (model may not have trained due to insufficient data)");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ML text classifier retraining failed");
            return false;
        }
    }

    private async Task<bool> TrainBayesAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Retraining Bayes classifier");

            await bayesClassifier.TrainAsync(cancellationToken);

            var metadata = bayesClassifier.GetMetadata();
            if (metadata != null)
            {
                logger.LogInformation(
                    "Bayes classifier retrained successfully: {Spam} spam + {Ham} ham samples (ratio: {Ratio:P1})",
                    metadata.SpamSampleCount,
                    metadata.HamSampleCount,
                    metadata.SpamRatio);
            }
            else
            {
                logger.LogWarning("Bayes retraining completed but metadata unavailable (insufficient data)");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bayes classifier retraining failed");
            return false;
        }
    }
}
