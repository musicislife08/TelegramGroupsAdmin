using System.Net;
using System.Text.Json;

namespace TelegramGroupsAdmin.ComponentTests.Infrastructure;

/// <summary>
/// Simple mock HTTP message handler for component tests.
/// Allows setting up expected responses for specific request paths.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<(HttpMethod Method, string Path), HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _requests = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// All requests that have been sent through this handler.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>
    /// Sets up a GET response for the specified path.
    /// </summary>
    public MockHttpMessageHandler SetupGet<T>(string path, T response)
    {
        return Setup(HttpMethod.Get, path, response);
    }

    /// <summary>
    /// Sets up a POST response for the specified path.
    /// </summary>
    public MockHttpMessageHandler SetupPost<T>(string path, T response)
    {
        return Setup(HttpMethod.Post, path, response);
    }

    /// <summary>
    /// Sets up a DELETE response for the specified path.
    /// </summary>
    public MockHttpMessageHandler SetupDelete<T>(string path, T response)
    {
        return Setup(HttpMethod.Delete, path, response);
    }

    /// <summary>
    /// Sets up a response for the specified method and path.
    /// </summary>
    public MockHttpMessageHandler Setup<T>(HttpMethod method, string path, T response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _responses[(method, NormalizePath(path))] = httpResponse;
        return this;
    }

    /// <summary>
    /// Sets up an error response for the specified method and path.
    /// </summary>
    public MockHttpMessageHandler SetupError(HttpMethod method, string path, HttpStatusCode statusCode, string? errorMessage = null)
    {
        var httpResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(errorMessage ?? statusCode.ToString(), System.Text.Encoding.UTF8, "text/plain")
        };
        _responses[(method, NormalizePath(path))] = httpResponse;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        var path = NormalizePath(request.RequestUri?.PathAndQuery ?? "");
        var key = (request.Method, path);

        if (_responses.TryGetValue(key, out var response))
        {
            return Task.FromResult(response);
        }

        // Return 404 for unmatched requests
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock configured for {request.Method} {path}")
        });
    }

    private static string NormalizePath(string path)
    {
        // Remove query string for matching (can be enhanced if needed)
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }
        return path.TrimStart('/').ToLowerInvariant();
    }
}
