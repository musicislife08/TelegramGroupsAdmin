using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;

/// <summary>
/// Exports table data with Data Protection field decryption for cross-machine backup portability
/// </summary>
public class TableExportService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<TableExportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TableExportService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TableExportService> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Export all rows from a table using raw ADO.NET with manual JSONB deserialization
    /// </summary>
    /// <remarks>
    /// We use raw NpgsqlCommand instead of Dapper because Dapper cannot deserialize
    /// JSONB columns to complex CLR types like List&lt;T&gt;. By reading JSONB as text
    /// and deserializing manually, we have full control over type conversion.
    /// </remarks>
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
            .Select(p =>
            {
                var columnAttr = p.GetCustomAttribute<ColumnAttribute>()!;
                return new ColumnMapping
                {
                    ColumnName = columnAttr.Name!,
                    Property = p,
                    IsJsonb = string.Equals(columnAttr.TypeName, "jsonb", StringComparison.OrdinalIgnoreCase)
                };
            })
            .ToList();

        // Build column list - cast JSONB to text for manual deserialization
        var columnList = string.Join(", ", columnMappings.Select(m =>
            m.IsJsonb ? $"{m.ColumnName}::text AS {m.ColumnName}" : m.ColumnName));

        var columnNames = columnMappings.Select(m => m.ColumnName).ToList();

        // Determine sort column (prefer id, created_at, or first column)
        var sortColumn = columnNames.Contains("id") ? "id" :
                        columnNames.Contains("created_at") ? "created_at" :
                        columnNames.FirstOrDefault() ?? "id";

        var sql = $"SELECT {columnList} FROM {tableName} ORDER BY {sortColumn}";

        var records = new List<object>();

        // Use raw ADO.NET for full control over JSONB deserialization
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var record = Activator.CreateInstance(dtoType)!;

            foreach (var mapping in columnMappings)
            {
                var ordinal = reader.GetOrdinal(mapping.ColumnName);
                if (reader.IsDBNull(ordinal))
                    continue;

                object? value;
                if (mapping.IsJsonb)
                {
                    // JSONB columns come as text
                    var jsonText = reader.GetString(ordinal);

                    // If target property is string, keep as raw JSON string
                    // If target property is complex type, deserialize
                    var targetType = Nullable.GetUnderlyingType(mapping.Property.PropertyType) ?? mapping.Property.PropertyType;
                    value = targetType == typeof(string)
                        ? jsonText
                        : JsonSerializer.Deserialize(jsonText, mapping.Property.PropertyType, JsonOptions);
                }
                else
                {
                    // Read value with proper type handling
                    value = ReadTypedValue(reader, ordinal, mapping.Property.PropertyType);
                }

                if (value != null)
                    mapping.Property.SetValue(record, value);
            }

            records.Add(record);
        }

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

    private class ColumnMapping
    {
        public required string ColumnName { get; init; }
        public required PropertyInfo Property { get; init; }
        public bool IsJsonb { get; init; }
    }

    /// <summary>
    /// Read value from reader using the correct typed method to preserve type fidelity.
    /// Handles DateTimeOffset (timezone preservation) and enums (int → enum conversion).
    /// </summary>
    private static object ReadTypedValue(NpgsqlDataReader reader, int ordinal, Type propertyType)
    {
        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // DateTimeOffset - use GetFieldValue to read timestamptz with proper timezone
        if (underlyingType == typeof(DateTimeOffset))
        {
            return reader.GetFieldValue<DateTimeOffset>(ordinal);
        }

        // Enums - convert from int to enum type
        if (underlyingType.IsEnum)
        {
            var intValue = reader.GetInt32(ordinal);
            return Enum.ToObject(underlyingType, intValue);
        }

        // Default - let Npgsql return native type
        return reader.GetValue(ordinal);
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
