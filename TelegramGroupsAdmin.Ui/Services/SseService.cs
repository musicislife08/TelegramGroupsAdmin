using System.Text.Json;
using TelegramGroupsAdmin.Ui.Api;

namespace TelegramGroupsAdmin.Ui.Services;

/// <summary>
/// Client-side SSE (Server-Sent Events) service for real-time updates.
/// Connects to /api/events/stream and dispatches events to subscribers.
/// </summary>
public class SseService : IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _cts;
    private Task? _connectionTask;

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
    /// Fired when connection state changes.
    /// </summary>
    public event Action<bool>? OnConnectionStateChanged;

    public bool IsConnected { get; private set; }

    public SseService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient Http => _httpClientFactory.CreateClient(HttpClientNames.Api);

    /// <summary>
    /// Start listening for SSE events.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            return;
        }

        _cts = new CancellationTokenSource();

        _connectionTask = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Routes.Events.Stream);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

                response.EnsureSuccessStatusCode();

                IsConnected = true;
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
                        // Stream closed
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

                    if (line.StartsWith("event:", StringComparison.Ordinal))
                    {
                        eventType = line[6..].Trim();
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        dataLines.Add(line[5..].Trim());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception)
            {
                // Connection error - could implement reconnection logic here
            }
            finally
            {
                IsConnected = false;
                OnConnectionStateChanged?.Invoke(false);
            }
        }, _cts.Token);

        // Wait a moment for connection to establish
        await Task.Delay(100);
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
    /// Stop listening for SSE events.
    /// </summary>
    public async Task DisconnectAsync()
    {
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
    /// <summary>
    /// Deserialize the event data to a specific type.
    /// </summary>
    public T? GetData<T>()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(RawData, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return default;
        }
    }
}
