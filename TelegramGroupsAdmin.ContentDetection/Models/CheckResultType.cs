namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Content check result classification (spam, malware, policy violations)
/// Phase 4.13: Expanded to support multiple violation types
/// </summary>
public enum CheckResultType
{
    /// <summary>Content is clean with no violations detected</summary>
    Clean = 0,

    /// <summary>Content identified as spam</summary>
    Spam = 1,

    /// <summary>Content needs human review due to uncertain classification (AI-based checks only)</summary>
    Review = 2,

    /// <summary>Content contains malware detected via VirusTotal file scanning</summary>
    Malware = 3,

    /// <summary>Hard block policy violation triggering instant ban (URL hard blocks, severe policy violations)</summary>
    HardBlock = 4
}
