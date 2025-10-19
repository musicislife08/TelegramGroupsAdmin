namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Domain filter type classification
/// </summary>
public enum DomainFilterType
{
    /// <summary>Blacklist - domain is blocked</summary>
    Blacklist = 0,

    /// <summary>Whitelist - domain bypasses all checks</summary>
    Whitelist = 1
}
