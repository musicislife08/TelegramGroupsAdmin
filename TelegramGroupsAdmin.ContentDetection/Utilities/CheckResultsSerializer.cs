using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Utilities;

/// <summary>
/// Static utility for serializing/deserializing spam check results to/from JSON
/// Uses concrete CheckResult/CheckResults models for type safety
/// </summary>
public static class CheckResultsSerializer
{
    /// <summary>
    /// Serialize check results to JSON with proper field names and enum values
    /// Returns JSON like: {"Checks":[{"CheckName":0,"Result":1,"Confidence":95,"Reason":"..."},...]}
    /// Enums are serialized as integers for database efficiency
    /// </summary>
    public static string Serialize(List<ContentCheckResponse> checkResults)
    {
        var results = new CheckResults
        {
            Checks = checkResults.Select(c => new CheckResult
            {
                CheckName = c.CheckName,
                Result = c.Result,
                Confidence = c.Confidence,
                Reason = c.Details
            }).ToList()
        };

        return JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    /// <summary>
    /// Deserialize check results JSON back to CheckResult list
    /// Handles enum deserialization automatically (integers to enum values)
    /// </summary>
    public static List<CheckResult> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var results = JsonSerializer.Deserialize<CheckResults>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return results?.Checks ?? [];
        }
        catch
        {
            return [];
        }
    }
}
