namespace TelegramGroupsAdmin.Data.Attributes;

/// <summary>
/// Indicates that this property contains Data Protection encrypted data
/// that must be decrypted during export and re-encrypted during import
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ProtectedDataAttribute : Attribute
{
    /// <summary>
    /// The Data Protection purpose string used for encryption/decryption.
    /// Must match the purpose used when the data was originally encrypted.
    /// Default: "TgSpamPreFilter.TotpSecrets" (for backward compatibility)
    /// </summary>
    public string Purpose { get; set; } = "TgSpamPreFilter.TotpSecrets";
}
