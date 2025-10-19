using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Parser for Block List Project format: one domain per line, # for comments
/// Example:
/// # Comment line
/// evil.com
/// spam.net
/// # Another comment
/// phishing.org
/// </summary>
public class NewlineDomainsParser : IBlocklistParser
{
    public BlocklistFormat Format => BlocklistFormat.NewlineDomains;

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
            .Select(domain => NormalizeDomain(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)       // Remove duplicates (case-insensitive)
            .ToList();
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
}
