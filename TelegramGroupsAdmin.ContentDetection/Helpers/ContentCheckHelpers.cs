using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Helpers;

/// <summary>
/// Shared helper methods for content checks to reduce code duplication
/// </summary>
public static class ContentCheckHelpers
{
    /// <summary>
    /// Create a standardized fail-open response for content check errors
    /// All checks fail open (return Clean) to avoid false positives
    /// </summary>
    /// <param name="checkName">Name of the check that failed</param>
    /// <param name="ex">Exception that caused the failure</param>
    /// <param name="logger">Logger to record the error</param>
    /// <param name="userId">Optional user ID for context</param>
    /// <returns>Clean response with error details</returns>
    public static ContentCheckResponse CreateFailureResponse(
        string checkName,
        Exception ex,
        ILogger logger,
        long? userId = null)
    {
        var userContext = userId.HasValue ? $"user {userId}" : "request";
        logger.LogError(ex, "{CheckName} check failed for {UserContext}", checkName, userContext);

        return new ContentCheckResponse
        {
            CheckName = checkName,
            Result = CheckResultType.Clean, // Fail open
            Details = $"{checkName} check failed due to error",
            Confidence = 0,
            Error = ex
        };
    }
}
