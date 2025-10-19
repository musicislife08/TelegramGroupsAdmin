using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Coordinates spam checking workflow by filtering trusted/admin users before spam detection
/// Centralizes spam checking logic so it's consistent between bot and UI
/// </summary>
public interface ISpamCheckCoordinator
{
    /// <summary>
    /// Run complete spam check workflow: trust check → admin check → spam detection
    /// Trusted users and admins skip spam detection entirely
    /// </summary>
    /// <param name="request">Spam check request with message, user info, optional image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Coordinated result with trust/admin status and spam detection results</returns>
    Task<SpamCheckCoordinatorResult> CheckAsync(SpamLibRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete spam check result including user trust/admin status and spam detection
/// </summary>
public record SpamCheckCoordinatorResult
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
    public ContentDetectionResult? SpamResult { get; init; }

    /// <summary>
    /// Overall determination: should this message be allowed?
    /// </summary>
    public bool ShouldAllow => SpamCheckSkipped || (SpamResult?.IsSpam == false);

    /// <summary>
    /// Overall determination: should this message be flagged as spam?
    /// </summary>
    public bool IsSpam => !SpamCheckSkipped && (SpamResult?.IsSpam ?? false);
}
