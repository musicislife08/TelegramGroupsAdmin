using Dapper;
using Npgsql;

namespace TelegramGroupsAdmin.Services.Backup.Handlers;

/// <summary>
/// Discovers database tables and maps them to DTO types using naming conventions
/// </summary>
public class TableDiscoveryService
{
    private readonly ILogger<TableDiscoveryService> _logger;

    public TableDiscoveryService(ILogger<TableDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discover all tables and their corresponding DTO types by reflection
    /// </summary>
    public async Task<Dictionary<string, Type>> DiscoverTablesAsync(NpgsqlConnection connection)
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
    internal Type? FindDtoForTable(string tableName, List<Type> dtoTypes)
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
}
