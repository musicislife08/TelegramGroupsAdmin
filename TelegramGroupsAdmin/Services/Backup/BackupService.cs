using System.Data;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace TelegramGroupsAdmin.Services.Backup;

public class BackupService : IBackupService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BackupService> _logger;
    private const string CurrentVersion = "2.0";

    public BackupService(NpgsqlDataSource dataSource, ILogger<BackupService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
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

        foreach (var tableName in tableNames)
        {
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
        var parts = snakeCase.Split('_');
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
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

        // DTOs use snake_case property names that match database columns
        var columnNames = properties.Select(p => p.Name).ToList();

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

        return enumerable.ToList();
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

            // Wipe all tables in reverse dependency order
            _logger.LogWarning("Wiping all tables...");
            await WipeAllTablesAsync(connection, transaction, currentTables.Keys.ToList());

            // Restore each table from backup
            foreach (var (tableName, records) in backup.Data)
            {
                if (!currentTables.TryGetValue(tableName, out var dtoType))
                {
                    _logger.LogWarning("Table {TableName} from backup not found in current schema, skipping", tableName);
                    continue;
                }

                try
                {
                    _logger.LogDebug("Restoring table: {TableName} ({Count} records)", tableName, records.Count);
                    await RestoreTableAsync(connection, transaction, tableName, dtoType, records);
                    _logger.LogDebug("Restored {Count} records to {TableName}", records.Count, tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore table {TableName}", tableName);
                    throw;
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("System restore complete - all data restored successfully");
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

        // Get column names from DTO type using reflection
        var properties = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columnNames = properties.Select(p => p.Name).ToList();
        var columnList = string.Join(", ", columnNames);
        var paramList = string.Join(", ", columnNames.Select(c => $"@{c}"));

        var sql = $"INSERT INTO {tableName} ({columnList}) VALUES ({paramList})";

        // Deserialize JSON elements back to DTO type
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        foreach (var record in records)
        {
            // Convert object back to DTO type via JSON round-trip (handles JsonElement → DTO)
            var jsonElement = (JsonElement)record;
            var dto = JsonSerializer.Deserialize(jsonElement.GetRawText(), dtoType, jsonOptions);

            await connection.ExecuteAsync(sql, dto, transaction);
        }

        _logger.LogDebug("Inserted {Count} records into {TableName}", records.Count, tableName);
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

        // Build dependency graph and perform topological sort (reverse for deletion order)
        var sortedTables = TopologicalSort(tables, fkDeps.ToList());

        foreach (var table in sortedTables)
        {
            await connection.ExecuteAsync($"DELETE FROM {table}", transaction: transaction);
            _logger.LogDebug("Wiped table: {TableName}", table);
        }
    }

    /// <summary>
    /// Topological sort for table deletion order (children before parents)
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
        foreach (var (child, parent) in dependencies)
        {
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

        // Reverse for deletion order (children first, parents last)
        result.Reverse();
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
}
