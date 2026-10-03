namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// Fetches a user- or admin-supplied URL on the server's behalf without letting it reach anything
/// but public Internet addresses. Use this, never a plain <see cref="HttpClient"/>, for any URL
/// that comes from the UI, configuration or chat content.
/// </summary>
public interface IPublicUrlFetcher
{
    /// <summary>
    /// GETs <paramref name="url"/> and returns the body, up to <paramref name="maxBytes"/>; a larger
    /// body is refused. Equivalent to <see cref="FetchAsync(string, PublicUrlFetchOptions, CancellationToken)"/>
    /// with only <see cref="PublicUrlFetchOptions.MaxBytes"/> set.
    /// </summary>
    /// <exception cref="PublicUrlFetchException">
    /// The URL is not http/https, carries userinfo, targets a non-public address (at any redirect
    /// hop, judged at the connected socket), redirects more than <see cref="PublicUrlFetcher.MaxRedirects"/>
    /// times, answers with a non-success status, exceeds <paramref name="maxBytes"/>, or does not
    /// answer in time. The message is safe to show; <see cref="PublicUrlFetchException.Reason"/> is for logs.
    /// </exception>
    Task<PublicUrlFetchResult> FetchAsync(string url, long maxBytes, CancellationToken ct = default);

    /// <summary>
    /// GETs <paramref name="url"/> under <paramref name="options"/>: the size cap, whether an
    /// oversized body is cut at the cap or refused, and the overall deadline.
    /// </summary>
    /// <exception cref="PublicUrlFetchException">
    /// As for <see cref="FetchAsync(string, long, CancellationToken)"/>, except that with
    /// <see cref="PublicUrlFetchOptions.TruncateAtCap"/> an oversized body is returned cut at the
    /// cap (<see cref="PublicUrlFetchResult.Truncated"/>) instead of refused.
    /// </exception>
    Task<PublicUrlFetchResult> FetchAsync(string url, PublicUrlFetchOptions options, CancellationToken ct = default);
}

/// <summary>Per-call settings for <see cref="IPublicUrlFetcher"/>. The address policy is never a setting.</summary>
public sealed record PublicUrlFetchOptions
{
    /// <summary>Most body bytes the fetch will read.</summary>
    public required long MaxBytes { get; init; }

    /// <summary>
    /// When true, a body over <see cref="MaxBytes"/> is returned cut at the cap with
    /// <see cref="PublicUrlFetchResult.Truncated"/> set, for callers that only need the start of a
    /// document. When false (the default) such a body is refused.
    /// </summary>
    public bool TruncateAtCap { get; init; }

    /// <summary>Longest deadline a caller may ask for; the body read must never run unbounded.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Overall deadline across every hop and the body; null means <see cref="PublicUrlFetcher.FetchTimeout"/>.
    /// Must be positive and at most <see cref="MaxTimeout"/>; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is refused.
    /// </summary>
    public TimeSpan? Timeout
    {
        get;
        init
        {
            if (value is { } timeout && (timeout <= TimeSpan.Zero || timeout > MaxTimeout))
                throw new ArgumentOutOfRangeException(nameof(Timeout), timeout, $"Timeout must be positive and at most {MaxTimeout}.");
            field = value;
        }
    }
}

/// <summary>What a public-url fetch returned.</summary>
/// <param name="Content">The response body, at most the requested cap.</param>
/// <param name="MediaType">The response's Content-Type media type, when it sent one.</param>
/// <param name="FinalUri">The URL that answered, after any redirects.</param>
/// <param name="Truncated">True when the body was longer than the cap and <see cref="PublicUrlFetchOptions.TruncateAtCap"/> cut it.</param>
/// <param name="Charset">The response's Content-Type charset parameter, when it sent one; for callers that decode the body as text.</param>
public sealed record PublicUrlFetchResult(byte[] Content, string? MediaType, Uri FinalUri, bool Truncated = false, string? Charset = null);
