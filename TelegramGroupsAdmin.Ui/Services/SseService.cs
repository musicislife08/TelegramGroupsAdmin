using System.Text.Json;
using TelegramGroupsAdmin.Ui.Api;

namespace TelegramGroupsAdmin.Ui.Services;

/// <summary>
/// SSE connection states for reconnection UI.
/// </summary>
public enum SseConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Client-side SSE (Server-Sent Events) service for real-time updates.
/// Connects to /api/events/stream and dispatches events to subscribers.
/// Includes automatic reconnection with exponential backoff.
/// </summary>
public class SseService : IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _cts;
    private Task? _connectionTask;

    // Reconnection settings
    private const int MaxReconnectAttempts = 5;
    private static readonly int[] ReconnectDelaysMs = [1000, 2000, 4000, 8000, 16000];

    private int _reconnectAttempt;
    private bool _autoReconnect = true;

    /// <summary>
    /// Fired when any SSE event is received.
    /// </summary>
    public event Action<SseEvent>? OnEvent;

    /// <summary>
    /// Fired when a message-related event is received.
    /// </summary>
    public event Action<SseEvent>? OnMessageEvent;

    /// <summary>
    /// Fired when a notification event is received.
    /// </summary>
    public event Action<SseEvent>? OnNotificationEvent;

    /// <summary>
    /// Fired when connection state changes (legacy - use OnConnectionStateChanged instead).
    /// </summary>
    public event Action<bool>? OnConnectionStateChanged;

    /// <summary>
    /// Fired when reconnection state changes. Provides detailed state for UI display.
    /// </summary>
    public event Action<SseConnectionState, int>? OnReconnectionStateChanged;

    public bool IsConnected { get; private set; }
    public SseConnectionState ConnectionState { get; private set; } = SseConnectionState.Disconnected;
    public int CurrentReconnectAttempt => _reconnectAttempt;

    public SseService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient Http => _httpClientFactory.CreateClient(HttpClientNames.Api);

    /// <summary>
    /// Start listening for SSE events with automatic reconnection.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (IsConnected || ConnectionState == SseConnectionState.Connecting)
        {
            return;
        }

        _autoReconnect = true;
        _reconnectAttempt = 0;
        await StartConnectionAsync();
    }

    private async Task StartConnectionAsync()
    {
        _cts = new CancellationTokenSource();
        SetConnectionState(_reconnectAttempt == 0 ? SseConnectionState.Connecting : SseConnectionState.Reconnecting);

        _connectionTask = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Routes.Events.Stream);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

                response.EnsureSuccessStatusCode();

                // Connection successful - reset reconnect counter
                _reconnectAttempt = 0;
                IsConnected = true;
                SetConnectionState(SseConnectionState.Connected);
                OnConnectionStateChanged?.Invoke(true);

                await using var stream = await response.Content.ReadAsStreamAsync(_cts.Token);
                using var reader = new StreamReader(stream);

                string? eventType = null;
                var dataLines = new List<string>();

                while (!_cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);

                    if (line == null)
                    {
                        // Stream closed - attempt reconnection
                        break;
                    }

                    if (string.IsNullOrEmpty(line))
                    {
                        // Empty line = end of event
                        if (eventType != null && dataLines.Count > 0)
                        {
                            var data = string.Join("\n", dataLines);
                            DispatchEvent(eventType, data);
                        }

                        eventType = null;
                        dataLines.Clear();
                        continue;
                    }

                    // Use AsSpan for prefix checks to avoid virtual call overhead
                    if (line.AsSpan().StartsWith("event:"))
                    {
                        eventType = line[6..].Trim();
                    }
                    else if (line.AsSpan().StartsWith("data:"))
                    {
                        dataLines.Add(line[5..].Trim());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation - don't reconnect
                return;
            }
            catch (Exception ex)
            {
                // Connection error - will attempt reconnection
                Console.WriteLine($"SSE connection error (will reconnect): {ex.Message}");
            }
            finally
            {
                IsConnected = false;
                OnConnectionStateChanged?.Invoke(false);
            }

            // Attempt reconnection if enabled
            if (_autoReconnect && !_cts?.Token.IsCancellationRequested == true)
            {
                await AttemptReconnectAsync();
            }
        }, _cts.Token);

        // Wait a moment for connection to establish
        await Task.Delay(100);
    }

    private async Task AttemptReconnectAsync()
    {
        _reconnectAttempt++;

        if (_reconnectAttempt > MaxReconnectAttempts)
        {
            SetConnectionState(SseConnectionState.Failed);
            return;
        }

        SetConnectionState(SseConnectionState.Reconnecting);

        // Exponential backoff delay
        var delayMs = ReconnectDelaysMs[Math.Min(_reconnectAttempt - 1, ReconnectDelaysMs.Length - 1)];

        try
        {
            await Task.Delay(delayMs, _cts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Start new connection attempt
        await StartConnectionAsync();
    }

    private void SetConnectionState(SseConnectionState state)
    {
        ConnectionState = state;
        OnReconnectionStateChanged?.Invoke(state, _reconnectAttempt);
    }

    /// <summary>
    /// Manually trigger a reconnection attempt after connection failed.
    /// </summary>
    public async Task RetryConnectionAsync()
    {
        if (ConnectionState != SseConnectionState.Failed)
        {
            return;
        }

        _reconnectAttempt = 0;
        await StartConnectionAsync();
    }

    private void DispatchEvent(string eventType, string data)
    {
        var sseEvent = new SseEvent(eventType, data);

        // Fire general event
        OnEvent?.Invoke(sseEvent);

        // Fire specific event handlers based on type
        if (eventType.StartsWith("message.", StringComparison.Ordinal))
        {
            OnMessageEvent?.Invoke(sseEvent);
        }
        else if (eventType.StartsWith("notification.", StringComparison.Ordinal))
        {
            OnNotificationEvent?.Invoke(sseEvent);
        }
    }

    /// <summary>
    /// Stop listening for SSE events and disable auto-reconnect.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _autoReconnect = false;

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        if (_connectionTask != null)
        {
            try
            {
                await _connectionTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            _connectionTask = null;
        }

        IsConnected = false;
        SetConnectionState(SseConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents an SSE event received from the server.
/// </summary>
public record SseEvent(string Type, string RawData)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserialize the event data to a specific type.
    /// </summary>
    public T? GetData<T>()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(RawData, s_jsonOptions);
        }
        catch (JsonException ex)
        {
            // Log to browser console for debugging SSE deserialization issues
            Console.WriteLine($"[SSE] Failed to deserialize event '{Type}' to {typeof(T).Name}: {ex.Message}");
            Console.WriteLine($"[SSE] Raw data: {RawData[..Math.Min(200, RawData.Length)]}...");
            return default;
        }
    }
}
