using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Core spam detection engine that orchestrates all spam checks
/// Loads configuration once, builds strongly-typed requests, and aggregates results
/// </summary>
public interface IContentDetectionEngine
{
    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// Loads config once, determines enabled checks, builds typed requests for each
    /// </summary>
    Task<ContentDetectionResult> CheckMessageAsync(ContentCheckRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run all non-AI pipeline checks (stop words, similarity, etc.)
    /// AI veto check runs separately via CheckMessageAsync when spam flags are set
    /// </summary>
    Task<ContentDetectionResult> RunPipelineChecksAsync(ContentCheckRequest request, CancellationToken cancellationToken = default);
}
