using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Scans user profiles via the WTelegram User API to detect spam signals
/// in bios, personal channels, and pinned stories.
/// </summary>
public interface IProfileScanService
{
    /// <summary>
    /// Scan a user's profile and take appropriate action (ban, report, or pass).
    /// When the profile cannot be read (no session, unresolvable, full profile not fetched, timeout, FLOOD_WAIT), the name is scored alone and acted on.
    /// </summary>
    /// <param name="user">Identity of the Telegram user to scan.</param>
    /// <param name="triggeringChat">Chat that triggered the scan (for reports). Null for background scans.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forceRescan">Skip both cached-score reuses (60s freshness window and unchanged-profile diff), e.g. after a rename.</param>
    /// <returns>Scan result with extracted data, score, and outcome.</returns>
    Task<ProfileScanResult> ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool forceRescan = false);
}
