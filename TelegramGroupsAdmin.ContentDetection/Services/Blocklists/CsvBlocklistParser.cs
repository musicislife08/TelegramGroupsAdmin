using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Parser for CSV format: domain in first column, header skipped
/// Example:
/// domain,category,notes
/// evil.com,malware,description
/// spam.net,phishing,another note
/// </summary>
public class CsvBlocklistParser : IBlocklistParser
{
    public BlocklistFormat Format => BlocklistFormat.Csv;

    public List<string> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Skip if no data
        if (lines.Length == 0)
        {
            return [];
        }

        return lines
            .Skip(1)  // Skip header row
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))  // Skip comments
            .Select(line => ExtractFirstColumn(line))
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => NormalizeDomain(domain!))
            .Distinct(StringComparer.OrdinalIgnoreCase)  // Remove duplicates (case-insensitive)
            .ToList();
    }

    /// <summary>
    /// Extract first column from CSV line (domain)
    /// Handles simple CSV (comma-separated)
    /// </summary>
    private static string? ExtractFirstColumn(string line)
    {
        var columns = line.Split(',');
        if (columns.Length == 0)
        {
            return null;
        }

        return columns[0].Trim();
    }

    /// <summary>
    /// Normalize domain: lowercase, trim, remove www prefix
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();

        // Remove www prefix if present
        if (domain.StartsWith("www."))
        {
            domain = domain.Substring(4);
        }

        // Remove quotes if present (CSV quoting)
        domain = domain.Trim('"', '\'');

        return domain;
    }
}
