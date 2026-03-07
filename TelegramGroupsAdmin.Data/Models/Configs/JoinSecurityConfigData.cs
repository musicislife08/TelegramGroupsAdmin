namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of JoinSecurityConfig for EF Core JSON column mapping.
/// Contains security checks that run when a user joins.
/// </summary>
public class JoinSecurityConfigData
{
    /// <summary>
    /// CAS (Combot Anti-Spam) check configuration.
    /// </summary>
    public CasConfigData Cas { get; set; } = new();

    /// <summary>
    /// Impersonation detection configuration.
    /// </summary>
    public ImpersonationConfigData Impersonation { get; set; } = new();
}
