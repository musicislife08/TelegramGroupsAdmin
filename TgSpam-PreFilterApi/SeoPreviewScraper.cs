namespace TgSpam_PreFilterApi;

using AngleSharp;
using AngleSharp.Html.Dom;

public class SeoPreviewScraper(HttpClient client)
{
    public async Task<SeoPreviewResult?> GetSeoPreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new("Mozilla", "5.0"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode || !response.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var context = BrowsingContext.New();
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            return new SeoPreviewResult(
                Title: document.Title?.Trim(),
                Description: GetMeta("description"),
                OgTitle: GetMeta("og:title"),
                OgDescription: GetMeta("og:description"),
                OgImage: GetMeta("og:image"),
                FinalUrl: response.RequestMessage?.RequestUri?.ToString()
            );

            string? GetMeta(string name) =>
                document.QuerySelectorAll("meta")
                    .OfType<IHtmlMetaElement>()
                    .FirstOrDefault(m =>
                        m.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true ||
                        m.GetAttribute("property")?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                    ?.Content;
        }
        catch
        {
            return null; // Fail-safe: treat as no preview
        }
    }
}