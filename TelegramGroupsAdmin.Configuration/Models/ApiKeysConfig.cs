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
    /// SendGrid API key (https://app.sendgrid.com/settings/api_keys)
    /// Required for email verification, password reset, and notification emails
    /// </summary>
    public string? SendGrid { get; set; }

    // Legacy properties - used only for migration from old format
    // Will be copied to AIConnectionKeys dictionary on first load
    // TODO: Remove these after all deployments have migrated

    /// <summary>
    /// Legacy OpenAI API key - migrated to AIConnectionKeys["openai"]
    /// </summary>
    public string? OpenAI { get; set; }

    /// <summary>
    /// Legacy Azure OpenAI API key - migrated to AIConnectionKeys["azure-openai"]
    /// </summary>
    public string? AzureOpenAI { get; set; }

    /// <summary>
    /// Legacy Local AI API key - migrated to AIConnectionKeys["local-ai"]
    /// </summary>
    public string? LocalAI { get; set; }

    /// <summary>
    /// Per-connection API keys for AI providers.
    /// Maps connection ID (from AIProviderConfig.Connections) to API key.
    /// Supports multiple connections of the same provider type (e.g., multiple OpenAI accounts).
    /// </summary>
    public Dictionary<string, string> AIConnectionKeys { get; set; } = [];

    /// <summary>
    /// Returns true if at least one API key is configured
    /// </summary>
    public bool HasAnyKey()
    {
        return !string.IsNullOrWhiteSpace(VirusTotal) ||
               !string.IsNullOrWhiteSpace(SendGrid) ||
               AIConnectionKeys.Count > 0 ||
               // Legacy properties check
               !string.IsNullOrWhiteSpace(OpenAI) ||
               !string.IsNullOrWhiteSpace(AzureOpenAI) ||
               !string.IsNullOrWhiteSpace(LocalAI);
    }

    /// <summary>
    /// Migrates legacy API key properties to the new AIConnectionKeys dictionary.
    /// Returns true if any migration was performed.
    /// </summary>
    public bool MigrateLegacyKeys()
    {
        var migrated = false;

        if (!string.IsNullOrWhiteSpace(OpenAI) && !AIConnectionKeys.ContainsKey("openai"))
        {
            AIConnectionKeys["openai"] = OpenAI;
            OpenAI = null;
            migrated = true;
        }

        if (!string.IsNullOrWhiteSpace(AzureOpenAI) && !AIConnectionKeys.ContainsKey("azure-openai"))
        {
            AIConnectionKeys["azure-openai"] = AzureOpenAI;
            AzureOpenAI = null;
            migrated = true;
        }

        if (!string.IsNullOrWhiteSpace(LocalAI) && !AIConnectionKeys.ContainsKey("local-ai"))
        {
            AIConnectionKeys["local-ai"] = LocalAI;
            LocalAI = null;
            migrated = true;
        }

        return migrated;
    }

    /// <summary>
    /// Gets the API key for a specific AI connection, or null if not configured.
    /// </summary>
    public string? GetAIConnectionKey(string connectionId)
    {
        return AIConnectionKeys.TryGetValue(connectionId, out var key) ? key : null;
    }

    /// <summary>
    /// Sets the API key for a specific AI connection.
    /// Pass null or empty to remove the key.
    /// </summary>
    public void SetAIConnectionKey(string connectionId, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AIConnectionKeys.Remove(connectionId);
        }
        else
        {
            AIConnectionKeys[connectionId] = apiKey;
        }
    }
}
