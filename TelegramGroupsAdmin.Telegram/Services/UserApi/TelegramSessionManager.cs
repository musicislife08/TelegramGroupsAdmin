using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Repositories;
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

        return await TryReconnectAsync(webUserId, ct);
    }

    public async Task<bool> HasAnyActiveSessionAsync(CancellationToken ct)
    {
        // Check cache first
        if (!_clients.IsEmpty)
            return true;

        // Fall back to DB
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var sessions = await sessionRepo.GetAllActiveSessionsAsync(ct);
        return sessions.Count > 0;
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
            var client = await TryReconnectAsync(session.WebUserId, ct);
            if (client is not null)
                return client;
        }

        return null;
    }

    public async Task DisconnectAsync(string webUserId, CancellationToken ct)
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
                Actor.FromWebUser(webUserId),
                value: "Disconnected by user",
                cancellationToken: ct);

            logger.LogInformation("Disconnected WTelegram session for web user {WebUserId}", webUserId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _clients)
        {
            if (_clients.TryRemove(kvp.Key, out var cached))
            {
                await DisposeClientAsync(cached);
            }
        }
    }

    private async Task<IWTelegramApiClient?> TryReconnectAsync(string webUserId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        // Check credentials gate
        if (!await configRepo.HasUserApiCredentialsAsync(ct))
            return null;

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
            _ => null
        };

        try
        {
            var apiClient = clientFactory.Create(ConfigCallback, sessionStream);
            await apiClient.LoginUserIfNeeded();

            var cached = new CachedClient(apiClient, sessionId, sessionStream);
            _clients[webUserId] = cached;

            await sessionRepo.UpdateLastUsedAsync(sessionId, ct);
            logger.LogInformation("Reconnected WTelegram session {SessionId} for web user {WebUserId}", sessionId, webUserId);
            return apiClient;
        }
        catch (TL.RpcException ex) when (IsSessionRevoked(ex))
        {
            logger.LogWarning("WTelegram session {SessionId} revoked for web user {WebUserId}: {Error}", sessionId, webUserId, ex.Message);
            await DeactivateAndAuditRevokedSessionAsync(webUserId, sessionId, ex.Message, ct);
            await sessionStream.DisposeAsync();
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reconnect WTelegram session {SessionId} for web user {WebUserId}", sessionId, webUserId);
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
