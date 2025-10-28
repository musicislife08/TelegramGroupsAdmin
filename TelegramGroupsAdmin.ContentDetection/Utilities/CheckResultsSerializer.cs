using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Utilities;

/// <summary>
/// Static utility for serializing spam check results to compact JSON format
/// Extracted from ContentDetectionEngine to avoid reflection usage
/// Phase 4.5: Updated to use Result enum instead of IsSpam boolean
/// </summary>
public static class CheckResultsSerializer
{
    /// <summary>
    /// Serialize check results to compact JSON with minimal field names to save database space
    /// Returns JSON like: {"checks":[{"name":"StopWords","result":"spam","conf":95.0,"reason":"..."},...]}
    /// </summary>
    public static string Serialize(List<ContentCheckResponse> checkResults)
    {
        var checks = checkResults.Select(c => new
        {
            name = c.CheckName,
            result = c.Result.ToString().ToLowerInvariant(), // "spam", "clean", or "review"
            conf = c.Confidence,
            reason = c.Details
        });

        return JsonSerializer.Serialize(new { checks }, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }
}
