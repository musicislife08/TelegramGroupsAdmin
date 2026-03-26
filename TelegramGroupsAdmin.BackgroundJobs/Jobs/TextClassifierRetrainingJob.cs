using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.ContentDetection.ML;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic for retraining ML.NET text classifier with latest spam/ham samples
/// Runs on schedule (default: every 8 hours) to keep model fresh with new data
/// Can be triggered manually via Settings → Background Jobs UI
/// </summary>
[DisallowConcurrentExecution]
public class TextClassifierRetrainingJob : IJob
{
    private readonly IMLTextClassifierService _mlClassifier;
    private readonly ILogger<TextClassifierRetrainingJob> _logger;
    private readonly JobMetrics _jobMetrics;

    public TextClassifierRetrainingJob(
        IMLTextClassifierService mlClassifier,
        ILogger<TextClassifierRetrainingJob> logger,
        JobMetrics jobMetrics)
    {
        _mlClassifier = mlClassifier;
        _logger = logger;
        _jobMetrics = jobMetrics;
    }

    /// <summary>
    /// Execute ML model retraining (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        const string jobName = "TextClassifierRetraining";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation("Starting scheduled ML text classifier retraining");

            await _mlClassifier.TrainModelAsync(context.CancellationToken);

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
                _logger.LogWarning("Model retraining completed but metadata unavailable (model may not have trained due to insufficient data)");
            }

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML text classifier retraining failed");
            throw; // Re-throw for retry logic and exception recording
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
        }
    }
}
