namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for security checks that run when a user joins.
/// Lives under WelcomeConfig since these run during the join/welcome flow.
/// </summary>
public class JoinSecurityConfig
{
    /// <summary>
    /// CAS (Combot Anti-Spam) check configuration.
    /// </summary>
    public CasConfig Cas { get; set; } = new();

    /// <summary>
    /// Impersonation detection configuration.
    /// </summary>
    public ImpersonationConfig Impersonation { get; set; } = new();

    /// <summary>
    /// User API profile scanning configuration.
    /// </summary>
    public ProfileScanConfig ProfileScan { get; set; } = new();

    /// <summary>
    /// Username blacklist configuration - auto-ban users with blacklisted display names on join.
    /// </summary>
    public UsernameBlacklistConfig UsernameBlacklist { get; set; } = new();
}
