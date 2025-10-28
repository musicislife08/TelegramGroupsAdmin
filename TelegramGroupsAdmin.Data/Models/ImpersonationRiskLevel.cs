namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Risk level classification for impersonation alerts
/// </summary>
public enum ImpersonationRiskLevel
{
    /// <summary>50 points - name OR photo match detected</summary>
    Medium = 0,
    /// <summary>100 points - name AND photo match detected</summary>
    Critical = 1
}
