using System.Collections.Concurrent;
using System.Text.Json;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Manages Server-Sent Events (SSE) connections for real-time updates.
/// Tracks connected clients and dispatches events to them.
/// </summary>
public class SseConnectionManager
{
    private readonly ConcurrentDictionary<string, SseClient> _clients = new();
    private readonly ILogger<SseConnectionManager> _logger;

    public SseConnectionManager(ILogger<SseConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a new SSE client connection.
    /// </summary>
    public string AddClient(string userId, HttpResponse response)
    {
        var connectionId = Guid.NewGuid().ToString();
        var client = new SseClient(connectionId, userId, response);

        _clients[connectionId] = client;
        _logger.LogInformation("SSE client connected: {ConnectionId} for user {UserId}", connectionId, userId);

        return connectionId;
    }

    /// <summary>
    /// Remove a client connection.
    /// </summary>
    public void RemoveClient(string connectionId)
    {
        if (_clients.TryRemove(connectionId, out var client))
        {
            _logger.LogInformation("SSE client disconnected: {ConnectionId} for user {UserId}", connectionId, client.UserId);
        }
    }

    /// <summary>
    /// Send an event to a specific user (all their connections).
    /// </summary>
    public async Task SendToUserAsync(string userId, string eventType, object data, CancellationToken ct = default)
    {
        var userClients = _clients.Values.Where(c => c.UserId == userId).ToList();

        foreach (var client in userClients)
        {
            await SendEventAsync(client, eventType, data, ct);
        }
    }

    /// <summary>
    /// Send an event to all connected clients.
    /// </summary>
    public async Task BroadcastAsync(string eventType, object data, CancellationToken ct = default)
    {
        var tasks = _clients.Values.Select(client => SendEventAsync(client, eventType, data, ct));
        await Task.WhenAll(tasks);
    }

    private async Task SendEventAsync(SseClient client, string eventType, object data, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var message = $"event: {eventType}\ndata: {json}\n\n";
            await client.Response.WriteAsync(message, ct);
            await client.Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SSE event to client {ConnectionId}", client.ConnectionId);
            // Client likely disconnected - will be cleaned up by the endpoint
        }
    }

    /// <summary>
    /// Get count of connected clients (for diagnostics).
    /// </summary>
    public int ConnectionCount => _clients.Count;
}

/// <summary>
/// Represents a connected SSE client.
/// </summary>
public record SseClient(string ConnectionId, string UserId, HttpResponse Response);
