using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Security;
using TelegramGroupsAdmin.Data.Attributes;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Abstractions.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services.Backup;

public class BackupService : IBackupService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BackupService> _logger;
    private readonly IDataProtectionService _totpProtection;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly INotificationService _notificationService;
    private readonly IBackupEncryptionService _encryptionService;
    private readonly IServiceProvider _serviceProvider;
    private const string CurrentVersion = "2.0";

    public BackupService(
        NpgsqlDataSource dataSource,
        ILogger<BackupService> logger,
        IDataProtectionService totpProtection,
        IDataProtectionProvider dataProtectionProvider,
        INotificationService notificationService,
        IBackupEncryptionService encryptionService,
        IServiceProvider serviceProvider)
    {
        _dataSource = dataSource;
        _logger = logger;
        _totpProtection = totpProtection;
        _dataProtectionProvider = dataProtectionProvider;
        _notificationService = notificationService;
        _encryptionService = encryptionService;
        _serviceProvider = serviceProvider;
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
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AppVersion = "1.0.0"
            }
        };

        await using var connection = await _dataSource.OpenConnectionAsync();

        // Discover all tables dynamically from database
        var allTables = await DiscoverTablesAsync(connection);

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
                var records = await ExportTableAsync(connection, tableName, dtoType);
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
        var encryptionConfig = await GetEncryptionConfigAsync();

        if (encryptionConfig?.Enabled != true)
        {
            throw new InvalidOperationException(
                "Backup encryption is not configured. Please set up encryption before creating backups. " +
                "Navigate to Settings → Backup & Restore to enable encryption.");
        }

        // Get decrypted passphrase from database (reads from passphrase_encrypted column)
        var passphrase = await GetDecryptedPassphraseAsync();

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
    /// Discover all tables and their corresponding DTO types by reflection
    /// </summary>
    private async Task<Dictionary<string, Type>> DiscoverTablesAsync(NpgsqlConnection connection)
    {
        // Get all tables from database
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;

        var tableNames = (await connection.QueryAsync<string>(sql)).ToList();

        // Load TelegramGroupsAdmin.Data assembly and find all DTO types
        var dataAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TelegramGroupsAdmin.Data")
            ?? throw new InvalidOperationException("TelegramGroupsAdmin.Data assembly not found");

        var dtoTypes = dataAssembly.GetTypes()
            .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
            .Where(t => t.Name.EndsWith("Dto") && (t.IsClass || t.IsValueType))
            .ToList();

        _logger.LogDebug("Found {DtoCount} DTO types in Data assembly", dtoTypes.Count);

        // Match tables to DTOs by convention (snake_case table name → PascalCaseDto)
        var mapping = new Dictionary<string, Type>();
        var knownSystemTables = new HashSet<string> { "VersionInfo" }; // FluentMigrator table

        foreach (var tableName in tableNames)
        {
            // Skip known system tables
            if (knownSystemTables.Contains(tableName))
            {
                _logger.LogDebug("Skipping system table '{TableName}'", tableName);
                continue;
            }

            var dtoType = FindDtoForTable(tableName, dtoTypes);
            if (dtoType != null)
            {
                mapping[tableName] = dtoType;
                _logger.LogDebug("Mapped table '{TableName}' → {DtoType}", tableName, dtoType.Name);
            }
            else
            {
                _logger.LogWarning("No DTO found for table '{TableName}', skipping", tableName);
            }
        }

        return mapping;
    }

    /// <summary>
    /// Find DTO type for a table using naming conventions
    /// Examples: users → UserRecordDto, stop_words → StopWordDto
    /// </summary>
    private Type? FindDtoForTable(string tableName, List<Type> dtoTypes)
    {
        // Try exact match first (e.g., "users" → "UserRecordDto")
        var pascalName = ToPascalCase(tableName);

        // Try common DTO naming patterns
        var candidates = new[]
        {
            $"{pascalName}Dto",
            $"{pascalName}RecordDto",
            $"{Singularize(pascalName)}Dto",
            $"{Singularize(pascalName)}RecordDto"
        };

        return dtoTypes.FirstOrDefault(dto =>
            candidates.Any(c => dto.Name.Equals(c, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Convert snake_case to PascalCase (e.g., "stop_words" → "StopWords")
    /// </summary>
    private static string ToPascalCase(string snakeCase)
    {
        var parts = snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : ""));
    }

    /// <summary>
    /// Simple pluralization removal (users → user, stop_words → stop_word)
    /// </summary>
    private string Singularize(string plural)
    {
        if (plural.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return plural.Substring(0, plural.Length - 1);
        return plural;
    }

    private async Task<List<object>> ExportTableAsync(NpgsqlConnection connection, string tableName, Type dtoType)
    {
        // Get all properties from DTO using reflection
        var properties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // EF Core models: exclude navigation properties (virtual) and [NotMapped] properties
        // Only include properties with [Column] attribute (actual database columns)
        var columnMappings = properties
            .Where(p => !p.GetGetMethod()!.IsVirtual) // Exclude navigation properties
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null) // Exclude [NotMapped]
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() != null) // Must have [Column]
            .Select(p => new
            {
                ColumnName = p.GetCustomAttribute<ColumnAttribute>()!.Name,
                PropertyName = p.Name
            })
            .ToList();

        // Build column list with aliases to match property names (fixes Dapper reflection mapping issue)
        var columnList = string.Join(", ", columnMappings.Select(m =>
            m.ColumnName == m.PropertyName.ToLowerInvariant()
                ? m.ColumnName  // No alias needed if names match
                : $"{m.ColumnName} AS {m.PropertyName}"));  // Add alias for Dapper mapping

        var columnNames = columnMappings.Select(m => m.ColumnName).ToList();

        // Determine sort column (prefer id, created_at, or first column)
        var sortColumn = columnNames.Contains("id") ? "id" :
                        columnNames.Contains("created_at") ? "created_at" :
                        columnNames.FirstOrDefault() ?? "id";

        var sql = $"SELECT {columnList} FROM {tableName} ORDER BY {sortColumn}";

        // Use Dapper with reflection to query and deserialize to DTO type
        var queryAsync = typeof(SqlMapper).GetMethod("QueryAsync", 1,
            new[] { typeof(IDbConnection), typeof(string), typeof(object), typeof(IDbTransaction), typeof(int?), typeof(CommandType?) });
        var genericMethod = queryAsync!.MakeGenericMethod(dtoType);
        var task = (Task)genericMethod.Invoke(null, new object?[] { connection, sql, null, null, null, null })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result");
        var enumerable = (IEnumerable<object>)resultProperty!.GetValue(task)!;
        var records = enumerable.ToList();

        // Check if this DTO has any properties with [ProtectedData] attribute
        var protectedProperties = dtoType.GetProperties()
            .Where(p => p.GetCustomAttribute<ProtectedDataAttribute>() != null)
            .ToList();

        if (protectedProperties.Any())
        {
            records = await DecryptProtectedDataAsync(records, dtoType, protectedProperties);
        }

        return records;
    }

    /// <summary>
    /// Decrypt Data Protection encrypted fields for cross-machine backup
    /// </summary>
    private Task<List<object>> DecryptProtectedDataAsync(List<object> records, Type dtoType, List<PropertyInfo> protectedProperties)
    {
        var decryptedRecords = new List<object>();

        // Only process properties that are actual database columns (same filter as ExportTableAsync)
        var writableProperties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.GetGetMethod()!.IsVirtual) // Exclude navigation properties
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null) // Exclude [NotMapped]
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() != null) // Must have [Column]
            .Where(p => p.CanWrite) // Must have a setter
            .ToList();

        foreach (var record in records)
        {
            var recordCopy = Activator.CreateInstance(dtoType)!;

            foreach (var prop in writableProperties)
            {
                var value = prop.GetValue(record);

                // Check if this property has [ProtectedData] attribute and has a value
                if (protectedProperties.Contains(prop) && value is string encryptedValue && !string.IsNullOrEmpty(encryptedValue))
                {
                    try
                    {
                        // Get the purpose from the attribute
                        var protectedDataAttr = prop.GetCustomAttribute<ProtectedDataAttribute>();
                        var purpose = protectedDataAttr?.Purpose ?? "TgSpamPreFilter.TotpSecrets";

                        // Decrypt using the correct protector for this purpose
                        var protector = _dataProtectionProvider.CreateProtector(purpose);
                        var decryptedValue = protector.Unprotect(encryptedValue);
                        prop.SetValue(recordCopy, decryptedValue);
                        _logger.LogDebug("Decrypted protected property {PropertyName} with purpose {Purpose}", prop.Name, purpose);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt protected property {PropertyName}, keeping encrypted value", prop.Name);
                        prop.SetValue(recordCopy, value);
                    }
                }
                else
                {
                    // Copy non-protected properties as-is
                    prop.SetValue(recordCopy, value);
                }
            }

            decryptedRecords.Add(recordCopy);
        }

        return Task.FromResult(decryptedRecords);
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
                passphrase = await GetDecryptedPassphraseAsync();
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
            backup.Metadata.Version, DateTimeOffset.FromUnixTimeSeconds(backup.Metadata.CreatedAt), backup.Metadata.TableCount);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Discover current table/DTO mappings
            var currentTables = await DiscoverTablesAsync(connection);

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
            const string fkQuery = """
                SELECT
                    tc.table_name,
                    ccu.table_name AS foreign_table_name
                FROM information_schema.table_constraints AS tc
                JOIN information_schema.constraint_column_usage AS ccu
                    ON tc.constraint_name = ccu.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                """;

            var fkDeps = await connection.QueryAsync<(string table_name, string foreign_table_name)>(fkQuery);

            // Sort tables in dependency order (parents before children) for restore
            var tablesToRestore = backup.Data.Keys.Where(t => currentTables.ContainsKey(t)).ToList();
            _logger.LogInformation("Tables to restore: {Tables}", string.Join(", ", tablesToRestore));
            var sortedTables = TopologicalSort(tablesToRestore, fkDeps.ToList());
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
        const string fkQuery = """
            SELECT
                tc.table_name,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
                ON tc.constraint_name = ccu.constraint_name
            WHERE tc.constraint_type = 'FOREIGN KEY'
            """;

        var fkDeps = await connection.QueryAsync<(string table_name, string foreign_table_name)>(fkQuery);

        // Build dependency graph and perform topological sort
        var sortedTables = TopologicalSort(tables, fkDeps.ToList());
        sortedTables.Reverse(); // Reverse for deletion order (children before parents)

        foreach (var table in sortedTables)
        {
            await connection.ExecuteAsync($"DELETE FROM {table}", transaction: transaction);
            _logger.LogDebug("Wiped table: {TableName}", table);
        }
    }

    /// <summary>
    /// Topological sort for table restore/insertion order (parents before children)
    /// </summary>
    private List<string> TopologicalSort(List<string> tables, List<(string child, string parent)> dependencies)
    {
        var graph = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        // Initialize graph
        foreach (var table in tables)
        {
            graph[table] = new List<string>();
            inDegree[table] = 0;
        }

        // Build adjacency list (parent → children)
        // Skip self-referencing foreign keys (circular dependencies within same table)
        foreach (var (child, parent) in dependencies)
        {
            // Skip if child and parent are the same table (self-referencing FK)
            if (child == parent)
                continue;

            if (graph.ContainsKey(parent) && graph.ContainsKey(child))
            {
                graph[parent].Add(child);
                inDegree[child]++;
            }
        }

        // Kahn's algorithm for topological sort
        var queue = new Queue<string>(tables.Where(t => inDegree[t] == 0));
        var result = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var neighbor in graph[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        // Add any remaining tables (circular dependencies or isolated tables)
        var remaining = tables.Where(t => !result.Contains(t)).ToList();
        result.AddRange(remaining);

        // Result is currently in insertion order (parents before children)
        // Reverse ONLY for deletion in WipeAllTablesAsync
        // For restore, we DON'T reverse (keep insertion order)
        return result;
    }

    public async Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes)
    {
        return await GetMetadataAsync(backupBytes, passphrase: null);
    }

    /// <summary>
    /// Check if backup file is encrypted by checking for TGAENC magic header
    /// </summary>
    public async Task<bool> IsEncryptedAsync(byte[] backupBytes)
    {
        return await Task.FromResult(_encryptionService.IsEncrypted(backupBytes));
    }

    private async Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes, string? passphrase)
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
                        passphrase = await GetDecryptedPassphraseAsync();
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
    /// Retrieves backup encryption configuration from database
    /// </summary>
    private async Task<Configuration.Models.BackupEncryptionConfig?> GetEncryptionConfigAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        const string sql = """
            SELECT backup_encryption_config
            FROM configs
            WHERE chat_id IS NULL
            LIMIT 1
            """;

        var configJson = await connection.QuerySingleOrDefaultAsync<string>(sql);

        if (string.IsNullOrEmpty(configJson))
            return null;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<Configuration.Models.BackupEncryptionConfig>(configJson, jsonOptions);
    }

    /// <summary>
    /// Validates backup by attempting to decompress/decrypt and verify metadata
    /// </summary>
    private async Task<bool> ValidateBackupAsync(byte[] backupBytes, string? passphrase = null)
    {
        try
        {
            // Try to read metadata
            var metadata = await GetMetadataAsync(backupBytes, passphrase);

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
    /// Rotate backup encryption passphrase.
    /// Generates a new passphrase and queues a background job to re-encrypt all existing backups.
    /// </summary>
    public async Task<string> RotatePassphraseAsync(string backupDirectory, string userId)
    {
        _logger.LogInformation("Initiating passphrase rotation for user {UserId}", userId);

        // Generate new passphrase (6 words = 77.5 bits entropy)
        var newPassphrase = PassphraseGenerator.Generate();

        _logger.LogInformation("Generated new passphrase, queuing re-encryption job");

        // Queue background job to re-encrypt all backups
        var payload = new RotateBackupPassphrasePayload(newPassphrase, backupDirectory, userId);

        var jobId = await TickerQUtilities.ScheduleJobAsync(
            _serviceProvider,
            _logger,
            "rotate_backup_passphrase",
            payload,
            delaySeconds: 0, // Execute immediately
            retries: 1,
            retryIntervals: [60]); // Retry after 60 seconds if it fails

        if (jobId == null)
        {
            throw new InvalidOperationException("Failed to schedule passphrase rotation job");
        }

        _logger.LogInformation("Passphrase rotation job queued successfully (JobId: {JobId})", jobId);

        // Return the new passphrase so user can save it
        return newPassphrase;
    }

    // ========== Backup Encryption Config Management (Composition Pattern) ==========

    /// <summary>
    /// Sets up initial backup encryption configuration.
    /// Creates new config with CreatedAt timestamp.
    /// </summary>
    public async Task SaveEncryptionConfigAsync(string passphrase)
    {
        var encryptedPassphrase = EncryptPassphrase(passphrase);
        var config = CreateNewEncryptionConfig();
        await SaveEncryptionConfigToDatabaseAsync(config, encryptedPassphrase);
    }

    /// <summary>
    /// Updates existing backup encryption configuration with new passphrase.
    /// Updates LastRotatedAt timestamp.
    /// </summary>
    public async Task UpdateEncryptionConfigAsync(string passphrase)
    {
        var encryptedPassphrase = EncryptPassphrase(passphrase);
        var config = await UpdateExistingEncryptionConfigAsync();
        await SaveEncryptionConfigToDatabaseAsync(config, encryptedPassphrase);
    }

    /// <summary>
    /// Gets the current decrypted passphrase from database.
    /// </summary>
    public async Task<string> GetDecryptedPassphraseAsync()
    {
        await using var context = await _dataSource.OpenConnectionAsync();

        // Read encrypted passphrase from dedicated column
        var encryptedPassphrase = await context.QuerySingleOrDefaultAsync<string>(
            "SELECT passphrase_encrypted FROM configs WHERE chat_id IS NULL");

        if (string.IsNullOrEmpty(encryptedPassphrase))
        {
            throw new InvalidOperationException("No passphrase found in encryption config");
        }

        return _totpProtection.Unprotect(encryptedPassphrase);
    }

    // Private helper methods - shared implementation

    private string EncryptPassphrase(string passphrase)
    {
        return _totpProtection.Protect(passphrase);
    }

    private BackupEncryptionConfig CreateNewEncryptionConfig()
    {
        return new BackupEncryptionConfig
        {
            Enabled = true,
            Algorithm = "AES-256-GCM",
            Iterations = 100000,
            CreatedAt = DateTimeOffset.UtcNow,
            LastRotatedAt = null  // First setup
        };
    }

    private async Task<BackupEncryptionConfig> UpdateExistingEncryptionConfigAsync()
    {
        var existing = await GetEncryptionConfigAsync();
        if (existing == null)
        {
            throw new InvalidOperationException("Cannot update encryption config - no existing configuration found");
        }

        existing.LastRotatedAt = DateTimeOffset.UtcNow;

        return existing;
    }

    private async Task SaveEncryptionConfigToDatabaseAsync(BackupEncryptionConfig config, string encryptedPassphrase)
    {
        await using var context = await _dataSource.OpenConnectionAsync();

        // Load or create global config record
        var configRecord = await context.QueryFirstOrDefaultAsync<DataModels.ConfigRecordDto>(
            "SELECT * FROM configs WHERE chat_id IS NULL");

        if (configRecord == null)
        {
            configRecord = new DataModels.ConfigRecordDto
            {
                ChatId = null,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        // Save config metadata to JSONB (without passphrase)
        configRecord.BackupEncryptionConfig = JsonSerializer.Serialize(config);
        // Save encrypted passphrase to dedicated TEXT column
        configRecord.PassphraseEncrypted = encryptedPassphrase;
        configRecord.UpdatedAt = DateTimeOffset.UtcNow;

        if (configRecord.Id == 0)
        {
            // Insert
            await context.ExecuteAsync(
                @"INSERT INTO configs (chat_id, backup_encryption_config, passphrase_encrypted, created_at, updated_at)
                  VALUES (@ChatId, @BackupEncryptionConfig::jsonb, @PassphraseEncrypted, @CreatedAt, @UpdatedAt)",
                configRecord);
        }
        else
        {
            // Update
            await context.ExecuteAsync(
                @"UPDATE configs
                  SET backup_encryption_config = @BackupEncryptionConfig::jsonb,
                      passphrase_encrypted = @PassphraseEncrypted,
                      updated_at = @UpdatedAt
                  WHERE id = @Id",
                configRecord);
        }

        _logger.LogInformation("Saved backup encryption config and passphrase to database");
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
                ((System.Collections.Generic.IList<System.Text.Json.Serialization.Metadata.JsonPropertyInfo>)jsonTypeInfo.Properties).Remove(property);
            }
        }

        return jsonTypeInfo;
    }
}
