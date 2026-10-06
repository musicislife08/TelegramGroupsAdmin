namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Who asked for a profile scan. Tags the scan-source metric only; the chat passed with the scan
/// still targets alerts and supplies thresholds.
/// </summary>
public enum ProfileScanOrigin
{
    /// <summary>A chat event through the scan gate: join, first message or rename. Metric source "welcome".</summary>
    ChatEvent,

    /// <summary>The rescan job or an admin's manual rescan. Metric source "rescan".</summary>
    Rescan
}
