using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Abstractions;

/// <summary>
/// Interface for individual content detection checks
/// Each check receives a strongly-typed request with exactly the config/data it needs
/// Engine decides if check should run - if CheckAsync is called, the check executes
/// Checks manage their own database access with appropriate TAKE() limits as guardrails
/// </summary>
public interface IContentCheck
{
    /// <summary>
    /// Name of this content check for identification
    /// </summary>
    string CheckName { get; }

    /// <summary>
    /// Execute the content check with a strongly-typed request
    /// Request contains message data and check-specific configuration
    /// Checks load database data themselves using injected services with guardrails
    /// </summary>
    /// <param name="request">Strongly-typed request with all data needed for this check</param>
    /// <returns>The result of this content check</returns>
    Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request);

    /// <summary>
    /// Whether this check should be executed for the given request
    /// Used by engine to determine if check is applicable (e.g., message length, has URLs)
    /// </summary>
    /// <param name="request">The original spam check request</param>
    /// <returns>True if this check should run</returns>
    bool ShouldExecute(ContentCheckRequest request);
}