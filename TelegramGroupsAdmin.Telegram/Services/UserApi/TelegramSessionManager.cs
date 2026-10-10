using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Singleton service managing warm WTelegram client connections.
///
/// Design:
/// - ConcurrentDictionary cache keyed by web user ID (GUID string)
/// - Lazy reconnection: clients created on first GetClientAsync, reused after
/// - DatabaseSessionStream bridges WTelegram's Stream API to ITelegramSessionRepository
/// - A cached client found disconnected is stale, not revoked: it is disposed, dropped from the
///   cache and rebuilt from the stored session. Dropped connections happen routinely (Telegram
///   resets idle connections, home internet outages) and say nothing about the session itself.
/// - Only a revocation RPC error during reconnect (AUTH_KEY_UNREGISTERED, SESSION_REVOKED, ...)
///   deactivates the session, with audit logging
/// - A reconnect that fails for any other reason (e.g. no network) leaves the session active and
///   starts a short in-memory per-user backoff, so an outage is not hammered with connect attempts
/// - IAsyncDisposable: disposes all cached clients on app shutdown
/// </summary>
public sealed class TelegramSessionManager(
    IServiceScopeFactory scopeFactory,
    IWTelegramClientFactory clientFactory,
    TimeProvider timeProvider,
    ILogger<TelegramSessionManager> logger) : ITelegramSessionManager
{
    /// <summary>
    /// After a reconnect fails for a reason other than revocation, further attempts for that web user
    /// wait this long. Callers like the profile rescan job ask once per Telegram user, so without it a
    /// network outage would turn every scan into a fresh connect attempt.
    /// </summary>
    internal static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CachedClient> _clients = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _reconnectLocks = new();

    // Transient connection state, deliberately not persisted: when the last failed reconnect happened
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFailedReconnect = new();

    public int ActiveClientCount => _clients.Count;

    private sealed record CachedClient(IWTelegramApiClient ApiClient, long SessionId, DatabaseSessionStream SessionStream);

    public async Task<IWTelegramApiClient?> GetClientAsync(string webUserId, CancellationToken ct)
    {
        if (_clients.TryGetValue(webUserId, out var existing) && !existing.ApiClient.Disconnected)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
            await sessionRepo.UpdateLastUsedAsync(existing.SessionId, ct);
            return existing.ApiClient;
        }

        // Nothing cached, or the cached client lost its connection: rebuild from the stored session
        return await ReconnectWithLockAsync(webUserId, ct);
    }

    public async Task<bool> HasAnyActiveSessionAsync(CancellationToken ct)
    {
        // Check cache first — only count non-disconnected clients
        foreach (var kvp in _clients)
        {
            if (!kvp.Value.ApiClient.Disconnected)
                return true;
        }

        // Fall back to DB — lightweight existence check, no row materialization or decryption
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        return await sessionRepo.AnyActiveSessionExistsAsync(ct);
    }

    public async Task<IWTelegramApiClient?> GetAnyClientAsync(CancellationToken ct)
    {
        // Try cached clients first
        foreach (var kvp in _clients)
        {
            if (!kvp.Value.ApiClient.Disconnected)
                return kvp.Value.ApiClient;
        }

        // Try reconnecting any active session from DB
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var sessions = await sessionRepo.GetAllActiveSessionsAsync(ct);

        foreach (var session in sessions)
        {
            var client = await ReconnectWithLockAsync(session.WebUserId, ct);
            if (client is not null)
                return client;
        }

        return null;
    }

    public async Task<IWTelegramApiClient?> GetClientForChatAsync(long botApiChatId, CancellationToken ct)
    {
        // Fast path: check in-memory peer caches — prefer a verified match
        IWTelegramApiClient? fallback = null;
        foreach (var kvp in _clients)
        {
            if (kvp.Value.ApiClient.Disconnected) continue;

            if (kvp.Value.ApiClient.GetInputPeerForChat(botApiChatId) is not null)
                return kvp.Value.ApiClient; // Verified match

            fallback ??= kvp.Value.ApiClient; // Remember first available
        }

        // DB path: always query even when a fallback exists in _clients. This discovers
        // newly connected sessions not yet in the cache (e.g., user added a second Telegram
        // account via Settings UI). The query is cheap (tiny table, indexed) and
        // ReconnectWithLockAsync short-circuits for already-cached sessions.
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var sessions = await sessionRepo.GetAllActiveSessionsAsync(ct, preferChatId: botApiChatId);

        foreach (var session in sessions)
        {
            var client = await ReconnectWithLockAsync(session.WebUserId, ct);
            if (client is not null)
                return client;
        }

        return fallback; // May be null if no sessions at all
    }

    /// <summary>
    /// Per-user lock around TryReconnectAsync to prevent concurrent reconnects
    /// creating duplicate clients (where the first is orphaned and never disposed).
    /// A disconnected client still in the cache is disposed and dropped here before reconnecting.
    /// </summary>
    private async Task<IWTelegramApiClient?> ReconnectWithLockAsync(string webUserId, CancellationToken ct)
    {
        var userLock = _reconnectLocks.GetOrAdd(webUserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            if (_clients.TryGetValue(webUserId, out var cached))
            {
                // Another caller may have reconnected while we waited
                if (!cached.ApiClient.Disconnected)
                    return cached.ApiClient;

                // Remove only this exact entry, so a client someone else just cached is never disposed
                if (_clients.TryRemove(new KeyValuePair<string, CachedClient>(webUserId, cached)))
                {
                    logger.LogInformation(
                        "WTelegram client for web user {WebUserId} was disconnected; reconnecting from stored session",
                        webUserId);
                    await DisposeClientAsync(cached);
                }
            }

            if (_lastFailedReconnect.TryGetValue(webUserId, out var failedAt)
                && timeProvider.GetUtcNow() - failedAt < ReconnectBackoff)
            {
                logger.LogDebug(
                    "Skipping WTelegram reconnect for web user {WebUserId}: last attempt failed at {FailedAt:u}",
                    webUserId, failedAt);
                return null;
            }

            return await TryReconnectAsync(webUserId, ct);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task DisconnectAsync(string webUserId, Actor executor, CancellationToken ct)
    {
        _lastFailedReconnect.TryRemove(webUserId, out _);

        if (_clients.TryRemove(webUserId, out var cached))
        {
            await DisposeClientAsync(cached);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var session = await sessionRepo.GetActiveSessionAsync(webUserId, ct);

        if (session is not null)
        {
            await sessionRepo.DeactivateSessionAsync(session.Id, ct);

            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await auditService.LogEventAsync(
                AuditEventType.TelegramAccountDisconnected,
                executor,
                value: "Disconnected by user",
                cancellationToken: ct);

            logger.LogInformation("Disconnected WTelegram session for web user {WebUser}", session.WebUser.ToLogInfo(webUserId));
        }
    }

    /// <summary>
    /// Best-effort cleanup at app shutdown. Iterating ConcurrentDictionary while removing
    /// could theoretically miss entries added concurrently, but this only runs during
    /// IHost shutdown when no new requests are being accepted — safe for our singleton lifecycle.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _clients)
        {
            if (_clients.TryRemove(kvp.Key, out var cached))
                await DisposeClientAsync(cached);
        }

        foreach (var kvp in _reconnectLocks)
        {
            if (_reconnectLocks.TryRemove(kvp.Key, out var semaphore))
                semaphore.Dispose();
        }
    }

    private async Task<IWTelegramApiClient?> TryReconnectAsync(string webUserId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        var session = await sessionRepo.GetActiveSessionAsync(webUserId, ct);
        if (session is null)
            return null;

        var config = await configRepo.GetUserApiConfigAsync(ct);
        var apiHash = await configRepo.GetUserApiHashAsync(ct);
        if (config.ApiId == 0 || string.IsNullOrEmpty(apiHash))
            return null;

        // Create save callback that opens a fresh scope per save (fire-and-forget safe)
        var sessionId = session.Id;
        var saveCallback = async (byte[] data) =>
        {
            await using var saveScope = scopeFactory.CreateAsyncScope();
            var repo = saveScope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
            await repo.UpdateSessionDataAsync(sessionId, data, CancellationToken.None);
        };

        var sessionStream = new DatabaseSessionStream(session.SessionData, saveCallback, logger);

        string? ConfigCallback(string what) => what switch
        {
            "api_id" => config.ApiId.ToString(),
            "api_hash" => apiHash,
            "phone_number" => session.PhoneNumber,
            // If WTelegram asks for verification_code or password, the session is truly expired
            "verification_code" or "password" =>
                throw new InvalidOperationException("Session requires re-authentication"),
            _ => null
        };

        IWTelegramApiClient? apiClient = null;
        try
        {
            apiClient = clientFactory.Create(ConfigCallback, sessionStream);
            await apiClient.LoginUserIfNeeded();
            await apiClient.WarmPeerCacheAsync();

            // Persist accessible chats for DB-level session routing
            var chatIds = apiClient.GetBotApiChatIds();
            if (chatIds.Count > 0)
                await sessionRepo.UpdateMemberChatsAsync(sessionId, JsonSerializer.Serialize(chatIds), ct);

            var cached = new CachedClient(apiClient, sessionId, sessionStream);
            _clients[webUserId] = cached;

            _lastFailedReconnect.TryRemove(webUserId, out _);

            await sessionRepo.UpdateLastUsedAsync(sessionId, ct);
            logger.LogInformation("Reconnected WTelegram session {SessionId} for web user {WebUser}", sessionId, session.WebUser.ToLogInfo(webUserId));
            return apiClient;
        }
        catch (TL.RpcException ex) when (IsSessionRevoked(ex))
        {
            logger.LogWarning("WTelegram session {SessionId} revoked for web user {WebUser}: {Error}", sessionId, session.WebUser.ToLogDebug(webUserId), ex.Message);
            await DeactivateAndAuditRevokedSessionAsync(webUserId, sessionId, ex.Message, ct);
            // Dispose client first (stops background thread), then stream
            if (apiClient != null) await apiClient.DisposeAsync();
            await sessionStream.DisposeAsync();
            return null;
        }
        catch (Exception ex)
        {
            // Not a revocation (e.g. no network): the session stays active and a later call retries
            // once the backoff has passed. A caller cancelling is not a failed connection.
            if (!ct.IsCancellationRequested)
                _lastFailedReconnect[webUserId] = timeProvider.GetUtcNow();
            logger.LogError(ex, "Failed to reconnect WTelegram session {SessionId} for web user {WebUser}", sessionId, session.WebUser.ToLogDebug(webUserId));
            // Dispose client first (stops background thread), then stream
            if (apiClient != null) await apiClient.DisposeAsync();
            await sessionStream.DisposeAsync();
            return null;
        }
    }

    private async Task DeactivateAndAuditRevokedSessionAsync(string webUserId, long sessionId, string reason, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        await sessionRepo.DeactivateSessionAsync(sessionId, ct);
        await auditService.LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Actor.FromSystem("SessionManager"),
            value: $"Session revoked by Telegram: {reason}",
            cancellationToken: ct);

        logger.LogWarning("Deactivated revoked WTelegram session {SessionId} for web user {WebUserId}: {Reason}", sessionId, webUserId, reason);
    }

    private static bool IsSessionRevoked(TL.RpcException ex) =>
        ex.Message is "AUTH_KEY_UNREGISTERED" or "SESSION_REVOKED" or "AUTH_KEY_INVALID" or "USER_DEACTIVATED" or "USER_DEACTIVATED_BAN";

    private static async Task DisposeClientAsync(CachedClient cached)
    {
        try
        {
            await cached.ApiClient.DisposeAsync();
        }
        catch
        {
            // Best-effort disposal
        }

        try
        {
            await cached.SessionStream.DisposeAsync();
        }
        catch
        {
            // Best-effort disposal
        }
    }
}
