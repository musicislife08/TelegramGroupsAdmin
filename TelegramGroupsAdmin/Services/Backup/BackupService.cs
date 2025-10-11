using System.IO.Compression;
using Dapper;
using MessagePack;
using Npgsql;

namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Service for creating and restoring full system backups using MessagePack + tar.gz compression
/// </summary>
public class BackupService : IBackupService
{
    private readonly string _connectionString;
    private readonly ILogger<BackupService> _logger;

    public BackupService(IConfiguration configuration, ILogger<BackupService> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task<byte[]> ExportAsync()
    {
        _logger.LogInformation("Starting full system backup export");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var backup = new SystemBackup
        {
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Version = "1.0"
        };

        // Export all tables
        backup.Users = (await ExportUsersAsync(connection)).ToList();
        backup.Invites = (await ExportInvitesAsync(connection)).ToList();
        backup.AuditLogs = (await ExportAuditLogsAsync(connection)).ToList();
        backup.VerificationTokens = (await ExportVerificationTokensAsync(connection)).ToList();
        backup.Messages = (await ExportMessagesAsync(connection)).ToList();
        backup.MessageEdits = (await ExportMessageEditsAsync(connection)).ToList();
        backup.DetectionResults = (await ExportDetectionResultsAsync(connection)).ToList();
        backup.UserActions = (await ExportUserActionsAsync(connection)).ToList();
        backup.StopWords = (await ExportStopWordsAsync(connection)).ToList();
        backup.SpamDetectionConfigs = (await ExportSpamDetectionConfigsAsync(connection)).ToList();
        backup.SpamCheckConfigs = (await ExportSpamCheckConfigsAsync(connection)).ToList();
        backup.ChatPrompts = (await ExportChatPromptsAsync(connection)).ToList();
        backup.ManagedChats = (await ExportManagedChatsAsync(connection)).ToList();
        backup.ChatAdmins = (await ExportChatAdminsAsync(connection)).ToList();
        backup.TelegramUserMappings = (await ExportTelegramUserMappingsAsync(connection)).ToList();
        backup.TelegramLinkTokens = (await ExportTelegramLinkTokensAsync(connection)).ToList();
        backup.Reports = (await ExportReportsAsync(connection)).ToList();

        _logger.LogInformation(
            "Exported {UserCount} users, {MessageCount} messages, {DetectionCount} detections",
            backup.Users.Count, backup.Messages.Count, backup.DetectionResults.Count);

        // Serialize with MessagePack
        var serialized = MessagePackSerializer.Serialize(backup);

        // Compress with gzip
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
        {
            await gzipStream.WriteAsync(serialized);
        }

        var compressed = compressedStream.ToArray();
        _logger.LogInformation(
            "Backup complete: {OriginalSize} bytes → {CompressedSize} bytes ({Ratio:F1}% compression)",
            serialized.Length, compressed.Length, (1 - (double)compressed.Length / serialized.Length) * 100);

        return compressed;
    }

    public async Task RestoreAsync(byte[] backupBytes)
    {
        _logger.LogWarning("Starting full system restore - ALL DATA WILL BE WIPED");

        // Decompress
        using var compressedStream = new MemoryStream(backupBytes);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }

        // Deserialize
        var backup = MessagePackSerializer.Deserialize<SystemBackup>(decompressedStream.ToArray());

        _logger.LogInformation(
            "Restoring backup from {CreatedAt} (version {Version}): {UserCount} users, {MessageCount} messages",
            DateTimeOffset.FromUnixTimeSeconds(backup.CreatedAt), backup.Version,
            backup.Users.Count, backup.Messages.Count);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Single transaction - all or nothing
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // Wipe all tables (preserve VersionInfo for migrations)
            await WipeAllTablesAsync(connection, transaction);

            // Restore all tables
            await RestoreUsersAsync(connection, transaction, backup.Users);
            await RestoreInvitesAsync(connection, transaction, backup.Invites);
            await RestoreAuditLogsAsync(connection, transaction, backup.AuditLogs);
            await RestoreVerificationTokensAsync(connection, transaction, backup.VerificationTokens);
            await RestoreMessagesAsync(connection, transaction, backup.Messages);
            await RestoreMessageEditsAsync(connection, transaction, backup.MessageEdits);
            await RestoreDetectionResultsAsync(connection, transaction, backup.DetectionResults);
            await RestoreUserActionsAsync(connection, transaction, backup.UserActions);
            await RestoreStopWordsAsync(connection, transaction, backup.StopWords);
            await RestoreSpamDetectionConfigsAsync(connection, transaction, backup.SpamDetectionConfigs);
            await RestoreSpamCheckConfigsAsync(connection, transaction, backup.SpamCheckConfigs);
            await RestoreChatPromptsAsync(connection, transaction, backup.ChatPrompts);
            await RestoreManagedChatsAsync(connection, transaction, backup.ManagedChats);
            await RestoreChatAdminsAsync(connection, transaction, backup.ChatAdmins);
            await RestoreTelegramUserMappingsAsync(connection, transaction, backup.TelegramUserMappings);
            await RestoreTelegramLinkTokensAsync(connection, transaction, backup.TelegramLinkTokens);
            await RestoreReportsAsync(connection, transaction, backup.Reports);

            // Reset sequences for tables with auto-increment IDs
            await ResetSequencesAsync(connection, transaction);

            await transaction.CommitAsync();
            _logger.LogInformation("System restore completed successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "System restore failed - rolled back all changes");
            throw;
        }
    }

