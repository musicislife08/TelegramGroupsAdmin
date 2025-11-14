using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;

/// <summary>
/// Handles foreign key dependency resolution and topological sorting for backup/restore operations.
/// Ensures tables are restored in correct order (parents before children) and deleted in reverse order.
/// </summary>
public class DependencyResolutionService
{
    private readonly ILogger<DependencyResolutionService> _logger;

    public DependencyResolutionService(ILogger<DependencyResolutionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Query foreign key dependencies from PostgreSQL information_schema.
    /// Returns list of (child_table, parent_table) relationships.
    /// </summary>
    public async Task<List<(string childTable, string parentTable)>> GetForeignKeyDependenciesAsync(NpgsqlConnection connection)
    {
        const string fkQuery = """
            SELECT
                tc.table_name,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
                ON tc.constraint_name = ccu.constraint_name
            WHERE tc.constraint_type = 'FOREIGN KEY'
            """;

        var result = await connection.QueryAsync<(string table_name, string foreign_table_name)>(fkQuery);
        return result.ToList();
    }

    /// <summary>
    /// Topological sort using Kahn's algorithm for table restore/insertion order.
    /// Returns tables in dependency order (parents before children).
    /// Handles self-referencing foreign keys and circular dependencies.
    /// </summary>
    /// <param name="tables">List of table names to sort</param>
    /// <param name="dependencies">List of (child, parent) FK relationships</param>
    /// <returns>Sorted table list (parents before children)</returns>
    public List<string> TopologicalSort(List<string> tables, List<(string child, string parent)> dependencies)
    {
        var graph = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        // Initialize graph
        foreach (var table in tables)
        {
            graph[table] = [];
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

        // Result is in insertion order (parents before children)
        // For deletion, caller should reverse this list
        return result;
    }
}
