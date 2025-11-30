namespace TelegramGroupsAdmin.Data.Constants;

/// <summary>
/// Constants for Data Protection purpose strings.
/// These purpose strings are used to create isolated data protection scopes,
/// ensuring encrypted data can only be decrypted with the same purpose string.
///
/// IMPORTANT: Changing these values will break existing encrypted data.
/// Each purpose creates a unique encryption key derivation.
/// </summary>
public static class DataProtectionPurposes
{
    /// <summary>
    /// Purpose for encrypting API keys (VirusTotal, etc.) in configs.api_keys column
    /// </summary>
    public const string ApiKeys = "ApiKeys";

    /// <summary>
    /// Purpose for encrypting TOTP secrets in configs.passphrase_encrypted column
    /// </summary>
    public const string TotpSecrets = "TgSpamPreFilter.TotpSecrets";

    /// <summary>
    /// Purpose for encrypting backup passphrases in configs.passphrase_encrypted column
    /// </summary>
    public const string BackupPassphrase = "BackupPassphrase";

    /// <summary>
    /// Purpose for encrypting Telegram bot token in configs.telegram_bot_token_encrypted column
    /// </summary>
    public const string TelegramBotToken = "TelegramBotToken";
}