    public async Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes)
    {
        // Decompress
        using var compressedStream = new MemoryStream(backupBytes);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }

        // Deserialize
        var backup = MessagePackSerializer.Deserialize<SystemBackup>(decompressedStream.ToArray());

        return new BackupMetadata(
            CreatedAt: backup.CreatedAt,
            Version: backup.Version,
            UserCount: backup.Users.Count,
            MessageCount: backup.Messages.Count,
            DetectionCount: backup.DetectionResults.Count,
            FileSizeBytes: backupBytes.Length
        );
    }

    #region Export Methods

    private async Task<IEnumerable<UserBackup>> ExportUsersAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM users ORDER BY created_at";
        return await connection.QueryAsync<UserBackup>(sql);
    }

    private async Task<IEnumerable<InviteBackup>> ExportInvitesAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM invites ORDER BY created_at";
        return await connection.QueryAsync<InviteBackup>(sql);
    }

    private async Task<IEnumerable<AuditLogBackup>> ExportAuditLogsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM audit_log ORDER BY id";
        return await connection.QueryAsync<AuditLogBackup>(sql);
    }

    private async Task<IEnumerable<VerificationTokenBackup>> ExportVerificationTokensAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM verification_tokens ORDER BY id";
        return await connection.QueryAsync<VerificationTokenBackup>(sql);
    }

    private async Task<IEnumerable<MessageBackup>> ExportMessagesAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM messages ORDER BY timestamp";
        return await connection.QueryAsync<MessageBackup>(sql);
    }

    private async Task<IEnumerable<MessageEditBackup>> ExportMessageEditsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM message_edits ORDER BY id";
        return await connection.QueryAsync<MessageEditBackup>(sql);
    }

    private async Task<IEnumerable<DetectionResultBackup>> ExportDetectionResultsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM detection_results ORDER BY id";
        return await connection.QueryAsync<DetectionResultBackup>(sql);
    }

    private async Task<IEnumerable<UserActionBackup>> ExportUserActionsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM user_actions ORDER BY id";
        return await connection.QueryAsync<UserActionBackup>(sql);
    }

    private async Task<IEnumerable<StopWordBackup>> ExportStopWordsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM stop_words ORDER BY id";
        return await connection.QueryAsync<StopWordBackup>(sql);
    }

    private async Task<IEnumerable<SpamDetectionConfigBackup>> ExportSpamDetectionConfigsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM spam_detection_configs ORDER BY chat_id";
        return await connection.QueryAsync<SpamDetectionConfigBackup>(sql);
    }

    private async Task<IEnumerable<SpamCheckConfigBackup>> ExportSpamCheckConfigsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM spam_check_configs ORDER BY check_name";
        return await connection.QueryAsync<SpamCheckConfigBackup>(sql);
    }

    private async Task<IEnumerable<ChatPromptBackup>> ExportChatPromptsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM chat_prompts ORDER BY id";
        return await connection.QueryAsync<ChatPromptBackup>(sql);
    }

    private async Task<IEnumerable<ManagedChatBackup>> ExportManagedChatsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM managed_chats ORDER BY chat_id";
        return await connection.QueryAsync<ManagedChatBackup>(sql);
    }

    private async Task<IEnumerable<ChatAdminBackup>> ExportChatAdminsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM chat_admins ORDER BY id";
        return await connection.QueryAsync<ChatAdminBackup>(sql);
    }

    private async Task<IEnumerable<TelegramUserMappingBackup>> ExportTelegramUserMappingsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM telegram_user_mappings ORDER BY id";
        return await connection.QueryAsync<TelegramUserMappingBackup>(sql);
    }

    private async Task<IEnumerable<TelegramLinkTokenBackup>> ExportTelegramLinkTokensAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM telegram_link_tokens ORDER BY id";
        return await connection.QueryAsync<TelegramLinkTokenBackup>(sql);
    }

    private async Task<IEnumerable<ReportBackup>> ExportReportsAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM reports ORDER BY id";
        return await connection.QueryAsync<ReportBackup>(sql);
    }

    #endregion

    #region Restore Methods

    private async Task WipeAllTablesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _logger.LogWarning("Wiping all tables");

        // Order matters - respect foreign keys
        await connection.ExecuteAsync("TRUNCATE TABLE reports CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE telegram_link_tokens CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE telegram_user_mappings CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE chat_admins CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE managed_chats CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE chat_prompts CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE spam_check_configs CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE spam_detection_configs CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE stop_words CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE user_actions CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE detection_results CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE message_edits CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE messages CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE verification_tokens CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE audit_log CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE invites CASCADE", transaction);
        await connection.ExecuteAsync("TRUNCATE TABLE users CASCADE", transaction);
    }

    private async Task RestoreUsersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<UserBackup> users)
    {
        if (users.Count == 0) return;

        const string sql = """
            INSERT INTO users (
                id, email, normalized_email, password_hash, security_stamp,
                permission_level, invited_by, is_active, totp_secret, totp_enabled,
                totp_setup_started_at, created_at, last_login_at, status,
                modified_by, modified_at, email_verified, email_verification_token,
                email_verification_token_expires_at, password_reset_token, password_reset_token_expires_at
            ) VALUES (
                @Id, @Email, @NormalizedEmail, @PasswordHash, @SecurityStamp,
                @PermissionLevel, @InvitedBy, @IsActive, @TotpSecret, @TotpEnabled,
                @TotpSetupStartedAt, @CreatedAt, @LastLoginAt, @Status,
                @ModifiedBy, @ModifiedAt, @EmailVerified, @EmailVerificationToken,
                @EmailVerificationTokenExpiresAt, @PasswordResetToken, @PasswordResetTokenExpiresAt
            )
            """;

        await connection.ExecuteAsync(sql, users, transaction);
        _logger.LogInformation("Restored {Count} users", users.Count);
    }

    private async Task RestoreInvitesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<InviteBackup> invites)
    {
        if (invites.Count == 0) return;

        const string sql = """
            INSERT INTO invites (
                token, created_by, created_at, expires_at, used_by,
                permission_level, status, modified_at
            ) VALUES (
                @Token, @CreatedBy, @CreatedAt, @ExpiresAt, @UsedBy,
                @PermissionLevel, @Status, @ModifiedAt
            )
            """;

        await connection.ExecuteAsync(sql, invites, transaction);
        _logger.LogInformation("Restored {Count} invites", invites.Count);
    }

    private async Task RestoreAuditLogsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<AuditLogBackup> logs)
    {
        if (logs.Count == 0) return;

        const string sql = """
            INSERT INTO audit_log (id, event_type, timestamp, actor_user_id, target_user_id, value)
            VALUES (@Id, @EventType, @Timestamp, @ActorUserId, @TargetUserId, @Value)
            """;

        await connection.ExecuteAsync(sql, logs, transaction);
        _logger.LogInformation("Restored {Count} audit logs", logs.Count);
    }

    private async Task RestoreVerificationTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<VerificationTokenBackup> tokens)
    {
        if (tokens.Count == 0) return;

        const string sql = """
            INSERT INTO verification_tokens (id, user_id, token_type, token, value, expires_at, created_at, used_at)
            VALUES (@Id, @UserId, @TokenType, @Token, @Value, @ExpiresAt, @CreatedAt, @UsedAt)
            """;

        await connection.ExecuteAsync(sql, tokens, transaction);
        _logger.LogInformation("Restored {Count} verification tokens", tokens.Count);
    }

    private async Task RestoreMessagesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<MessageBackup> messages)
    {
        if (messages.Count == 0) return;

        const string sql = """
            INSERT INTO messages (
                message_id, chat_id, user_id, user_name, timestamp,
                message_text, photo_file_id, photo_file_size, photo_local_path,
                photo_thumbnail_path, urls, content_hash, chat_name, edit_date,
                deleted_at, deletion_source
            ) VALUES (
                @MessageId, @ChatId, @UserId, @UserName, @Timestamp,
                @MessageText, @PhotoFileId, @PhotoFileSize, @PhotoLocalPath,
                @PhotoThumbnailPath, @Urls, @ContentHash, @ChatName, @EditDate,
                @DeletedAt, @DeletionSource
            )
            """;

        await connection.ExecuteAsync(sql, messages, transaction);
        _logger.LogInformation("Restored {Count} messages", messages.Count);
    }

    private async Task RestoreMessageEditsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<MessageEditBackup> edits)
    {
        if (edits.Count == 0) return;

        const string sql = """
            INSERT INTO message_edits (id, message_id, edit_date, previous_text, previous_content_hash)
            VALUES (@Id, @MessageId, @EditDate, @PreviousText, @PreviousContentHash)
            """;

        await connection.ExecuteAsync(sql, edits, transaction);
        _logger.LogInformation("Restored {Count} message edits", edits.Count);
    }

    private async Task RestoreDetectionResultsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<DetectionResultBackup> results)
    {
        if (results.Count == 0) return;

        const string sql = """
            INSERT INTO detection_results (
                id, message_id, detected_at, detection_source, detection_method,
                is_spam, confidence, reason, added_by, user_id, message_text
            ) VALUES (
                @Id, @MessageId, @DetectedAt, @DetectionSource, @DetectionMethod,
                @IsSpam, @Confidence, @Reason, @AddedBy, @UserId, @MessageText
            )
            """;

        await connection.ExecuteAsync(sql, results, transaction);
        _logger.LogInformation("Restored {Count} detection results", results.Count);
    }

    private async Task RestoreUserActionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<UserActionBackup> actions)
    {
        if (actions.Count == 0) return;

        const string sql = """
            INSERT INTO user_actions (
                id, user_id, chat_ids, action_type, message_id,
                issued_by, issued_at, expires_at, reason
            ) VALUES (
                @Id, @UserId, @ChatIds, @ActionType, @MessageId,
                @IssuedBy, @IssuedAt, @ExpiresAt, @Reason
            )
            """;

        await connection.ExecuteAsync(sql, actions, transaction);
        _logger.LogInformation("Restored {Count} user actions", actions.Count);
    }

    private async Task RestoreStopWordsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<StopWordBackup> words)
    {
        if (words.Count == 0) return;

        const string sql = """
            INSERT INTO stop_words (
                id, word, word_type, added_date, source, enabled,
                added_by, detection_count, last_detected_date
            ) VALUES (
                @Id, @Word, @WordType, @AddedDate, @Source, @Enabled,
                @AddedBy, @DetectionCount, @LastDetectedDate
            )
            """;

        await connection.ExecuteAsync(sql, words, transaction);
        _logger.LogInformation("Restored {Count} stop words", words.Count);
    }

    private async Task RestoreSpamDetectionConfigsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<SpamDetectionConfigBackup> configs)
    {
        if (configs.Count == 0) return;

        const string sql = """
            INSERT INTO spam_detection_configs (
                chat_id, min_confidence_threshold, enabled_checks, custom_prompt,
                auto_ban_threshold, created_at, updated_at
            ) VALUES (
                @ChatId, @MinConfidenceThreshold, @EnabledChecks, @CustomPrompt,
                @AutoBanThreshold, @CreatedAt, @UpdatedAt
            )
            """;

        await connection.ExecuteAsync(sql, configs, transaction);
        _logger.LogInformation("Restored {Count} spam detection configs", configs.Count);
    }

    private async Task RestoreSpamCheckConfigsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<SpamCheckConfigBackup> configs)
    {
        if (configs.Count == 0) return;

        const string sql = """
            INSERT INTO spam_check_configs (check_name, enabled, confidence_weight, config_json, updated_at)
            VALUES (@CheckName, @Enabled, @ConfidenceWeight, @ConfigJson, @UpdatedAt)
            """;

        await connection.ExecuteAsync(sql, configs, transaction);
        _logger.LogInformation("Restored {Count} spam check configs", configs.Count);
    }

    private async Task RestoreChatPromptsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<ChatPromptBackup> prompts)
    {
        if (prompts.Count == 0) return;

        const string sql = """
            INSERT INTO chat_prompts (id, chat_id, prompt, created_at, updated_at)
            VALUES (@Id, @ChatId, @Prompt, @CreatedAt, @UpdatedAt)
            """;

        await connection.ExecuteAsync(sql, prompts, transaction);
        _logger.LogInformation("Restored {Count} chat prompts", prompts.Count);
    }

    private async Task RestoreManagedChatsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<ManagedChatBackup> chats)
    {
        if (chats.Count == 0) return;

        const string sql = """
            INSERT INTO managed_chats (chat_id, chat_title, chat_username, chat_type, added_at, is_active)
            VALUES (@ChatId, @ChatTitle, @ChatUsername, @ChatType, @AddedAt, @IsActive)
            """;

        await connection.ExecuteAsync(sql, chats, transaction);
        _logger.LogInformation("Restored {Count} managed chats", chats.Count);
    }

    private async Task RestoreChatAdminsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<ChatAdminBackup> admins)
    {
        if (admins.Count == 0) return;

        const string sql = """
            INSERT INTO chat_admins (id, chat_id, telegram_user_id, telegram_username, cached_at, is_active)
            VALUES (@Id, @ChatId, @TelegramUserId, @TelegramUsername, @CachedAt, @IsActive)
            """;

        await connection.ExecuteAsync(sql, admins, transaction);
        _logger.LogInformation("Restored {Count} chat admins", admins.Count);
    }

    private async Task RestoreTelegramUserMappingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<TelegramUserMappingBackup> mappings)
    {
        if (mappings.Count == 0) return;

        const string sql = """
            INSERT INTO telegram_user_mappings (id, telegram_id, telegram_username, user_id, linked_at, is_active)
            VALUES (@Id, @TelegramId, @TelegramUsername, @UserId, @LinkedAt, @IsActive)
            """;

        await connection.ExecuteAsync(sql, mappings, transaction);
        _logger.LogInformation("Restored {Count} Telegram user mappings", mappings.Count);
    }

    private async Task RestoreTelegramLinkTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<TelegramLinkTokenBackup> tokens)
    {
        if (tokens.Count == 0) return;

        const string sql = """
            INSERT INTO telegram_link_tokens (id, token, user_id, created_at, expires_at, used)
            VALUES (@Id, @Token, @UserId, @CreatedAt, @ExpiresAt, @Used)
            """;

        await connection.ExecuteAsync(sql, tokens, transaction);
        _logger.LogInformation("Restored {Count} Telegram link tokens", tokens.Count);
    }

    private async Task RestoreReportsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<ReportBackup> reports)
    {
        if (reports.Count == 0) return;

        const string sql = """
            INSERT INTO reports (
                id, message_id, chat_id, reported_user_id, reported_username,
                reported_by, reporter_username, reported_at, reason, status,
                reviewed_by, reviewed_at, action_taken, message_text,
                message_photo_file_id, chat_title
            ) VALUES (
                @Id, @MessageId, @ChatId, @ReportedUserId, @ReportedUsername,
                @ReportedBy, @ReporterUsername, @ReportedAt, @Reason, @Status,
                @ReviewedBy, @ReviewedAt, @ActionTaken, @MessageText,
                @MessagePhotoFileId, @ChatTitle
            )
            """;

        await connection.ExecuteAsync(sql, reports, transaction);
        _logger.LogInformation("Restored {Count} reports", reports.Count);
    }

    private async Task ResetSequencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _logger.LogInformation("Resetting auto-increment sequences");

        // Reset sequences to max ID + 1 for tables with SERIAL/BIGSERIAL columns
        var sequences = new[]
        {
            ("audit_log", "audit_log_id_seq", "id"),
            ("verification_tokens", "verification_tokens_id_seq", "id"),
            ("message_edits", "message_edits_id_seq", "id"),
            ("detection_results", "detection_results_id_seq", "id"),
            ("user_actions", "user_actions_id_seq", "id"),
            ("stop_words", "stop_words_id_seq", "id"),
            ("chat_prompts", "chat_prompts_id_seq", "id"),
            ("chat_admins", "chat_admins_id_seq", "id"),
            ("telegram_user_mappings", "telegram_user_mappings_id_seq", "id"),
            ("telegram_link_tokens", "telegram_link_tokens_id_seq", "id"),
            ("reports", "reports_id_seq", "id")
        };

        foreach (var (table, sequence, column) in sequences)
        {
            await connection.ExecuteAsync(
                $"SELECT setval('{sequence}', COALESCE((SELECT MAX({column}) FROM {table}), 0) + 1, false)",
                transaction: transaction);
        }
    }

    #endregion
}
