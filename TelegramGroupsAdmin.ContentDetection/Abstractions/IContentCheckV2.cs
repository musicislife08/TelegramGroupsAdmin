using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Abstractions;

/// <summary>
/// V2 spam detection check interface with proper abstention support
/// Returns additive scores (0-5.0 points) instead of Spam/Clean voting
/// </summary>
public interface IContentCheckV2
{
    /// <summary>
    /// Unique identifier for this check
    /// </summary>
    CheckName CheckName { get; }

    /// <summary>
    /// Determine if this check should run based on request properties
    /// </summary>
    bool ShouldExecute(ContentCheckRequest request);

    /// <summary>
    /// Execute the spam check and return a score contribution
    /// Score range: 0.0 (abstained/no evidence) to 5.0 (maximum spam signal)
    /// </summary>
    ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request);
}
