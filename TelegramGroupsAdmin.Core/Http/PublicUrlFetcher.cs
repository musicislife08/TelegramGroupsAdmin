using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// <see cref="IPublicUrlFetcher"/> over the named <see cref="ClientName"/> HttpClient, whose
/// <see cref="SocketsHttpHandler.ConnectCallback"/> resolves every hostname itself and connects
/// only to addresses <see cref="PublicAddressPolicy"/> (or the registered
/// <see cref="IPublicUrlFetchAllowance"/>) accepts. The handler never follows redirects on its
/// own and never uses a proxy, so the address check cannot be bypassed by a redirect hop, a DNS
/// answer that changes between lookups, or a proxy doing the connecting.
/// </summary>
public sealed class PublicUrlFetcher(
    IHttpClientFactory httpClientFactory,
    ILogger<PublicUrlFetcher> logger) : IPublicUrlFetcher
{
    /// <summary>Name of the guarded HttpClient; registered by <see cref="PublicUrlFetcherServiceCollectionExtensions.AddPublicUrlFetcher"/>.</summary>
    public const string ClientName = "public-url";

    /// <summary>Redirect hops followed before the fetch is refused; each hop is re-validated.</summary>
    public const int MaxRedirects = 3;

    /// <summary>Overall deadline for a fetch, across every hop and the whole body.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long the handler waits for a single response's headers.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<PublicUrlFetchResult> FetchAsync(string url, long maxBytes, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        try
        {
            return await FetchCoreAsync(url, maxBytes, ct);
        }
        catch (PublicUrlFetchException ex)
        {
            logger.LogWarning("Refused to fetch URL {Url}: {Reason}", url, ex.Reason);
            throw;
        }
    }

    private async Task<PublicUrlFetchResult> FetchCoreAsync(string url, long maxBytes, CancellationToken ct)
    {
        var current = ValidateUrl(url);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(FetchTimeout);
        var token = deadline.Token;

        var client = httpClientFactory.CreateClient(ClientName);

        for (var hop = 0; ; hop++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (HttpRequestException ex)
            {
                throw Translate(ex);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PublicUrlFetchException(PublicUrlFetchException.TimeoutMessage, $"no response within {FetchTimeout}");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects)
                        throw new PublicUrlFetchException(
                            PublicUrlFetchException.NotAllowedMessage, $"more than {MaxRedirects} redirects");

                    var location = response.Headers.Location
                        ?? throw new PublicUrlFetchException(
                            PublicUrlFetchException.NotAllowedMessage, $"HTTP {(int)response.StatusCode} redirect without a Location header");

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    current = ValidateUrl(next.AbsoluteUri);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new PublicUrlFetchException(
                        $"The URL returned HTTP {(int)response.StatusCode}.", $"HTTP {(int)response.StatusCode} from {current}");

                var declared = response.Content.Headers.ContentLength;
                if (declared > maxBytes)
                    throw new PublicUrlFetchException(
                        PublicUrlFetchException.TooLargeMessage(maxBytes), $"Content-Length {declared} exceeds the {maxBytes}-byte cap");

                var content = await ReadCappedAsync(response, maxBytes, ct, token);
                return new PublicUrlFetchResult(content, response.Content.Headers.ContentType?.MediaType, current);
            }
        }
    }

    /// <summary>Scheme, userinfo and IP-literal checks; the connect callback is the authority for everything resolved.</summary>
    private static Uri ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new PublicUrlFetchException(PublicUrlFetchException.NotAllowedMessage, "not an absolute URL");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new PublicUrlFetchException(PublicUrlFetchException.NotAllowedMessage, $"scheme '{uri.Scheme}' is not http or https");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new PublicUrlFetchException(PublicUrlFetchException.NotAllowedMessage, "URL carries userinfo");

        if (string.IsNullOrEmpty(uri.Host))
            throw new PublicUrlFetchException(PublicUrlFetchException.NotAllowedMessage, "URL has no host");

        return uri;
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadCappedAsync(
        HttpResponseMessage response, long maxBytes, CancellationToken callerToken, CancellationToken token)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var buffered = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, token)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new PublicUrlFetchException(
                        PublicUrlFetchException.TooLargeMessage(maxBytes), $"body exceeded the {maxBytes}-byte cap while streaming");
                buffered.Write(buffer, 0, read);
            }

            return buffered.ToArray();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new PublicUrlFetchException(PublicUrlFetchException.TimeoutMessage, $"body not received within {FetchTimeout}");
        }
        catch (HttpRequestException ex)
        {
            throw Translate(ex);
        }
        catch (IOException ex)
        {
            throw new PublicUrlFetchException(PublicUrlFetchException.UnreachableMessage, $"body read failed: {ex.Message}");
        }
    }

    /// <summary>A refusal raised inside the connect callback arrives wrapped; everything else is a plain network failure.</summary>
    private static PublicUrlFetchException Translate(HttpRequestException ex)
    {
        for (Exception? inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is PublicUrlFetchException refused) return refused;
        }

        return new PublicUrlFetchException(PublicUrlFetchException.UnreachableMessage, ex.Message);
    }

    /// <summary>
    /// The connect callback for the <see cref="ClientName"/> handler: resolve the host, keep the
    /// addresses the policy or allowance accepts, and open a socket to the first that answers.
    /// Refusing here means a hostname whose DNS answer is private, and every redirect hop, is
    /// judged by the address the server would really talk to.
    /// </summary>
    internal static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, IPublicUrlFetchAllowance allowance, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var port = context.DnsEndPoint.Port;

        IPAddress[] resolved = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        if (resolved.Length == 0)
            throw new PublicUrlFetchException(PublicUrlFetchException.UnreachableMessage, $"'{host}' resolved to no addresses");

        var candidates = new List<IPAddress>(resolved.Length);
        var refusals = new List<string>();
        foreach (var address in resolved)
        {
            if (PublicAddressPolicy.IsPublic(address, out var reason) || allowance.IsAllowed(address, port))
                candidates.Add(address);
            else
                refusals.Add($"{address} is {reason}");
        }

        if (candidates.Count == 0)
            throw new PublicUrlFetchException(
                PublicUrlFetchException.NotAllowedMessage, $"'{host}:{port}' only resolves to non-public addresses: {string.Join("; ", refusals)}");

        SocketException? last = null;
        foreach (var address in candidates)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last!;
    }
}

public static class PublicUrlFetcherServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPublicUrlFetcher"/> and its guarded named HttpClient. The allowance
    /// defaults to <see cref="NoFetchAllowance"/>; a test host registers its own
    /// <see cref="IPublicUrlFetchAllowance"/> before calling this to open its loopback ports.
    /// </summary>
    public static IServiceCollection AddPublicUrlFetcher(this IServiceCollection services)
    {
        services.TryAddSingleton<IPublicUrlFetchAllowance, NoFetchAllowance>();
        services.TryAddSingleton<IPublicUrlFetcher, PublicUrlFetcher>();

        services.AddHttpClient(PublicUrlFetcher.ClientName, client =>
            {
                client.Timeout = PublicUrlFetcher.RequestTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramGroupsAdmin/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var allowance = sp.GetRequiredService<IPublicUrlFetchAllowance>();
                return new SocketsHttpHandler
                {
                    // Redirects are followed by PublicUrlFetcher so each hop is re-validated.
                    AllowAutoRedirect = false,
                    // A proxy would do the connecting and the target address would never be judged.
                    UseProxy = false,
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    ConnectCallback = (context, ct) => PublicUrlFetcher.ConnectAsync(context, allowance, ct)
                };
            });

        return services;
    }
}
