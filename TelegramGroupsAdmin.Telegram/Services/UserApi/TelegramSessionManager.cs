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
/// - Revoked sessions (AUTH_KEY_UNREGISTERED, SESSION_REVOKED) auto-cleanup with audit logging
/// - IAsyncDisposable: disposes all cached clients on app shutdown
/// </summary>
public sealed class TelegramSessionManager(
    IServiceScopeFactory scopeFactory,
    IWTelegramClientFactory clientFactory,
    ILogger<TelegramSessionManager> logger) : ITelegramSessionManager
{
    private readonly ConcurrentDictionary<string, CachedClient> _clients = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _reconnectLocks = new();

    private sealed record CachedClient(IWTelegramApiClient ApiClient, long SessionId, DatabaseSessionStream SessionStream);

    public async Task<IWTelegramApiClient?> GetClientAsync(string webUserId, CancellationToken ct)
    {
        if (_clients.TryGetValue(webUserId, out var existing))
        {
            if (!existing.ApiClient.Disconnected)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
                await sessionRepo.UpdateLastUsedAsync(existing.SessionId, ct);
                return existing.ApiClient;
            }

            // Client disconnected server-side — clean up
            await HandleRevokedSessionAsync(webUserId, existing, "Client found disconnected on access", ct);
            return null;
        }

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
    /// </summary>
    private async Task<IWTelegramApiClient?> ReconnectWithLockAsync(string webUserId, CancellationToken ct)
    {
        var userLock = _reconnectLocks.GetOrAdd(webUserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            // Re-check cache — another thread may have populated it while we waited
            if (_clients.TryGetValue(webUserId, out var justAdded) && !justAdded.ApiClient.Disconnected)
                return justAdded.ApiClient;

            return await TryReconnectAsync(webUserId, ct);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task DisconnectAsync(string webUserId, Actor executor, CancellationToken ct)
    {
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
            logger.LogError(ex, "Failed to reconnect WTelegram session {SessionId} for web user {WebUser}", sessionId, session.WebUser.ToLogDebug(webUserId));
            // Dispose client first (stops background thread), then stream
            if (apiClient != null) await apiClient.DisposeAsync();
            await sessionStream.DisposeAsync();
            return null;
        }
    }

    private async Task HandleRevokedSessionAsync(string webUserId, CachedClient cached, string reason, CancellationToken ct)
    {
        _clients.TryRemove(webUserId, out _);
        await DisposeClientAsync(cached);
        await DeactivateAndAuditRevokedSessionAsync(webUserId, cached.SessionId, reason, ct);
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
