using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Abstractions;

/// <summary>
/// V2 spam detection check interface with proper abstention support.
/// Returns additive scores (0.0-5.0 points) instead of Spam/Clean voting.
/// </summary>
/// <remarks>
/// <para>Each check implementation contributes a score that is summed by the detection engine.</para>
/// <para>Checks can abstain (score=0, abstained=true) when they cannot make a determination.</para>
/// </remarks>
public interface IContentCheckV2
{
    /// <summary>
    /// Gets the unique identifier for this check.
    /// </summary>
    CheckName CheckName { get; }

    /// <summary>
    /// Determines if this check should run based on request properties.
    /// </summary>
    /// <remarks>
    /// Use this for fast, synchronous pre-filtering (e.g., skip if message is empty,
    /// skip for trusted users, skip if required config is disabled).
    /// </remarks>
    /// <param name="request">The content check request containing message and user context.</param>
    /// <returns><c>true</c> if the check should execute; otherwise <c>false</c>.</returns>
    bool ShouldExecute(ContentCheckRequest request);

    /// <summary>
    /// Executes the spam check and returns a score contribution.
    /// </summary>
    /// <remarks>
    /// <para>Score range: 0.0 (no spam signal) to 5.0 (maximum spam signal).</para>
    /// <para>Set <see cref="ContentCheckResponseV2.Abstained"/> to <c>true</c> when the check
    /// cannot make a determination (e.g., API unavailable, insufficient data).</para>
    /// <para>Results may be cached by implementations to avoid redundant processing.</para>
    /// </remarks>
    /// <param name="request">
    /// The content check request. Cast to the appropriate derived type
    /// (e.g., <see cref="AIVetoCheckRequest"/>) for check-specific properties.
    /// </param>
    /// <returns>
    /// A <see cref="ContentCheckResponseV2"/> containing the score, abstention status,
    /// and diagnostic details.
    /// </returns>
    ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request);
}
