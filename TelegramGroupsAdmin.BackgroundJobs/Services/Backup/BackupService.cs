using System.ComponentModel.DataAnnotations.Schema;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Data.Attributes;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

public class BackupService : IBackupService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BackupService> _logger;
    private readonly IDataProtectionService _totpProtection;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly INotificationService _notificationService;
    private readonly IBackupEncryptionService _encryptionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TableDiscoveryService _tableDiscoveryService;
    private readonly TableExportService _tableExportService;
    private readonly IPassphraseManagementService _passphraseService;
    private readonly IBackupConfigurationService _configService;
    private readonly DependencyResolutionService _dependencyResolutionService;
    private readonly BackupRetentionService _retentionService;
    private const string CurrentVersion = "2.1"; // SCHEMA-3: configs.chat_id NULL → 0 migration

    public BackupService(
        NpgsqlDataSource dataSource,
        ILogger<BackupService> logger,
        IDataProtectionService totpProtection,
        IDataProtectionProvider dataProtectionProvider,
        INotificationService notificationService,
        IBackupEncryptionService encryptionService,
        IServiceProvider serviceProvider,
        TableDiscoveryService tableDiscoveryService,
        TableExportService tableExportService,
        IPassphraseManagementService passphraseService,
        IBackupConfigurationService configService,
        DependencyResolutionService dependencyResolutionService,
        BackupRetentionService retentionService)
    {
        _dataSource = dataSource;
        _logger = logger;
        _totpProtection = totpProtection;
        _dataProtectionProvider = dataProtectionProvider;
        _notificationService = notificationService;
        _encryptionService = encryptionService;
        _serviceProvider = serviceProvider;
        _tableDiscoveryService = tableDiscoveryService;
        _tableExportService = tableExportService;
        _passphraseService = passphraseService;
        _configService = configService;
        _dependencyResolutionService = dependencyResolutionService;
        _retentionService = retentionService;
    }

    public async Task<byte[]> ExportAsync()
    {
        return await ExportInternalAsync(encryptAfter: true);
    }

    /// <summary>
    /// Export backup with explicit passphrase override (for CLI usage)
    /// </summary>
    public async Task<byte[]> ExportAsync(string passphraseOverride)
    {
        if (string.IsNullOrWhiteSpace(passphraseOverride))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphraseOverride));

        _logger.LogInformation("Starting backup export with explicit passphrase");

        // Export backup normally (will be unencrypted initially)
        var unencryptedBackup = await ExportInternalAsync(encryptAfter: false);

        // Encrypt with provided passphrase
        var encryptedBackup = _encryptionService.EncryptBackup(unencryptedBackup, passphraseOverride);
        _logger.LogInformation("Encrypted backup with explicit passphrase: {Original} → {Encrypted} bytes",
            unencryptedBackup.Length, encryptedBackup.Length);

        // Validate
        if (!await ValidateBackupAsync(encryptedBackup, passphraseOverride))
        {
            throw new InvalidOperationException("Backup validation failed after encryption");
        }

        _logger.LogInformation("✅ Backup export with explicit passphrase complete: {Size} bytes", encryptedBackup.Length);
        return encryptedBackup;
    }

    /// <summary>
    /// Internal export method that can skip encryption for passphrase override scenario
    /// </summary>
    private async Task<byte[]> ExportInternalAsync(bool encryptAfter = true)
    {
        _logger.LogInformation("Starting full system backup export");

        var backup = new SystemBackup
        {
            Metadata = new BackupMetadata
            {
                Version = CurrentVersion,
                CreatedAt = DateTimeOffset.UtcNow,
                AppVersion = "1.0.0"
            }
        };

        await using var connection = await _dataSource.OpenConnectionAsync();

        // Discover all tables dynamically from database
        var allTables = await _tableDiscoveryService.DiscoverTablesAsync(connection);

        // Exclude repullable cached data (blocklist domains can be re-synced)
        var excludedTables = new HashSet<string> { "cached_blocked_domains" };
        var tables = allTables.Where(kvp => !excludedTables.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _logger.LogInformation("Discovered {TableCount} tables to backup (excluded {ExcludedCount} repullable tables)",
            tables.Count, excludedTables.Count);

        backup.Metadata.Tables = tables.Keys.ToList();
        backup.Metadata.TableCount = tables.Count;

        // Export each table using reflection
        foreach (var (tableName, dtoType) in tables)
        {
            try
            {
                _logger.LogDebug("Exporting table: {TableName}", tableName);
                var records = await _tableExportService.ExportTableAsync(connection, tableName, dtoType);
                backup.Data[tableName] = records;
                _logger.LogDebug("Exported {Count} records from {TableName}", records.Count, tableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export table {TableName}", tableName);

                // Notify Owners about backup failure (Phase 5.1)
                _ = _notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.BackupFailed,
                    subject: "Database Backup Failed",
                    message: $"Critical: Database backup failed while exporting table '{tableName}'.\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Please investigate the database connection and ensure all tables are accessible.",
                    ct: CancellationToken.None);

                throw;
            }
        }

        // Serialize to JSON (exclude [NotMapped] properties to avoid computed property errors)
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false, // Minimized JSON
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new NotMappedPropertiesIgnoringResolver()
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(backup, jsonOptions);
        _logger.LogInformation("Serialized backup to JSON: {Size} bytes", jsonBytes.Length);

        // Compress JSON with gzip
        using var jsonGzipStream = new MemoryStream();
        await using (var gzipWriter = new GZipStream(jsonGzipStream, CompressionLevel.Optimal))
        {
            await gzipWriter.WriteAsync(jsonBytes);
        }
        var compressedJson = jsonGzipStream.ToArray();
        _logger.LogInformation("Compressed database: {OriginalSize} bytes → {CompressedSize} bytes ({Ratio:P1} compression)",
            jsonBytes.Length, compressedJson.Length, 1 - (double)compressedJson.Length / jsonBytes.Length);

        if (!encryptAfter)
        {
            return compressedJson;
        }

        // Encryption is mandatory - check configuration
        var encryptionConfig = await _configService.GetEncryptionConfigAsync();

        if (encryptionConfig?.Enabled != true)
        {
            throw new InvalidOperationException(
                "Backup encryption is not configured. Please set up encryption before creating backups. " +
                "Navigate to Settings → Backup & Restore to enable encryption.");
        }

        // Get decrypted passphrase from database (reads from passphrase_encrypted column)
        var passphrase = await _passphraseService.GetDecryptedPassphraseAsync();

        // Encrypt the compressed JSON
        var finalBackup = _encryptionService.EncryptBackup(compressedJson, passphrase);
        _logger.LogInformation("Encrypted backup: {CompressedSize} bytes → {EncryptedSize} bytes",
            compressedJson.Length, finalBackup.Length);

        // Validate the encrypted backup
        if (!await ValidateBackupAsync(finalBackup, passphrase))
        {
            throw new InvalidOperationException("Backup validation failed after encryption - backup may be corrupted");
        }

        _logger.LogInformation("✅ Backup validated successfully");

        return finalBackup;
    }



    /// <summary>
    /// Encrypt Data Protection fields using the new machine's keys during restore
    /// </summary>
    private object EncryptProtectedData(object? dto, Type dtoType, List<PropertyInfo> protectedProperties)
    {
        if (dto == null)
            return dto!;

        var encryptedDto = Activator.CreateInstance(dtoType)!;

        // Only process properties that are actual database columns (same filter as ExportTableAsync)
        var writableProperties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.GetGetMethod()!.IsVirtual) // Exclude navigation properties
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null) // Exclude [NotMapped]
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() != null) // Must have [Column]
            .Where(p => p.CanWrite) // Must have a setter
            .ToList();

        foreach (var prop in writableProperties)
        {
            var value = prop.GetValue(dto);

            // Check if this property has [ProtectedData] attribute and has a value
            if (protectedProperties.Contains(prop) && value is string decryptedValue && !string.IsNullOrEmpty(decryptedValue))
            {
                try
                {
                    // Get the purpose from the attribute
                    var protectedDataAttr = prop.GetCustomAttribute<ProtectedDataAttribute>();
                    var purpose = protectedDataAttr?.Purpose ?? "TgSpamPreFilter.TotpSecrets";

                    // Encrypt using the correct protector for this purpose
                    var protector = _dataProtectionProvider.CreateProtector(purpose);
                    var encryptedValue = protector.Protect(decryptedValue);
                    prop.SetValue(encryptedDto, encryptedValue);
                    _logger.LogDebug("Encrypted protected property {PropertyName} with purpose {Purpose}", prop.Name, purpose);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to encrypt protected property {PropertyName}", prop.Name);
                    throw;
                }
            }
            else
            {
                // Copy non-protected properties as-is
                prop.SetValue(encryptedDto, value);
            }
        }

        return encryptedDto;
    }

    public async Task RestoreAsync(byte[] backupBytes)
    {
        // Auto-detect encryption and use passphrase from DB if encrypted
        string? passphrase = null;

        if (_encryptionService.IsEncrypted(backupBytes))
        {
            try
            {
                passphrase = await _passphraseService.GetDecryptedPassphraseAsync();
                _logger.LogInformation("Detected encrypted backup, using passphrase from database");
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "Backup is encrypted but no passphrase is configured in the database. " +
                    "Use RestoreAsync(backupBytes, passphrase) to provide passphrase explicitly.");
            }
        }

        await RestoreInternalAsync(backupBytes, passphrase);
    }

    public async Task RestoreAsync(byte[] backupBytes, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        _logger.LogInformation("Restoring backup with explicit passphrase");
        await RestoreInternalAsync(backupBytes, passphrase);
    }

    private async Task RestoreInternalAsync(byte[] backupBytes, string? passphrase)
    {
        _logger.LogWarning("Starting full system restore - THIS WILL WIPE ALL DATA");

        // Decrypt if encrypted
        byte[] compressedBytes;
        if (_encryptionService.IsEncrypted(backupBytes))
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new InvalidOperationException("Backup is encrypted but no passphrase provided");
            }

            compressedBytes = _encryptionService.DecryptBackup(backupBytes, passphrase);
            _logger.LogInformation("Decrypted backup: {EncryptedSize} bytes → {DecryptedSize} bytes",
                backupBytes.Length, compressedBytes.Length);
        }
        else
        {
            compressedBytes = backupBytes;
            _logger.LogInformation("Backup is not encrypted");
        }

        // Decompress gzip
        byte[] jsonBytes;
        using (var inputStream = new MemoryStream(compressedBytes))
        await using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var outputStream = new MemoryStream())
        {
            await gzipStream.CopyToAsync(outputStream);
            jsonBytes = outputStream.ToArray();
        }

        _logger.LogInformation("Decompressed backup: {Size} bytes", jsonBytes.Length);

        // Deserialize JSON
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var backup = JsonSerializer.Deserialize<SystemBackup>(jsonBytes, jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize backup");

        // Version check (allow backwards compatibility for now)
        if (backup.Metadata.Version != CurrentVersion)
        {
            _logger.LogWarning("Backup version mismatch: expected {Expected}, got {Actual}. Attempting restore anyway.",
                CurrentVersion, backup.Metadata.Version);
        }

        _logger.LogInformation("Backup metadata: version={Version}, created={CreatedAt}, tables={TableCount}",
            backup.Metadata.Version, backup.Metadata.CreatedAt, backup.Metadata.TableCount);

        // Apply version-specific migrations to backup data before restore
        ApplyBackupMigrations(backup);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Discover current table/DTO mappings
            var currentTables = await _tableDiscoveryService.DiscoverTablesAsync(connection);

            // Validate that all backup tables have DTOs (except known system tables)
            var knownSystemTables = new HashSet<string> { "VersionInfo" };
            var missingDtos = backup.Data.Keys
                .Where(t => !knownSystemTables.Contains(t))
                .Where(t => !currentTables.ContainsKey(t))
                .ToList();

            if (missingDtos.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot restore: backup contains tables without DTOs: {string.Join(", ", missingDtos)}. " +
                    "This likely means the backup is from a different schema version or DTOs are missing.");
            }

            // Wipe all tables in reverse dependency order
            _logger.LogWarning("Wiping all tables...");
            await WipeAllTablesAsync(connection, transaction, currentTables.Keys.ToList());

            // Get foreign key dependencies for proper restore order
            var fkDeps = await _dependencyResolutionService.GetForeignKeyDependenciesAsync(connection);

            // Sort tables in dependency order (parents before children) for restore
            var tablesToRestore = backup.Data.Keys.Where(t => currentTables.ContainsKey(t)).ToList();
            _logger.LogInformation("Tables to restore: {Tables}", string.Join(", ", tablesToRestore));
            var sortedTables = _dependencyResolutionService.TopologicalSort(tablesToRestore, fkDeps);
            // TopologicalSort already returns in correct order for insertion (handles circular deps)
            _logger.LogInformation("Restore order after sort: {Tables}", string.Join(", ", sortedTables));

            // Restore each table from backup in dependency order
            var totalRecordsRestored = 0;
            var tablesRestored = 0;
            foreach (var tableName in sortedTables)
            {
                if (!backup.Data.TryGetValue(tableName, out var records))
                {
                    continue; // Table not in backup
                }

                if (!currentTables.TryGetValue(tableName, out var dtoType))
                {
                    _logger.LogWarning("Table {TableName} from backup not found in current schema, skipping", tableName);
                    continue;
                }

                try
                {
                    _logger.LogInformation("Restoring table {Current}/{Total}: {TableName} ({Count} records)",
                        tablesRestored + 1, sortedTables.Count, tableName, records.Count);
                    await RestoreTableAsync(connection, transaction, tableName, dtoType, records);
                    totalRecordsRestored += records.Count;
                    tablesRestored++;

                    // Reset sequence immediately after restoring data for this table
                    // This ensures sequence is always in sync with table state, preventing duplicate key errors
                    await ResetSequenceForTableAsync(connection, tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore table {TableName}", tableName);
                    throw;
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("✅ System restore complete: {Tables} tables, {Records} total records restored",
                tablesRestored, totalRecordsRestored);

            // Disable bot to prevent dual instances (Phase 5: Backup/Restore Safety)
            // After restoring, the bot should be manually re-enabled by the user
            await DisableTelegramBotAsync(connection);
        }
        catch (Exception ex)
        {
            // Only rollback if transaction is not already completed
            if (transaction.Connection != null)
            {
                await transaction.RollbackAsync();
            }
            _logger.LogError(ex, "System restore failed - rolling back transaction");
            throw;
        }
    }

    private async Task RestoreTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tableName, Type dtoType, List<object> records)
    {
        if (records.Count == 0)
        {
            _logger.LogDebug("No records to restore for {TableName}", tableName);
            return;
        }

        // Temporarily disable FK constraints for self-referencing tables
        await connection.ExecuteAsync($"ALTER TABLE {tableName} DISABLE TRIGGER ALL", transaction);

        // Query database schema to find JSONB columns and GENERATED columns
        var schemaInfo = await connection.QueryAsync<(string column_name, string data_type, string is_generated)>(
            @"SELECT column_name, data_type, is_generated
              FROM information_schema.columns
              WHERE table_schema = 'public'
                AND table_name = @tableName",
            new { tableName },
            transaction);

        var jsonbColumnSet = new HashSet<string>(
            schemaInfo.Where(c => c.data_type == "jsonb").Select(c => c.column_name),
            StringComparer.OrdinalIgnoreCase);

        var generatedColumnSet = new HashSet<string>(
            schemaInfo.Where(c => c.is_generated == "ALWAYS").Select(c => c.column_name),
            StringComparer.OrdinalIgnoreCase);

        // Get column names from DTO type using reflection
        var properties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Get actual column names from [Column] attributes (skip navigation properties)
        // Must match the same filter used in ExportTableAsync for consistency
        var columnMappings = properties
            .Where(p => !p.GetGetMethod()!.IsVirtual) // Exclude navigation properties
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null) // Exclude [NotMapped]
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() != null) // Must have [Column]
            .Where(p => p.CanWrite) // Must have a setter
            .Select(p =>
            {
                var columnAttr = p.GetCustomAttribute<ColumnAttribute>();
                return new { PropertyName = p.Name, ColumnName = columnAttr!.Name };
            })
            .Where(m => m.ColumnName != null && !generatedColumnSet.Contains(m.ColumnName)) // Exclude GENERATED ALWAYS columns
            .ToList();

        var columnList = string.Join(", ", columnMappings.Select(m => m.ColumnName));
        // Add ::jsonb cast for JSONB columns to handle text→jsonb conversion
        var paramList = string.Join(", ", columnMappings.Select(m =>
            m.ColumnName != null && jsonbColumnSet.Contains(m.ColumnName)
                ? $"@{m.PropertyName}::jsonb"  // Cast JSONB columns
                : $"@{m.PropertyName}"));

        // Use OVERRIDING SYSTEM VALUE for tables with GENERATED ALWAYS AS IDENTITY columns
        // This allows us to insert explicit ID values during restore
        var sql = $"INSERT INTO {tableName} ({columnList}) OVERRIDING SYSTEM VALUE VALUES ({paramList})";

        // Deserialize JSON elements back to DTO type
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        // Check if this DTO has any properties with [ProtectedData] attribute
        var protectedProperties = dtoType.GetProperties()
            .Where(p => p.GetCustomAttribute<ProtectedDataAttribute>() != null)
            .ToList();

        foreach (var record in records)
        {
            // Convert object back to DTO type via JSON round-trip (handles JsonElement → DTO)
            var jsonElement = (JsonElement)record;
            var dto = JsonSerializer.Deserialize(jsonElement.GetRawText(), dtoType, jsonOptions);

            // Encrypt any [ProtectedData] properties for the new machine
            if (protectedProperties.Any())
            {
                dto = EncryptProtectedData(dto, dtoType, protectedProperties);
            }

            await connection.ExecuteAsync(sql, dto, transaction);
        }

        // Re-enable FK constraints
        await connection.ExecuteAsync($"ALTER TABLE {tableName} ENABLE TRIGGER ALL", transaction);

        _logger.LogDebug("Inserted {Count} records into {TableName}", records.Count, tableName);
    }

    /// <summary>
    /// Resets the sequence for a single table immediately after data is restored.
    /// More robust than batch reset because sequence is synced with table state before moving to next table.
    /// </summary>
    private async Task ResetSequenceForTableAsync(NpgsqlConnection connection, string tableName)
    {
        // Query for identity column and its sequence in this specific table
        const string sequenceQuery = """
            SELECT
                c.column_name,
                pg_get_serial_sequence(quote_ident(c.table_name), quote_ident(c.column_name)) as sequence_name
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
                AND c.table_name = @tableName
                AND (c.is_identity = 'YES' OR c.column_default LIKE 'nextval%')
            """;

        var identityColumns = await connection.QueryAsync<(string column_name, string sequence_name)>(
            sequenceQuery,
            new { tableName }
        );

        foreach (var (columnName, sequenceName) in identityColumns)
        {
            if (string.IsNullOrEmpty(sequenceName))
                continue;

            // Reset sequence to max(positive values) from the table
            // Ignore negative values (manual inserts like negative message_ids for training samples)
            // Sequences only generate positive integers, so we only care about max positive value
            var resetSql = $"SELECT setval('{sequenceName}', COALESCE((SELECT MAX({columnName}) FROM {tableName} WHERE {columnName} > 0), 1))";
            var newSeqValue = await connection.ExecuteScalarAsync<long>(resetSql);

            _logger.LogDebug("Reset sequence {SequenceName} for {TableName}.{ColumnName} to {NewValue}",
                sequenceName, tableName, columnName, newSeqValue);
        }
    }

    private async Task WipeAllTablesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<string> tables)
    {
        // Query foreign key dependencies to determine deletion order
        var fkDeps = await _dependencyResolutionService.GetForeignKeyDependenciesAsync(connection);

        // Build dependency graph and perform topological sort
        var sortedTables = _dependencyResolutionService.TopologicalSort(tables, fkDeps);
        sortedTables.Reverse(); // Reverse for deletion order (children before parents)

        foreach (var table in sortedTables)
        {
            await connection.ExecuteAsync($"DELETE FROM {table}", transaction: transaction);
            _logger.LogDebug("Wiped table: {TableName}", table);
        }
    }


    public async Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes)
    {
        return await GetMetadataInternalAsync(backupBytes, passphrase: null);
    }

    /// <summary>
    /// Get backup metadata with explicit passphrase (for encrypted backups during first-run)
    /// </summary>
    public async Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        return await GetMetadataInternalAsync(backupBytes, passphrase);
    }

    /// <summary>
    /// Check if backup file is encrypted by checking for TGAENC magic header
    /// </summary>
    public async Task<bool> IsEncryptedAsync(byte[] backupBytes)
    {
        return await Task.FromResult(_encryptionService.IsEncrypted(backupBytes));
    }

    private async Task<BackupMetadata> GetMetadataInternalAsync(byte[] backupBytes, string? passphrase)
    {
        try
        {
            // Decrypt if encrypted
            byte[] compressedBytes;
            if (_encryptionService.IsEncrypted(backupBytes))
            {
                if (string.IsNullOrWhiteSpace(passphrase))
                {
                    // Try to get passphrase from DB
                    try
                    {
                        passphrase = await _passphraseService.GetDecryptedPassphraseAsync();
                    }
                    catch (InvalidOperationException)
                    {
                        throw new InvalidOperationException("Backup is encrypted but no passphrase available");
                    }
                }

                compressedBytes = _encryptionService.DecryptBackup(backupBytes, passphrase);
            }
            else
            {
                compressedBytes = backupBytes;
            }

            // Decompress gzip
            using var inputStream = new MemoryStream(compressedBytes);
            await using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            await gzipStream.CopyToAsync(outputStream);
            var jsonBytes = outputStream.ToArray();

            // Deserialize only metadata (efficient - don't load full backup)
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            using var jsonDoc = JsonDocument.Parse(jsonBytes);
            var metadataElement = jsonDoc.RootElement.GetProperty("metadata");
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataElement.GetRawText(), jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize metadata");

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read backup metadata");
            throw new InvalidOperationException("Invalid backup file format", ex);
        }
    }


    /// <summary>
    /// Validates backup by attempting to decompress/decrypt and verify metadata
    /// </summary>
    private async Task<bool> ValidateBackupAsync(byte[] backupBytes, string? passphrase = null)
    {
        try
        {
            // Try to read metadata
            var metadata = await GetMetadataInternalAsync(backupBytes, passphrase);

            // Basic validation checks
            if (metadata == null)
                return false;

            if (string.IsNullOrEmpty(metadata.Version))
                return false;

            if (metadata.TableCount <= 0)
                return false;

            _logger.LogDebug("Backup validation successful: version={Version}, tables={TableCount}",
                metadata.Version, metadata.TableCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup validation failed");
            return false;
        }
    }

    /// <summary>
    /// Convert PascalCase property name to snake_case column name
    /// </summary>
    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(input[i]));
            }
            else
            {
                result.Append(input[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Disables the Telegram bot after restore to prevent dual instances
    /// Updates the telegram_bot_config JSONB column to set bot_enabled = false
    /// </summary>
    private async Task DisableTelegramBotAsync(NpgsqlConnection connection)
    {
        try
        {
            // Update the telegram_bot_config JSONB column to disable the bot
            const string updateSql = """
                UPDATE configs
                SET telegram_bot_config = '{"bot_enabled": false}'::jsonb
                WHERE chat_id = 0
                """;

            var rowsAffected = await connection.ExecuteAsync(updateSql);

            if (rowsAffected > 0)
            {
                _logger.LogWarning("🚫 Telegram bot disabled after restore to prevent dual instances. " +
                                  "Manually re-enable the bot if this is the only active instance.");
            }
            else
            {
                // If no global config exists, insert one with bot disabled
                const string insertSql = """
                    INSERT INTO configs (chat_id, telegram_bot_config)
                    VALUES (NULL, '{"bot_enabled": false}'::jsonb)
                    """;

                await connection.ExecuteAsync(insertSql);
                _logger.LogWarning("🚫 Created Telegram bot config (disabled) after restore to prevent dual instances.");
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the restore - this is a safety feature
            _logger.LogError(ex, "Failed to disable Telegram bot after restore - you may need to disable it manually");
        }
    }

    public async Task<BackupResult> CreateBackupWithRetentionAsync(
        string backupDirectory,
        RetentionConfig retentionConfig,
        CancellationToken cancellationToken = default)
    {
        // Generate backup
        var backupBytes = await ExportAsync();

        // Ensure directory exists
        Directory.CreateDirectory(backupDirectory);

        // Save backup with timestamp
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var filename = $"backup_{timestamp}.tar.gz";
        var filepath = Path.Combine(backupDirectory, filename);

        await File.WriteAllBytesAsync(filepath, backupBytes, cancellationToken);

        // Apply retention cleanup
        var backupFiles = Directory.GetFiles(backupDirectory, "backup_*.tar.gz")
            .Select(f => new BackupFileInfo
            {
                FilePath = f,
                CreatedAt = File.GetCreationTimeUtc(f),
                FileSizeBytes = new FileInfo(f).Length
            })
            .ToList();

        var toDelete = _retentionService.GetBackupsToDelete(backupFiles, retentionConfig);
        var deletedCount = 0;

        foreach (var backup in toDelete)
        {
            try
            {
                File.Delete(backup.FilePath);
                deletedCount++;
                _logger.LogDebug("Deleted old backup: {Filename}", Path.GetFileName(backup.FilePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old backup: {Filename}", Path.GetFileName(backup.FilePath));
            }
        }

        if (deletedCount > 0)
        {
            _logger.LogInformation("Deleted {Count} old backups via retention policy", deletedCount);
        }

        return new BackupResult(filename, filepath, backupBytes.Length, deletedCount);
    }

    /// <summary>
    /// Apply version-specific migrations to backup data for backward compatibility.
    /// This allows restoring old backups to newer database schemas.
    /// </summary>
    private void ApplyBackupMigrations(SystemBackup backup)
    {
        var backupVersion = backup.Metadata.Version;
        _logger.LogInformation("Checking backup version {BackupVersion} for required migrations", backupVersion);

        // Migration: v2.0 → v2.1 (SCHEMA-3: configs.chat_id NULL → 0)
        // Old backups have configs.chat_id = NULL for global config
        // New schema requires chat_id = 0 (NOT NULL with default 0)
        if (string.Compare(backupVersion, "2.1", StringComparison.Ordinal) < 0)
        {
            _logger.LogInformation("Applying SCHEMA-3 migration: configs.chat_id NULL → 0 (backup v{Version} < 2.1)", backupVersion);
            MigrateConfigsChatIdNullToZero(backup);
        }
    }

    /// <summary>
    /// SCHEMA-3 Migration: Transform configs.chat_id from NULL to 0 for global config.
    /// This handles backward compatibility for backups taken before migration 20251105161051.
    /// Handles edge case where backup has BOTH NULL and 0 rows (merges them like DB migration).
    /// </summary>
    private void MigrateConfigsChatIdNullToZero(SystemBackup backup)
    {
        if (!backup.Data.TryGetValue("configs", out var configRecords))
        {
            _logger.LogDebug("No configs table in backup, skipping SCHEMA-3 migration");
            return;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        // Step 1: Find NULL and 0 records
        Dictionary<string, JsonElement>? nullRecord = null;
        Dictionary<string, JsonElement>? zeroRecord = null;
        int nullIndex = -1;
        int zeroIndex = -1;

        for (int i = 0; i < configRecords.Count; i++)
        {
            var jsonElement = (JsonElement)configRecords[i];

            if (jsonElement.TryGetProperty("chat_id", out var chatIdProp))
            {
                if (chatIdProp.ValueKind == JsonValueKind.Null)
                {
                    nullRecord = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonElement.GetRawText(), jsonOptions);
                    nullIndex = i;
                }
                else if (chatIdProp.ValueKind == JsonValueKind.Number && chatIdProp.GetInt64() == 0)
                {
                    zeroRecord = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonElement.GetRawText(), jsonOptions);
                    zeroIndex = i;
                }
            }
        }

        // Step 2: Handle different scenarios
        if (nullRecord != null && zeroRecord != null)
        {
            // EDGE CASE: Both NULL and 0 exist - MERGE them (like DB migration did)
            _logger.LogWarning("Found BOTH chat_id=NULL and chat_id=0 in backup - merging (same as migration 20251105161051)");

            // Merge zeroRecord into nullRecord using COALESCE logic (preserve existing, fill NULLs)
            foreach (var kvp in zeroRecord)
            {
                if (!nullRecord.ContainsKey(kvp.Key) || nullRecord[kvp.Key].ValueKind == JsonValueKind.Null)
                {
                    nullRecord[kvp.Key] = kvp.Value;
                }
            }

            // Set chat_id to 0
            nullRecord["chat_id"] = JsonSerializer.SerializeToElement(0L, jsonOptions);

            // Update the NULL record in place
            var updatedJson = JsonSerializer.Serialize(nullRecord, jsonOptions);
            configRecords[nullIndex] = JsonSerializer.Deserialize<JsonElement>(updatedJson, jsonOptions);

            // Remove the duplicate 0 record
            configRecords.RemoveAt(zeroIndex);

            _logger.LogInformation("✅ SCHEMA-3 migration: Merged chat_id=NULL and chat_id=0 records, removed duplicate");
        }
        else if (nullRecord != null)
        {
            // Only NULL exists - simple conversion
            nullRecord["chat_id"] = JsonSerializer.SerializeToElement(0L, jsonOptions);

            var updatedJson = JsonSerializer.Serialize(nullRecord, jsonOptions);
            configRecords[nullIndex] = JsonSerializer.Deserialize<JsonElement>(updatedJson, jsonOptions);

            _logger.LogInformation("✅ SCHEMA-3 migration: Converted chat_id NULL → 0");
        }
        else if (zeroRecord != null)
        {
            // Only 0 exists - already correct, no action needed
            _logger.LogDebug("SCHEMA-3 migration: chat_id=0 already exists, no conversion needed");
        }
        else
        {
            // No global config in backup
            _logger.LogDebug("SCHEMA-3 migration: No global config (chat_id NULL or 0) found in backup");
        }
    }
}

/// <summary>
/// Custom JSON type info resolver that excludes properties marked with [NotMapped]
/// This prevents serialization errors from computed properties like TokenType
/// </summary>
internal class NotMappedPropertiesIgnoringResolver : System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
{
    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var jsonTypeInfo = base.GetTypeInfo(type, options);

        if (jsonTypeInfo.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
        {
            // Remove properties that have [NotMapped] attribute
            var propertiesToRemove = new List<System.Text.Json.Serialization.Metadata.JsonPropertyInfo>();

            foreach (var property in jsonTypeInfo.Properties)
            {
                var propertyInfo = type.GetProperty(property.Name);
                if (propertyInfo?.GetCustomAttribute<NotMappedAttribute>() != null)
                {
                    propertiesToRemove.Add(property);
                }
            }

            foreach (var property in propertiesToRemove)
            {
                ((IList<System.Text.Json.Serialization.Metadata.JsonPropertyInfo>)jsonTypeInfo.Properties).Remove(property);
            }
        }

        return jsonTypeInfo;
    }
}
