using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using TelegramGroupsAdmin.Data.Attributes;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.Services.Backup;

public class BackupService : IBackupService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BackupService> _logger;
    private readonly ITotpProtectionService _totpProtection;
    private const string CurrentVersion = "2.0";

    public BackupService(
        NpgsqlDataSource dataSource,
        ILogger<BackupService> logger,
        ITotpProtectionService totpProtection)
    {
        _dataSource = dataSource;
        _logger = logger;
        _totpProtection = totpProtection;
    }

    public async Task<byte[]> ExportAsync()
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
        var tables = await DiscoverTablesAsync(connection);
        _logger.LogInformation("Discovered {TableCount} tables to backup", tables.Count);

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
                throw;
            }
        }

        // Serialize to JSON
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false, // Minimized JSON
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(backup, jsonOptions);
        _logger.LogInformation("Serialized backup to JSON: {Size} bytes", jsonBytes.Length);

        // Compress with gzip
        using var outputStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
        {
            await gzipStream.WriteAsync(jsonBytes);
        }

        var compressedBytes = outputStream.ToArray();
        _logger.LogInformation("Backup export complete: {OriginalSize} bytes → {CompressedSize} bytes ({Ratio:P1} compression)",
            jsonBytes.Length, compressedBytes.Length, 1 - (double)compressedBytes.Length / jsonBytes.Length);

        return compressedBytes;
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
    private string ToPascalCase(string snakeCase)
    {
        var parts = snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : ""));
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
        var columnNames = properties
            .Where(p => !p.GetGetMethod()!.IsVirtual) // Exclude navigation properties
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null) // Exclude [NotMapped]
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() != null) // Must have [Column]
            .Select(p => p.GetCustomAttribute<ColumnAttribute>()!.Name)
            .ToList();

        var columnList = string.Join(", ", columnNames);

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
                        // Decrypt using current machine's keys
                        var decryptedValue = _totpProtection.Unprotect(encryptedValue);
                        prop.SetValue(recordCopy, decryptedValue);
                        _logger.LogDebug("Decrypted protected property {PropertyName}", prop.Name);
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
                    // Encrypt using new machine's keys
                    var encryptedValue = _totpProtection.Protect(decryptedValue);
                    prop.SetValue(encryptedDto, encryptedValue);
                    _logger.LogDebug("Encrypted protected property {PropertyName}", prop.Name);
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
        _logger.LogWarning("Starting full system restore - THIS WILL WIPE ALL DATA");

        // Decompress gzip
        using var inputStream = new MemoryStream(backupBytes);
        await using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        await gzipStream.CopyToAsync(outputStream);
        var jsonBytes = outputStream.ToArray();

        _logger.LogInformation("Decompressed backup: {CompressedSize} bytes → {OriginalSize} bytes",
            backupBytes.Length, jsonBytes.Length);

        // Deserialize JSON
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var backup = JsonSerializer.Deserialize<SystemBackup>(jsonBytes, jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize backup");

        // Version check
        if (backup.Metadata.Version != CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Backup version mismatch: expected {CurrentVersion}, got {backup.Metadata.Version}. " +
                "Cannot restore - schema may be incompatible.");
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
                    _logger.LogInformation("Restoring table: {TableName} ({Count} records)", tableName, records.Count);
                    await RestoreTableAsync(connection, transaction, tableName, dtoType, records);
                    _logger.LogInformation("Restored {Count} records to {TableName}", records.Count, tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore table {TableName}", tableName);
                    throw;
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("System restore complete - all data restored successfully");

            // Reset all identity sequences to prevent duplicate key violations
            await ResetSequencesAsync(connection, sortedTables);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
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

        // Get column names from DTO type using reflection
        var properties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Get actual column names from [Column] attributes (skip navigation properties)
        // Navigation properties are typically: virtual, collections (ICollection, IEnumerable, List), or marked [NotMapped]
        var columnMappings = properties
            .Where(p =>
            {
                // Skip if marked [NotMapped]
                if (p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() != null)
                    return false;

                // Skip if it's a collection type (navigation property)
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string))
                    return false;

                // Skip if it's a virtual complex type without [Column] attribute (likely navigation property)
                var columnAttr = p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
                if (columnAttr == null && p.PropertyType.IsClass && p.PropertyType != typeof(string) && !p.PropertyType.IsValueType)
                    return false;

                return true;
            })
            .Select(p =>
            {
                var columnAttr = p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
                var columnName = columnAttr?.Name ?? ToSnakeCase(p.Name);
                return new { PropertyName = p.Name, ColumnName = columnName };
            }).ToList();

        var columnList = string.Join(", ", columnMappings.Select(m => m.ColumnName));
        var paramList = string.Join(", ", columnMappings.Select(m => $"@{m.PropertyName}"));

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

    private async Task ResetSequencesAsync(NpgsqlConnection connection, List<string> tables)
    {
        // Query for all identity columns and their sequences
        const string sequenceQuery = """
            SELECT
                c.table_name,
                c.column_name,
                pg_get_serial_sequence(quote_ident(c.table_name), quote_ident(c.column_name)) as sequence_name
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
                AND c.is_identity = 'YES'
                AND c.table_name = ANY(@tables)
            """;

        var identityColumns = await connection.QueryAsync<(string table_name, string column_name, string sequence_name)>(
            sequenceQuery,
            new { tables }
        );

        foreach (var (tableName, columnName, sequenceName) in identityColumns)
        {
            if (string.IsNullOrEmpty(sequenceName))
                continue;

            // Reset sequence to max(id) + 1 from the table
            var resetSql = $"SELECT setval('{sequenceName}', COALESCE((SELECT MAX({columnName}) FROM {tableName}), 1))";
            await connection.ExecuteScalarAsync(resetSql);

            _logger.LogDebug("Reset sequence {SequenceName} for {TableName}.{ColumnName}",
                sequenceName, tableName, columnName);
        }

        _logger.LogInformation("All identity sequences reset successfully");
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
        try
        {
            // Decompress gzip
            using var inputStream = new MemoryStream(backupBytes);
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
}
