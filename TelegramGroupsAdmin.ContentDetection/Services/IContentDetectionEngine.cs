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
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// Used internally for two-tier decision system (veto mode)
    /// </summary>
    Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(ContentCheckRequest request, CancellationToken cancellationToken = default);
}
