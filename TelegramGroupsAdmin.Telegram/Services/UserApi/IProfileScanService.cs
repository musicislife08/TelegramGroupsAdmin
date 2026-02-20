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
    /// </summary>
    /// <param name="user">Identity of the Telegram user to scan.</param>
    /// <param name="triggeringChat">Chat that triggered the scan (for reports). Null for background scans.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scan result with extracted data, score, and outcome.</returns>
    Task<ProfileScanResult> ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct);
}
