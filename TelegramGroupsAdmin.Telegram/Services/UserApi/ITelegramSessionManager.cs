using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Manages warm WTelegram client connections backed by database-persisted sessions.
/// Singleton service — holds a ConcurrentDictionary cache of active clients keyed by web user ID.
/// Clients reconnect lazily on first access using encrypted session data from the database.
/// </summary>
public interface ITelegramSessionManager : IAsyncDisposable
{
    /// <summary>Get an active WTelegram API client for a web user. Returns null if no session exists
    /// or if API credentials are not configured.</summary>
    Task<IWTelegramApiClient?> GetClientAsync(string webUserId, CancellationToken ct);

    /// <summary>Check if any active session exists (for feature availability checks).</summary>
    Task<bool> HasAnyActiveSessionAsync(CancellationToken ct);

    /// <summary>Get any available client (for system-level lookups like profile scan).
    /// Returns the first active session's client — safe for read-only operations.</summary>
    Task<IWTelegramApiClient?> GetAnyClientAsync(CancellationToken ct);

    /// <summary>Disconnect and cleanup a session. Deactivates in DB, disposes client, removes from cache.</summary>
    Task DisconnectAsync(string webUserId, Actor executor, CancellationToken ct);
}
