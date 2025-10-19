namespace TelegramGroupsAdmin.ContentDetection.Models;

// ============================================================================
// Enums - Phase 4.13: URL Filtering
// ============================================================================

/// <summary>
/// Blocklist file format specification for parsing domain lists
/// </summary>
public enum BlocklistFormat
{
    /// <summary>Block List Project format - one domain per line with # for comments</summary>
    NewlineDomains = 0,

    /// <summary>Hosts file format - 0.0.0.0 or 127.0.0.1 prefix before domain</summary>
    HostsFile = 1,

    /// <summary>CSV format - domain in first column, header row skipped</summary>
    Csv = 2
}
