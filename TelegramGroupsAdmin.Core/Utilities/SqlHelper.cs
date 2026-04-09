namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Safe SQL identifier quoting for dynamic DDL/DML where parameterization is not possible.
/// PostgreSQL does not support parameterizing identifiers (table/column names) — only data values.
/// </summary>
public static class SqlHelper
{
    /// <summary>
    /// Wraps a SQL identifier in double quotes with proper escaping.
    /// Embedded double quotes are escaped by doubling them per SQL standard.
    /// </summary>
    public static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
