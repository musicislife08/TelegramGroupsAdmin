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
    /// <param name="triggeringChat">Chat the scan is for: targets alerts and the ban celebration, and supplies thresholds. Null when no chat applies (manual rescan, or a rescan-job user with no known chat).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forceRescan">Skip both cached-score reuses (60s freshness window and unchanged-profile diff), e.g. after a rename.</param>
    /// <param name="origin">Who asked for the scan, for the scan-source metric (welcome | rescan | manual). Concurrent callers share one run, whose first caller's origin is recorded.</param>
    /// <returns>Scan result with extracted data, score, and outcome.</returns>
    Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        CancellationToken ct,
        bool forceRescan = false,
        ProfileScanOrigin origin = ProfileScanOrigin.ChatEvent);
}
