using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Utilities;

/// <summary>
/// Static utility for serializing/deserializing spam check results to/from JSON.
/// Uses V2-only CheckResult/CheckResults models (Score, Abstained, Details, ProcessingTimeMs).
/// </summary>
public static class CheckResultsSerializer
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serialize V2 check results to JSON.
    /// Returns JSON like: {"Checks":[{"CheckName":0,"Score":3.5,"Abstained":false,"Details":"...","ProcessingTimeMs":42.3},...]}
    /// </summary>
    public static string Serialize(List<ContentCheckResponseV2> checkResults)
    {
        var results = new CheckResults
        {
            Checks = checkResults.Select(c => new CheckResult
            {
                CheckName = c.CheckName,
                Score = c.Score,
                Abstained = c.Abstained,
                Details = c.Details,
                ProcessingTimeMs = c.ProcessingTimeMs
            }).ToList()
        };

        return JsonSerializer.Serialize(results, SerializeOptions);
    }

    /// <summary>
    /// Deserialize check results JSON back to CheckResult list.
    /// Expects V2-format JSONB (migrated from V1 by EF migration).
    /// </summary>
    public static List<CheckResult> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var results = JsonSerializer.Deserialize<CheckResults>(json, DeserializeOptions);

            return results?.Checks ?? [];
        }
        catch
        {
            return [];
        }
    }
}
