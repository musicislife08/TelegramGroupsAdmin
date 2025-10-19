using System.Text.RegularExpressions;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Parser for hosts file format: 0.0.0.0 or 127.0.0.1 prefix
/// Example:
/// # Comment line
/// 0.0.0.0 evil.com
/// 127.0.0.1 spam.net
/// 0.0.0.0 phishing.org
/// </summary>
public partial class HostsFileParser : IBlocklistParser
{
    public BlocklistFormat Format => BlocklistFormat.HostsFile;

    // Regex to match hosts file entries: IP address followed by domain
    private static readonly Regex HostsLineRegex = HostsLinePattern();

    public List<string> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))  // Skip empty lines
            .Where(line => !line.StartsWith('#'))             // Skip comments
            .Select(line => ExtractDomain(line))
            .Where(domain => !string.IsNullOrEmpty(domain))   // Skip invalid entries
            .Select(domain => NormalizeDomain(domain!))       // Non-null assertion after Where filter
            .Distinct(StringComparer.OrdinalIgnoreCase)       // Remove duplicates (case-insensitive)
            .ToList();
    }

    /// <summary>
    /// Extract domain from hosts file line
    /// Matches: 0.0.0.0 domain.com or 127.0.0.1 domain.com
    /// </summary>
    private static string? ExtractDomain(string line)
    {
        var match = HostsLineRegex.Match(line);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
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

        return domain;
    }

    /// <summary>
    /// Compiled regex for hosts file format
    /// Matches: 0.0.0.0 or 127.0.0.1 followed by whitespace and domain
    /// </summary>
    [GeneratedRegex(@"^(?:0\.0\.0\.0|127\.0\.0\.1)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HostsLinePattern();
}
