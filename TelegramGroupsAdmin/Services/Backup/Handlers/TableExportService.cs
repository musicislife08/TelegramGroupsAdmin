using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.Services.Backup.Handlers;

/// <summary>
/// Exports table data with Data Protection field decryption for cross-machine backup portability
/// </summary>
public class TableExportService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<TableExportService> _logger;

    public TableExportService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TableExportService> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Export all rows from a table using reflection-based Dapper query
    /// </summary>
    public async Task<List<object>> ExportTableAsync(NpgsqlConnection connection, string tableName, Type dtoType)
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
            [typeof(IDbConnection), typeof(string), typeof(object), typeof(IDbTransaction), typeof(int?), typeof(CommandType?)
            ]);
        var genericMethod = queryAsync!.MakeGenericMethod(dtoType);
        var task = (Task)genericMethod.Invoke(null, [connection, sql, null, null, null, null])!;
        await task;

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
}
