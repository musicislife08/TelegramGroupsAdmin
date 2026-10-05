using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Single owner of the profile scan eligibility decision, shared by every automatic trigger:
/// join and first message scan a new, never-scanned or renamed user; a rename scans even an
/// excluded user. Admin-initiated rescans (UI) and the rescan job call IProfileScanService
/// directly and intentionally bypass this gate.
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
}
