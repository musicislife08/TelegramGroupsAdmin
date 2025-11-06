using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramGroupsAdmin.Data.Attributes;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for configs table
/// Unified configuration storage with JSONB columns for different config types
/// chat_id = 0 means global config, otherwise chat-specific override
/// </summary>
[Table("configs")]
public class ConfigRecordDto
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Chat ID (0 = global config, otherwise chat-specific override)
    /// </summary>
    [Column("chat_id")]
    public long ChatId { get; set; } = 0;

    /// <summary>
    /// Spam detection configuration (JSONB)
    /// </summary>
    [Column("spam_detection_config", TypeName = "jsonb")]
    public string? SpamDetectionConfig { get; set; }

    /// <summary>
    /// Welcome message configuration (JSONB)
    /// </summary>
    [Column("welcome_config", TypeName = "jsonb")]
    public string? WelcomeConfig { get; set; }

    /// <summary>
    /// Log level configuration (JSONB)
    /// </summary>
    [Column("log_config", TypeName = "jsonb")]
    public string? LogConfig { get; set; }

    /// <summary>
    /// Moderation configuration (JSONB) - future use
    /// </summary>
    [Column("moderation_config", TypeName = "jsonb")]
    public string? ModerationConfig { get; set; }

    /// <summary>
    /// Bot protection configuration (JSONB)
    /// Phase 6.1: Bot Auto-Ban
    /// </summary>
    [Column("bot_protection_config", TypeName = "jsonb")]
    public string? BotProtectionConfig { get; set; }

    /// <summary>
    /// Telegram bot service configuration (JSONB)
    /// Controls whether the bot polling service is active
    /// GLOBAL ONLY - only used when chat_id = 0
    /// </summary>
    [Column("telegram_bot_config", TypeName = "jsonb")]
    public string? TelegramBotConfig { get; set; }

    /// <summary>
    /// File scanning configuration (JSONB)
    /// Phase 4.17: File Scanning (ClamAV, YARA, cloud services)
    /// </summary>
    [Column("file_scanning_config", TypeName = "jsonb")]
    public string? FileScanningConfig { get; set; }

    /// <summary>
    /// Background jobs configuration (JSONB)
    /// Stores schedule and settings for scheduled backups, cleanup, etc.
    /// Only used for global config (chat_id = 0)
    /// </summary>
    [Column("background_jobs_config", TypeName = "jsonb")]
    public string? BackgroundJobsConfig { get; set; }

    /// <summary>
    /// API keys for external services (encrypted TEXT, not JSONB)
    /// Stores VirusTotal and other service API keys encrypted with Data Protection
    /// Encrypted at rest, automatically decrypted during backup export and re-encrypted during restore
    /// Only used for global config (chat_id = 0)
    /// Note: Uses TEXT not JSONB because encrypted data is base64, not valid JSON
    /// </summary>
    [Column("api_keys")]
    [ProtectedData(Purpose = DataProtectionPurposes.ApiKeys)]
    public string? ApiKeys { get; set; }

    /// <summary>
    /// Backup encryption configuration (JSONB)
    /// Metadata for backup encryption (algorithm, iterations, timestamps)
    /// Note: Passphrase moved to separate passphrase_encrypted column for proper backup/restore handling
    /// Only used for global config (chat_id = 0)
    /// </summary>
    [Column("backup_encryption_config", TypeName = "jsonb")]
    public string? BackupEncryptionConfig { get; set; }

    /// <summary>
    /// Backup encryption passphrase (encrypted TEXT, not JSONB)
    /// Encrypted at rest with Data Protection, automatically decrypted during backup export and re-encrypted during restore
    /// Moved from BackupEncryptionConfig JSONB to dedicated column for cross-machine compatibility
    /// Only used for global config (chat_id = 0)
    /// </summary>
    [Column("passphrase_encrypted")]
    [ProtectedData(Purpose = DataProtectionPurposes.TotpSecrets)]
    public string? PassphraseEncrypted { get; set; }

    /// <summary>
    /// Cached permanent invite link for this chat (NULL for global config or public chats)
    /// Used for return-to-chat buttons in DM notifications (welcome, tempban, etc.)
    /// Automatically created/reused by ChatInviteLinkService
    /// </summary>
    [Column("invite_link")]
    public string? InviteLink { get; set; }

    /// <summary>
    /// Telegram bot token (encrypted TEXT, not JSONB)
    /// Encrypted at rest with Data Protection, automatically decrypted during backup export and re-encrypted during restore
    /// Only used for global config (chat_id = 0)
    /// Migrated from TELEGRAM__BOTTOKEN env var to database for UI-based configuration
    /// Note: Uses TEXT not JSONB because encrypted data is base64, not valid JSON
    /// </summary>
    [Column("telegram_bot_token_encrypted")]
    [ProtectedData(Purpose = DataProtectionPurposes.TelegramBotToken)]
    public string? TelegramBotTokenEncrypted { get; set; }

    /// <summary>
    /// OpenAI service configuration (JSONB)
    /// Model selection, max tokens, and other non-sensitive settings
    /// API key stored in api_keys column (encrypted)
    /// Only used for global config (chat_id = 0)
    /// </summary>
    [Column("openai_config", TypeName = "jsonb")]
    public string? OpenAIConfig { get; set; }

    /// <summary>
    /// SendGrid email service configuration (JSONB)
    /// Enabled flag, from address, from name, and other non-sensitive settings
    /// API key stored in api_keys column (encrypted)
    /// Only used for global config (chat_id = 0)
    /// </summary>
    [Column("sendgrid_config", TypeName = "jsonb")]
    public string? SendGridConfig { get; set; }

    /// <summary>
    /// When this config was created (UTC timestamp)
    /// </summary>
    [Column("created_at")]
    [Required]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this config was last updated (UTC timestamp)
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
