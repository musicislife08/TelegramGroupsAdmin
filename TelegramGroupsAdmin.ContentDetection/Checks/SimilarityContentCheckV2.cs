using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.ML;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 similarity check using ML.NET SDCA classifier
/// Scoring: 0-5.0 points based on ML probability (≥0.95→5pts, ≥0.85→3.5pts, ≥0.70→2pts, ≥0.60→1pt)
/// Abstain when probability < threshold or model not loaded
/// Safety toggle: Can be disabled via config (Enabled=false)
/// </summary>
public class SimilarityContentCheckV2(
    ILogger<SimilarityContentCheckV2> logger,
    IMLTextClassifierService mlClassifier) : IContentCheckV2
{
    public CheckName CheckName => CheckName.Similarity;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip expensive database queries for trusted/admin users
        // Similarity is not a critical check - it requires database queries and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping Similarity check for user {UserId}: User is {UserType}",
                request.UserId,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (SimilarityCheckRequest)request;

        try
        {
            // Safety toggle: Abstain if check is disabled
            if (req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Run ML.NET SDCA prediction
            var prediction = mlClassifier.Predict(req.Message);

            if (prediction == null)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "ML model not loaded - run training first",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Abstain if probability below threshold (converted from 0-100 to 0-1)
            var probabilityThreshold = req.SimilarityThreshold;
            if (prediction.Probability < probabilityThreshold)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Low spam probability ({prediction.Probability:P0}, threshold: {probabilityThreshold:P0})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Map ML probability to score (0-5 points)
            // Plan: ≥0.95→5pts, ≥0.85→3.5pts, ≥0.70→2pts, ≥0.60→1pt, <0.60→abstain
            var score = prediction.Probability switch
            {
                >= ScoringConstants.SimilarityThreshold95 => ScoringConstants.ScoreSimilarity95,
                >= ScoringConstants.SimilarityThreshold85 => ScoringConstants.ScoreSimilarity85,
                >= ScoringConstants.SimilarityThreshold70 => ScoringConstants.ScoreSimilarity70,
                >= ScoringConstants.SimilarityThreshold60 => ScoringConstants.ScoreSimilarity60,
                _ => 0.0 // Should never reach here (abstained above)
            };

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = $"ML spam probability: {prediction.Probability:P1} (score: {score})",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ML.NET SDCA spam check");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }
}
