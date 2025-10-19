using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Interface for parsing blocklist files into domain lists
/// Phase 4.13: URL Filtering
/// </summary>
public interface IBlocklistParser
{
    /// <summary>
    /// Format this parser supports
    /// </summary>
    BlocklistFormat Format { get; }

    /// <summary>
    /// Parse blocklist content into list of domains
    /// </summary>
    /// <param name="content">Raw blocklist content</param>
    /// <returns>List of normalized domains (lowercase, trimmed)</returns>
    List<string> Parse(string content);
}
