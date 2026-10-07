using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Single owner of the profile scan eligibility decision, shared by every automatic scan: join and
/// first message scan a new, never-scanned or renamed user; a rename scans even an excluded user;
/// the rescan job retries incomplete scans. Bots, banned and trusted users, and admins of any managed
/// chat are never scanned. Only the admin's manual rescan (web UI) calls IProfileScanService directly.
/// </summary>
public interface IProfileScanGate
{
    /// <summary>
    /// Runs a profile scan if this trigger is eligible for this user.
    /// </summary>
    /// <returns>The scan result, or null when the scan was skipped.</returns>
    Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user,
        ChatIdentity? chat,
        ProfileScanTrigger trigger,
        CancellationToken ct,
        bool forceRescan = false);

    /// <summary>Whether profile scanning is enabled in the chat's effective config (0 = global).</summary>
    Task<bool> IsScanningEnabledAsync(long chatId, CancellationToken ct);
}
