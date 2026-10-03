namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Outcome of reading the encrypted <c>configs.api_keys</c> column. Lets a caller tell "no keys stored"
/// from "keys stored but unreadable" — the lenient <c>GetApiKeysAsync</c> collapses both to null, which is
/// fine for feature toggles but not for decisions that must fail closed.
/// </summary>
public enum ApiKeysReadStatus
{
    /// <summary>No ciphertext is stored (no global config row, or its api_keys column is NULL).</summary>
    NotStored,

    /// <summary>Ciphertext decrypted and deserialised; <see cref="ApiKeysReadResult.Keys"/> holds the keys.</summary>
    Decrypted,

    /// <summary>Ciphertext is stored but could not be decrypted or deserialised (key ring mismatch, corruption).</summary>
    Undecryptable
}

/// <param name="Status">How the read went.</param>
/// <param name="Keys">The keys when <paramref name="Status"/> is <see cref="ApiKeysReadStatus.Decrypted"/>; otherwise null.</param>
public sealed record ApiKeysReadResult(ApiKeysReadStatus Status, ApiKeysConfig? Keys)
{
    public static ApiKeysReadResult NotStored() => new(ApiKeysReadStatus.NotStored, null);
    public static ApiKeysReadResult Decrypted(ApiKeysConfig? keys) => new(ApiKeysReadStatus.Decrypted, keys);
    public static ApiKeysReadResult Undecryptable() => new(ApiKeysReadStatus.Undecryptable, null);
}
