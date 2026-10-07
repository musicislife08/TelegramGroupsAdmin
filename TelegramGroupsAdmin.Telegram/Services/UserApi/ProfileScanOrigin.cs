namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Who asked for a profile scan. Tags the scan-source metric only (welcome | rescan | manual); the
/// chat passed with the scan still targets alerts and supplies thresholds.
/// </summary>
public enum ProfileScanOrigin
{
    /// <summary>A join or first message, through the scan gate. Metric source "welcome".</summary>
    ChatEvent,

    /// <summary>A rescan of a known user: a rename or the rescan job, both through the gate. Metric source "rescan".</summary>
    Rescan,

    /// <summary>An admin's manual rescan from the web UI. Metric source "manual".</summary>
    Manual
}
