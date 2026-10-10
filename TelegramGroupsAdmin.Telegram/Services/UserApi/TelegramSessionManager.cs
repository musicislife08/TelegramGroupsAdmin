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
/// - Reconnect only resumes the stored session; it never logs out or starts a new login. Only a
///   revocation RPC error (AUTH_KEY_UNREGISTERED, SESSION_REVOKED, ...) or WTelegram asking for login
///   info (the session cannot resume) deactivates the session, with audit logging
/// - A reconnect that fails for any other reason (no network, a Telegram timeout, a long FLOOD_WAIT)
///   or takes longer than the reconnect timeout leaves the session active and starts a short
///   in-memory per-user backoff, so an outage is not hammered with connect attempts
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

    /// <summary>
    /// How long one reconnect (resume + peer cache warm-up) may take before it counts as failed.
    /// Settable so tests can time out without waiting.
    /// </summary>
    internal TimeSpan ReconnectTimeout { get; init; } = TimeSpan.FromSeconds(60);

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
            // Lets WTelegram check the resumed user by id; -1 accepts whichever user the session holds
            "user_id" => session.TelegramUserId?.ToString() ?? "-1",
            // With user_id supplied, WTelegram asks for these only when the stored session cannot
            // resume (wrong account, or not logged in) and it is about to start a new login.
            // A background reconnect must never send a login code: the session needs signing in again.
            "phone_number" or "verification_code" or "password" => throw new ReauthenticationRequiredException(what),
            _ => null
        };

        IWTelegramApiClient? apiClient = null;
        Task? resumeTask = null;
        try
        {
            apiClient = clientFactory.Create(ConfigCallback, sessionStream);
            var resumingClient = apiClient;

            async Task ResumeAsync()
            {
                // reloginOnFailedResume: false — on any RPC error WTelegram would otherwise log the
                // stored session out and start a new login; we want the error instead
                await resumingClient.LoginUserIfNeeded(reloginOnFailedResume: false);
                await resumingClient.WarmPeerCacheAsync();
            }

            // WTelegram calls take no CancellationToken, so bound the reconnect: it runs under the
            // per-user lock, and a hung one would block every caller for this user
            resumeTask = ResumeAsync();
            await resumeTask.WaitAsync(ReconnectTimeout, timeProvider, ct);

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
            await DeactivateAndAuditSessionAsync(webUserId, sessionId, $"Session revoked by Telegram: {ex.Message}", ct);
            await DisposeAsync(apiClient, sessionStream);
            return null;
        }
        catch (ReauthenticationRequiredException ex)
        {
            logger.LogWarning("WTelegram session {SessionId} for web user {WebUser} cannot resume: {Error}", sessionId, session.WebUser.ToLogDebug(webUserId), ex.Message);
            await DeactivateAndAuditSessionAsync(webUserId, sessionId, $"Session could not resume: {ex.Message}", ct);
            await DisposeAsync(apiClient, sessionStream);
            return null;
        }
        catch (Exception ex)
        {
            // Not a revocation (e.g. no network, a Telegram timeout, a long FLOOD_WAIT): the session
            // stays active and a later call retries once the backoff has passed.
            // A caller cancelling is not a failed connection.
            if (!ct.IsCancellationRequested)
                _lastFailedReconnect[webUserId] = timeProvider.GetUtcNow();

            if (ex is TimeoutException)
                logger.LogWarning("Reconnecting WTelegram session {SessionId} for web user {WebUser} timed out after {Timeout}", sessionId, session.WebUser.ToLogDebug(webUserId), ReconnectTimeout);
            else
                logger.LogError(ex, "Failed to reconnect WTelegram session {SessionId} for web user {WebUser}", sessionId, session.WebUser.ToLogDebug(webUserId));

            // On timeout or cancellation the resume keeps running until the client is disposed.
            // Observe it to prevent UnobservedTaskException and log how it ended.
            if (resumeTask is { IsCompleted: false })
            {
                _ = resumeTask.ContinueWith(
                    t => logger.LogDebug(t.Exception?.GetBaseException(),
                        "Abandoned WTelegram reconnect for session {SessionId} faulted after it was given up", sessionId),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            await DisposeAsync(apiClient, sessionStream);
            return null;
        }
    }

    /// <summary>
    /// Thrown from the config callback when WTelegram asks for login info during a reconnect,
    /// which happens only when the stored session cannot resume.
    /// </summary>
    private sealed class ReauthenticationRequiredException(string requested)
        : Exception($"Session needs to be signed in again (Telegram asked for {requested})");

    /// <summary>Deactivates a session that can no longer be used and audits why (<paramref name="auditValue"/>).</summary>
    private async Task DeactivateAndAuditSessionAsync(string webUserId, long sessionId, string auditValue, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        await sessionRepo.DeactivateSessionAsync(sessionId, ct);
        await auditService.LogEventAsync(
            AuditEventType.TelegramAccountDisconnected,
            Actor.FromSystem("SessionManager"),
            value: auditValue,
            cancellationToken: ct);

        logger.LogWarning("Deactivated WTelegram session {SessionId} for web user {WebUserId}: {Reason}", sessionId, webUserId, auditValue);
    }

    private static bool IsSessionRevoked(TL.RpcException ex) =>
        ex.Message is "AUTH_KEY_UNREGISTERED" or "SESSION_REVOKED" or "AUTH_KEY_INVALID" or "USER_DEACTIVATED" or "USER_DEACTIVATED_BAN";

    private static Task DisposeClientAsync(CachedClient cached) =>
        DisposeAsync(cached.ApiClient, cached.SessionStream);

    /// <summary>Best-effort disposal: the client first (stops its background thread), then the stream.</summary>
    private static async Task DisposeAsync(IWTelegramApiClient? apiClient, DatabaseSessionStream sessionStream)
    {
        try
        {
            if (apiClient is not null)
                await apiClient.DisposeAsync();
        }
        catch
        {
            // Best-effort disposal
        }

        try
        {
            await sessionStream.DisposeAsync();
        }
        catch
        {
            // Best-effort disposal
        }
    }
}
