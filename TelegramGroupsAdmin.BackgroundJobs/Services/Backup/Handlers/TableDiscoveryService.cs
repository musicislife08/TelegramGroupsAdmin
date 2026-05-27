using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;

public class TableDiscoveryService
{
    private readonly ILogger<TableDiscoveryService> _logger;

    public TableDiscoveryService(ILogger<TableDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<Dictionary<string, Type>> DiscoverTablesAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;

        var tableNames = (await connection.QueryAsync<string>(sql)).ToList();

        var dataAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TelegramGroupsAdmin.Data")
            ?? throw new InvalidOperationException("TelegramGroupsAdmin.Data assembly not found");

        var dtoTypes = dataAssembly.GetTypes()
            .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
            .Where(t => t.Name.EndsWith("Dto") && (t.IsClass || t.IsValueType))
            .ToList();

        _logger.LogDebug("Found {DtoCount} DTO types in Data assembly", dtoTypes.Count);

        var mapping = new Dictionary<string, Type>();
        var knownSystemTables = new HashSet<string> { "VersionInfo" }; // FluentMigrator table

        foreach (var tableName in tableNames)
        {
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
                _logger.LogDebug("No DTO found for table '{TableName}', skipping", tableName);
            }
        }

        return mapping;
    }

    internal static Type? FindDtoForTable(string tableName, IEnumerable<Type> dtoTypes)
    {
        return dtoTypes.FirstOrDefault(dto =>
            dto.GetCustomAttribute<TableAttribute>()?.Name?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
    }
}
