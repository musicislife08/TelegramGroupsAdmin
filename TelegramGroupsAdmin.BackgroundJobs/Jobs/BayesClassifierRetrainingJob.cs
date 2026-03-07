using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.Core.Telemetry;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job for retraining the Bayes classifier with latest spam/ham samples.
/// Follows the same pattern as <see cref="TextClassifierRetrainingJob"/>.
/// Can be triggered on-demand (after admin corrections) or on schedule.
/// </summary>
[DisallowConcurrentExecution]
public class BayesClassifierRetrainingJob : IJob
{
    private readonly IBayesClassifierService _bayesClassifier;
    private readonly ILogger<BayesClassifierRetrainingJob> _logger;

    public BayesClassifierRetrainingJob(
        IBayesClassifierService bayesClassifier,
        ILogger<BayesClassifierRetrainingJob> logger)
    {
        _bayesClassifier = bayesClassifier;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        const string jobName = "BayesClassifierRetraining";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation("Starting scheduled Bayes classifier retraining");

            await _bayesClassifier.TrainAsync(context.CancellationToken);

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

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bayes classifier retraining failed");
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
