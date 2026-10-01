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

    /// <summary>Default overall deadline for a fetch, across every hop and the whole body.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long the handler waits for a single response's headers.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public Task<PublicUrlFetchResult> FetchAsync(string url, long maxBytes, CancellationToken ct = default)
        => FetchAsync(url, new PublicUrlFetchOptions { MaxBytes = maxBytes }, ct);

    public async Task<PublicUrlFetchResult> FetchAsync(string url, PublicUrlFetchOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBytes);

        try
        {
            return await FetchCoreAsync(url, options, ct);
        }
        catch (PublicUrlFetchException ex)
        {
            // A policy refusal is a security signal worth a warning; a dead link, a slow server or
            // an oversized file is the caller's business and is reported through the exception.
            if (ex.Kind == PublicUrlFetchFailure.NotAllowed)
                logger.LogWarning("Refused to fetch URL {Url}: {Reason}", url, ex.Reason);
            else
                logger.LogDebug("Could not fetch URL {Url}: {Reason}", url, ex.Reason);
            throw;
        }
    }

    private async Task<PublicUrlFetchResult> FetchCoreAsync(string url, PublicUrlFetchOptions options, CancellationToken ct)
    {
        var current = ValidateUrl(url);
        var timeout = options.Timeout ?? FetchTimeout;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
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
                throw new PublicUrlFetchException(
                    PublicUrlFetchFailure.Timeout, PublicUrlFetchException.TimeoutMessage, $"no response within {timeout}");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects)
                        throw new PublicUrlFetchException(
                            PublicUrlFetchFailure.NotAllowed, PublicUrlFetchException.NotAllowedMessage, $"more than {MaxRedirects} redirects");

                    var location = response.Headers.Location
                        ?? throw new PublicUrlFetchException(
                            PublicUrlFetchFailure.NotAllowed, PublicUrlFetchException.NotAllowedMessage,
                            $"HTTP {(int)response.StatusCode} redirect without a Location header");

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    current = ValidateUrl(next.AbsoluteUri);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new PublicUrlFetchException(
                        PublicUrlFetchFailure.HttpStatus, $"The URL returned HTTP {(int)response.StatusCode}.",
                        $"HTTP {(int)response.StatusCode} from {current}");

                var declared = response.Content.Headers.ContentLength;
                if (declared > options.MaxBytes && !options.TruncateAtCap)
                    throw new PublicUrlFetchException(
                        PublicUrlFetchFailure.TooLarge, PublicUrlFetchException.TooLargeMessage(options.MaxBytes),
                        $"Content-Length {declared} exceeds the {options.MaxBytes}-byte cap");

                var (content, truncated) = await ReadCappedAsync(response, options, timeout, ct, token);
                var contentType = response.Content.Headers.ContentType;
                return new PublicUrlFetchResult(content, contentType?.MediaType, current, truncated, contentType?.CharSet);
            }
        }
    }

    /// <summary>Scheme, userinfo and IP-literal checks; the connect callback is the authority for everything resolved.</summary>
    private static Uri ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw Refused("not an absolute URL");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw Refused($"scheme '{uri.Scheme}' is not http or https");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw Refused("URL carries userinfo");

        if (string.IsNullOrEmpty(uri.Host))
            throw Refused("URL has no host");

        return uri;
    }

    private static PublicUrlFetchException Refused(string reason)
        => new(PublicUrlFetchFailure.NotAllowed, PublicUrlFetchException.NotAllowedMessage, reason);

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<(byte[] Content, bool Truncated)> ReadCappedAsync(
        HttpResponseMessage response, PublicUrlFetchOptions options, TimeSpan timeout,
        CancellationToken callerToken, CancellationToken token)
    {
        var maxBytes = options.MaxBytes;
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
                {
                    if (!options.TruncateAtCap)
                        throw new PublicUrlFetchException(
                            PublicUrlFetchFailure.TooLarge, PublicUrlFetchException.TooLargeMessage(maxBytes),
                            $"body exceeded the {maxBytes}-byte cap while streaming");

                    // Keep only what fits; disposing the response (by the caller) drops the rest of the connection.
                    var keep = (int)(maxBytes - (total - read));
                    buffered.Write(buffer, 0, keep);
                    return (buffered.ToArray(), true);
                }
                buffered.Write(buffer, 0, read);
            }

            return (buffered.ToArray(), false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new PublicUrlFetchException(
                PublicUrlFetchFailure.Timeout, PublicUrlFetchException.TimeoutMessage, $"body not received within {timeout}");
        }
        catch (HttpRequestException ex)
        {
            throw Translate(ex);
        }
        catch (IOException ex)
        {
            throw new PublicUrlFetchException(
                PublicUrlFetchFailure.Unreachable, PublicUrlFetchException.UnreachableMessage, $"body read failed: {ex.Message}");
        }
    }

    /// <summary>A refusal raised inside the connect callback arrives wrapped; everything else is a plain network failure.</summary>
    private static PublicUrlFetchException Translate(HttpRequestException ex)
    {
        for (Exception? inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is PublicUrlFetchException refused) return refused;
        }

        return new PublicUrlFetchException(PublicUrlFetchFailure.Unreachable, PublicUrlFetchException.UnreachableMessage, ex.Message);
    }

    /// <summary>
    /// The primary handler every guarded client uses: no automatic redirects (a redirect hop would
    /// otherwise connect to an unjudged address), no proxy (it would do the connecting), and
    /// <see cref="ConnectAsync"/> judging the address of every socket it opens.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler(IPublicUrlFetchAllowance allowance) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = (context, ct) => ConnectAsync(context, allowance, ct)
    };

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
            throw new PublicUrlFetchException(
                PublicUrlFetchFailure.Unreachable, PublicUrlFetchException.UnreachableMessage, $"'{host}' resolved to no addresses");

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
            throw Refused($"'{host}:{port}' only resolves to non-public addresses: {string.Join("; ", refusals)}");

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
            .UsePublicUrlPolicy();

        return services;
    }

    /// <summary>
    /// Puts a named or typed HttpClient on the public-url address policy: its primary handler is
    /// the same guarded <see cref="SocketsHttpHandler"/> the fetcher uses (connect-time address
    /// check, no automatic redirects, no proxy). For third-party clients that need their own
    /// request semantics but must still only talk to public addresses.
    /// </summary>
    public static IHttpClientBuilder UsePublicUrlPolicy(this IHttpClientBuilder builder)
    {
        builder.Services.TryAddSingleton<IPublicUrlFetchAllowance, NoFetchAllowance>();
        return builder
            .ConfigureHttpClient(client =>
            {
                // HTTP/3 runs over QUIC, which SocketsHttpHandler opens itself without consulting
                // ConnectCallback; pinning HTTP/1.1-or-lower means it can never be negotiated.
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                PublicUrlFetcher.CreateHandler(sp.GetRequiredService<IPublicUrlFetchAllowance>()));
    }
}
