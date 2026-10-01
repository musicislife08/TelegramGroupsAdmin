namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// Fetches a user- or admin-supplied URL on the server's behalf without letting it reach anything
/// but public Internet addresses. Use this, never a plain <see cref="HttpClient"/>, for any URL
/// that comes from the UI, configuration or chat content.
/// </summary>
public interface IPublicUrlFetcher
{
    /// <summary>
    /// GETs <paramref name="url"/> and returns the body, up to <paramref name="maxBytes"/>.
    /// </summary>
    /// <exception cref="PublicUrlFetchException">
    /// The URL is not http/https, carries userinfo, targets a non-public address (at any redirect
    /// hop, judged at the connected socket), redirects more than <see cref="PublicUrlFetcher.MaxRedirects"/>
    /// times, answers with a non-success status, exceeds <paramref name="maxBytes"/>, or does not
    /// answer in time. The message is safe to show; <see cref="PublicUrlFetchException.Reason"/> is for logs.
    /// </exception>
    Task<PublicUrlFetchResult> FetchAsync(string url, long maxBytes, CancellationToken ct = default);
}

/// <summary>What a public-url fetch returned.</summary>
/// <param name="Content">The response body, at most the requested cap.</param>
/// <param name="MediaType">The response's Content-Type media type, when it sent one.</param>
/// <param name="FinalUri">The URL that answered, after any redirects.</param>
public sealed record PublicUrlFetchResult(byte[] Content, string? MediaType, Uri FinalUri);
