using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// The URL scraper fetches whatever any chat member posts, so it goes through the public-url
/// fetcher: a URL that resolves to a loopback or private address is never requested, and a
/// refused, oversized, slow or non-HTML URL leaves the message text as it was — enrichment
/// degrades, message processing never fails because of it.
/// </summary>
[TestFixture]
public class UrlContentScrapingServiceTests
{
    private const string PageHtml =
        "<html><head><title>Totally Legit Offer</title>" +
        "<meta name=\"description\" content=\"Buy one get one free on everything today\">" +
        "</head><body><p>hello</p></body></html>";

    private readonly List<IDisposable> _disposables = [];

    [TearDown]
    public void DisposeAll()
    {
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_HostnameResolvingToLoopback_IsSkippedWithoutTheAllowance()
    {
        var server = StartHtmlServer("/page", PageHtml);
        var sut = CreateService();
        var text = $"check this out http://localhost:{server.Port}/page";

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync(text);

        Assert.That(enriched, Is.EqualTo(text), "a refused URL adds nothing and throws nothing");
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
    }

    [TestCase("http://127.0.0.1:{port}/page")]
    [TestCase("http://[::1]:{port}/page")]
    [TestCase("http://10.0.0.1/page")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    public async Task EnrichMessageWithUrlPreviewsAsync_NonPublicLiteral_IsSkipped(string template)
    {
        var server = StartHtmlServer("/page", PageHtml);
        var sut = CreateService();
        var text = "see " + template.Replace("{port}", server.Port.ToString());

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync(text);

        Assert.That(enriched, Is.EqualTo(text));
        Assert.That(server.LogEntries, Is.Empty);
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_ThroughTheAllowance_AppendsThePreview()
    {
        var server = StartHtmlServer("/page", PageHtml);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));
        var url = $"http://localhost:{server.Port}/page";

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync($"check this out {url}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enriched, Does.StartWith("check this out " + url));
            Assert.That(enriched, Does.Contain("Totally Legit Offer"));
            Assert.That(enriched, Does.Contain("Buy one get one free on everything today"));
        }
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/page" }));
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_OneRefusedOneAllowed_KeepsOnlyTheAllowedPreview()
    {
        var allowed = StartHtmlServer("/ok", PageHtml);
        var refused = StartHtmlServer("/secret", "<html><head><title>Internal Admin Panel</title></head></html>");
        var sut = CreateService(new LoopbackPortAllowance(allowed.Port));
        var text = $"http://localhost:{allowed.Port}/ok and http://localhost:{refused.Port}/secret";

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync(text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enriched, Does.Contain("Totally Legit Offer"));
            Assert.That(enriched, Does.Not.Contain("Internal Admin Panel"));
        }
        Assert.That(refused.LogEntries, Is.Empty);
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_PageLargerThanTheCap_StillReadsTheHead()
    {
        // Only the head is parsed, so a long body past the cap is cut off rather than refused.
        var padding = new string('x', (int)UrlContentScrapingService.MaxBytesPerUrl * 2);
        var html = $"<html><head><title>Long Page</title></head><body>{padding}</body></html>";
        var server = StartHtmlServer("/long", html);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync($"http://localhost:{server.Port}/long");

        Assert.That(enriched, Does.Contain("Long Page"));
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_NonHtmlContent_IsSkipped()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/anim.gif").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "image/gif")
                .WithBody("GIF89a<title>not a page</title>"u8.ToArray()));
        var sut = CreateService(new LoopbackPortAllowance(server.Port));
        var text = $"http://localhost:{server.Port}/anim.gif";

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync(text);

        Assert.That(enriched, Is.EqualTo(text));
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_HttpError_IsSkipped()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/gone").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        var sut = CreateService(new LoopbackPortAllowance(server.Port));
        var text = $"http://localhost:{server.Port}/gone";

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync(text);

        Assert.That(enriched, Is.EqualTo(text));
    }

    [Test]
    public async Task EnrichMessageWithUrlPreviewsAsync_DeclaredCharset_IsHonoured()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        var html = "<html><head><title>Café Über</title></head></html>";
        server.Given(Request.Create().WithPath("/latin1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "text/html; charset=iso-8859-1")
                .WithBody(Encoding.Latin1.GetBytes(html)));
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        var enriched = await sut.EnrichMessageWithUrlPreviewsAsync($"http://localhost:{server.Port}/latin1");

        Assert.That(enriched, Does.Contain("Café Über"));
    }

    #region Helpers

    private IUrlContentScrapingService CreateService(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        services.AddPublicUrlFetcher();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return new UrlContentScrapingService(
            provider.GetRequiredService<IPublicUrlFetcher>(),
            NullLogger<UrlContentScrapingService>.Instance);
    }

    private WireMockServer StartHtmlServer(string path, string html)
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "text/html; charset=utf-8")
                .WithBody(html));
        return server;
    }

    #endregion
}
