namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Outcome of reading the <c>configs.sendgrid_config</c> JSONB column. Lets a caller tell "not configured"
/// from "stored but unreadable" — the lenient <c>GetSendGridConfigAsync</c> returns a default (disabled) config
/// for both, which suits feature toggles but not decisions that must fail closed.
/// </summary>
public enum SendGridConfigReadStatus
{
    /// <summary>No config stored (no global config row, or the column is NULL / a JSON null).</summary>
    NotStored,

    /// <summary>The stored JSON parsed; <see cref="SendGridConfigReadResult.Config"/> holds it.</summary>
    Parsed,

    /// <summary>JSON is stored but could not be deserialised (corrupt or of an incompatible shape).</summary>
    Unreadable
}

/// <param name="Status">How the read went.</param>
/// <param name="Config">The config when <paramref name="Status"/> is <see cref="SendGridConfigReadStatus.Parsed"/>; otherwise null.</param>
public sealed record SendGridConfigReadResult(SendGridConfigReadStatus Status, SendGridConfig? Config)
{
    public static SendGridConfigReadResult NotStored() => new(SendGridConfigReadStatus.NotStored, null);
    public static SendGridConfigReadResult Parsed(SendGridConfig config) => new(SendGridConfigReadStatus.Parsed, config);
    public static SendGridConfigReadResult Unreadable() => new(SendGridConfigReadStatus.Unreadable, null);
}
