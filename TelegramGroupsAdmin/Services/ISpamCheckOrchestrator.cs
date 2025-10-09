using SpamLibRequest = TelegramGroupsAdmin.SpamDetection.Models.SpamCheckRequest;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Orchestrates the complete spam checking workflow including user trust/admin checks and spam detection.
/// This centralizes all spam checking logic so it's consistent between the bot and UI.
/// </summary>
public interface ISpamCheckOrchestrator
{
    /// <summary>
    /// Run complete spam check workflow: trust check → admin check → spam detection
    /// </summary>
    /// <param name="request">Spam check request with message, user info, optional image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Orchestrated result with trust/admin status and spam detection results</returns>
    Task<SpamCheckOrchestratorResult> CheckAsync(SpamLibRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete spam check result including user trust/admin status and spam detection
/// </summary>
public record SpamCheckOrchestratorResult
{
    /// <summary>
    /// Whether user is explicitly trusted (bypasses all spam checks)
    /// </summary>
    public bool IsUserTrusted { get; init; }

    /// <summary>
    /// Whether user is a chat admin (bypasses all spam checks)
    /// </summary>
    public bool IsUserAdmin { get; init; }

    /// <summary>
    /// Whether spam detection was skipped due to trust/admin status
    /// </summary>
    public bool SpamCheckSkipped { get; init; }

    /// <summary>
    /// Reason spam check was skipped (if applicable)
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Spam detection result (null if skipped)
    /// </summary>
    public SpamDetectionResult? SpamResult { get; init; }

    /// <summary>
    /// Overall determination: should this message be allowed?
    /// </summary>
    public bool ShouldAllow => SpamCheckSkipped || (SpamResult?.IsSpam == false);

    /// <summary>
    /// Overall determination: should this message be flagged as spam?
    /// </summary>
    public bool IsSpam => !SpamCheckSkipped && (SpamResult?.IsSpam ?? false);
}
