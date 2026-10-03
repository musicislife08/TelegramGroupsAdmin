namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>Which session template a test database is cloned from.</summary>
public enum GoldenTemplate
{
    /// <summary>Migrated schema, no rows.</summary>
    Empty,

    /// <summary>Migrated schema plus the full canonical dataset.</summary>
    Golden,
}
