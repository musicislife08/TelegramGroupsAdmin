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

    // Future: ProfileCheckConfig for Phase 2
}
