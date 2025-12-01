namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// API keys for external services used in file scanning
/// Stored encrypted in configs.api_keys JSONB column with [ProtectedData] attribute
/// Backup system automatically decrypts during export and re-encrypts during restore
/// </summary>
public class ApiKeysConfig
{
    /// <summary>
    /// VirusTotal API key (https://www.virustotal.com/gui/my-apikey)
    /// Required for cloud file scanning with 70+ antivirus engines
    /// Free tier: 500 requests/day, 4 requests/minute
    /// </summary>
    public string? VirusTotal { get; set; }

    /// <summary>
    /// OpenAI API key (https://platform.openai.com/api-keys)
    /// Required for image spam detection and translation features
    /// </summary>
    public string? OpenAI { get; set; }

    /// <summary>
    /// SendGrid API key (https://app.sendgrid.com/settings/api_keys)
    /// Required for email verification, password reset, and notification emails
    /// </summary>
    public string? SendGrid { get; set; }

    /// <summary>
    /// [DEPRECATED] VAPID public key - migrated to WebPushConfig.VapidPublicKey
    /// Kept for backwards compatibility during migration.
    /// VapidKeyMigrationService moves these to new location on startup.
    /// TODO: Remove after production migration - see GitHub issue #121
    /// </summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>
    /// [DEPRECATED] VAPID private key - migrated to configs.vapid_private_key_encrypted column
    /// Kept for backwards compatibility during migration.
    /// VapidKeyMigrationService moves these to new location on startup.
    /// TODO: Remove after production migration - see GitHub issue #121
    /// </summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>
    /// Returns true if at least one API key is configured
    /// </summary>
    public bool HasAnyKey()
    {
        return !string.IsNullOrWhiteSpace(VirusTotal) ||
               !string.IsNullOrWhiteSpace(OpenAI) ||
               !string.IsNullOrWhiteSpace(SendGrid);
    }

    /// <summary>
    /// [DEPRECATED] Check if VAPID keys exist in old location (for migration)
    /// New code should use ISystemConfigRepository.HasVapidKeysAsync()
    /// TODO: Remove after production migration - see GitHub issue #121
    /// </summary>
    public bool HasVapidKeys()
    {
        return !string.IsNullOrWhiteSpace(VapidPublicKey) &&
               !string.IsNullOrWhiteSpace(VapidPrivateKey);
    }
}
