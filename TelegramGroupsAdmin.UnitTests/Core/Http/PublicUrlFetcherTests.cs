using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TelegramGroupsAdmin.UnitTests.Core.Http;

/// <summary>
/// The public-url fetcher refuses every URL that would make the server talk to a non-public address,
/// at the socket it actually connects (so DNS answers and redirect hops are covered), and caps what
/// it reads. Loopback is reachable only through <see cref="LoopbackPortAllowance"/>, registered by
/// the test's own DI — there is no configuration path to it.
/// </summary>
[TestFixture]
public class PublicUrlFetcherTests
{
    private const long OneMegabyte = 1024 * 1024;

    private readonly List<IDisposable> _disposables = [];

    [TearDown]
    public void DisposeAll()
    {
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
    }

    #region Scheme and userinfo

    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/anim.gif")]
    [TestCase("gopher://example.com/1")]
    [TestCase("data:image/gif;base64,R0lGODlh")]
    [TestCase("javascript:alert(1)")]
    [TestCase("http://user:secret@example.com/anim.gif")]
    [TestCase("http://user@example.com/anim.gif")]
    [TestCase("example.com/anim.gif")]
    [TestCase("//example.com/anim.gif")]
    [TestCase("http:///anim.gif")]
    [TestCase("")]
    public void FetchAsync_SchemeOrUserinfoNotAllowed_IsRefusedBeforeAnyRequest(string url)
    {
        var fetcher = CreateFetcher();

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(url, OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(ex.Reason, Is.Not.Empty);
    }

    #endregion

    #region Address policy at the connected socket

    [TestCase("http://127.0.0.1:{port}/anim.gif")]
    [TestCase("http://[::1]:{port}/anim.gif")]
    [TestCase("http://[::ffff:127.0.0.1]:{port}/anim.gif")]
    [TestCase("http://10.0.0.1:{port}/anim.gif")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://0.0.0.0:{port}/anim.gif")]
    public void FetchAsync_NonPublicLiteral_IsRefusedWithoutConnecting(string template)
    {
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher();
        var url = template.Replace("{port}", server.Port.ToString());

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(url, OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
    }

    // Non-dotted and non-canonical spellings of 127.0.0.1. System.Uri canonicalises every one of
    // these to "127.0.0.1" (checked on .NET 10), and the connect callback judges that address;
    // pinned so a parser change that starts passing them through as hostnames is caught.
    [TestCase("http://2130706433:{port}/anim.gif")]
    [TestCase("http://0x7f.0.0.1:{port}/anim.gif")]
    [TestCase("http://0x7f000001:{port}/anim.gif")]
    [TestCase("http://0177.0.0.1:{port}/anim.gif")]
    [TestCase("http://127.1:{port}/anim.gif")]
    [TestCase("http://[::127.0.0.1]:{port}/anim.gif")]
    [TestCase("http://[::ffff:7f00:1]:{port}/anim.gif")]
    [TestCase("http://[64:ff9b::7f00:1]:{port}/anim.gif")]
    [TestCase("http://[64:ff9b:1::7f00:1]:{port}/anim.gif")]
    public void FetchAsync_NonCanonicalLoopbackLiteral_IsRefusedWithoutConnecting(string template)
    {
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher();
        var url = template.Replace("{port}", server.Port.ToString());

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(url, OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(ex.Reason, Does.Contain("loopback").Or.Contain("NAT64"));
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
    }

    [Test]
    public void FetchAsync_HostnameResolvingToLoopback_IsRefusedAtConnectTime()
    {
        // "localhost" is not an IP literal, so only the connect-time check can catch it: the URL
        // parser sees a hostname, DNS answers 127.0.0.1 / ::1, and the socket must not be opened.
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher();
        var url = $"http://localhost:{server.Port}/anim.gif";

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(url, OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(ex.Reason, Does.Contain("loopback"));
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
    }

    [Test]
    public async Task FetchAsync_HostnameResolvingToLoopback_IsAllowedOnlyThroughTheAllowance()
    {
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));
        var url = $"http://localhost:{server.Port}/anim.gif";

        var result = await fetcher.FetchAsync(url, OneMegabyte);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Content, Is.EqualTo(GifBytes));
            Assert.That(result.MediaType, Is.EqualTo("image/gif"));
            Assert.That(result.FinalUri, Is.EqualTo(new Uri(url)));
        }
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/anim.gif" }));
    }

    [Test]
    public void FetchAsync_AllowanceForAnotherPort_DoesNotOpenThisOne()
    {
        var server = StartGifServer("/anim.gif");
        var otherPort = ReserveUnusedPort();
        var fetcher = CreateFetcher(new LoopbackPortAllowance(otherPort));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/anim.gif", OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(server.LogEntries, Is.Empty);
    }

    #endregion

    #region Redirects

    [Test]
    public async Task FetchAsync_RedirectToAnAllowedTarget_IsFollowed()
    {
        var server = StartGifServer("/final.gif");
        server.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "/final.gif"));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var result = await fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/start", OneMegabyte);

        Assert.That(result.Content, Is.EqualTo(GifBytes));
        Assert.That(result.FinalUri.AbsolutePath, Is.EqualTo("/final.gif"));
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/start", "/final.gif" }));
    }

    [Test]
    public void FetchAsync_RedirectToLoopbackPortOutsideTheAllowance_IsRefused()
    {
        var allowed = WireMockServer.Start();
        _disposables.Add(allowed);
        var target = StartGifServer("/anim.gif");
        allowed.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302)
                .WithHeader("Location", $"http://127.0.0.1:{target.Port}/anim.gif"));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(allowed.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{allowed.Port}/start", OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(target.LogEntries, Is.Empty, "the redirect target must never see a request");
    }

    [Test]
    public void FetchAsync_RedirectToPrivateLiteral_IsRefused()
    {
        var allowed = WireMockServer.Start();
        _disposables.Add(allowed);
        allowed.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(301).WithHeader("Location", "http://10.0.0.1/anim.gif"));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(allowed.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{allowed.Port}/start", OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(ex.Reason, Does.Contain("private"));
    }

    [Test]
    public void FetchAsync_RedirectToLocalhostHostname_IsRefusedAtConnectTime()
    {
        var allowed = WireMockServer.Start();
        _disposables.Add(allowed);
        var target = StartGifServer("/anim.gif");
        allowed.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(307)
                .WithHeader("Location", $"http://localhost:{target.Port}/anim.gif"));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(allowed.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{allowed.Port}/start", OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(target.LogEntries, Is.Empty);
    }

    [Test]
    public void FetchAsync_RedirectToNonHttpScheme_IsRefused()
    {
        var allowed = WireMockServer.Start();
        _disposables.Add(allowed);
        allowed.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "ftp://example.com/anim.gif"));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(allowed.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{allowed.Port}/start", OneMegabyte));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(ex.Reason, Does.Contain("ftp"));
    }

    // A TLS origin is awkward to stand up here, so the downgrade rule is pinned on the hop check
    // the redirect loop calls; .NET's own redirect handler never follows https -> http either.
    [TestCase("https://example.com/a", "http://example.com/b", false)]
    [TestCase("https://example.com/a", "https://cdn.example.com/b", true)]
    [TestCase("http://example.com/a", "https://example.com/b", true)]
    [TestCase("http://example.com/a", "http://cdn.example.com/b", true)]
    public void IsAllowedRedirect_RefusesOnlyHttpsToHttpDowngrade(string from, string to, bool allowed)
    {
        Assert.That(PublicUrlFetcher.IsAllowedRedirect(new Uri(from), new Uri(to)), Is.EqualTo(allowed));
    }

    [Test]
    public void FetchAsync_MoreRedirectsThanTheLimit_IsRefused()
    {
        var server = StartGifServer("/final.gif");
        for (var hop = 0; hop <= PublicUrlFetcher.MaxRedirects; hop++)
        {
            var next = hop == PublicUrlFetcher.MaxRedirects ? "/final.gif" : $"/hop{hop + 1}";
            server.Given(Request.Create().WithPath($"/hop{hop}").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", next));
        }
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/hop0", OneMegabyte));

        Assert.That(ex!.Reason, Does.Contain("redirect"));
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Does.Not.Contain("/final.gif"),
            "the hop past the limit is never requested");
    }

    [Test]
    public void FetchAsync_RedirectWithoutLocation_IsRefused()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/start").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/start", OneMegabyte));

        Assert.That(ex!.Reason, Does.Contain("Location"));
    }

    #endregion

    #region Size cap and status

    [Test]
    public void FetchAsync_DeclaredContentLengthOverTheCap_IsRefusedBeforeReadingTheBody()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/big.gif").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "image/gif")
                .WithBody(new byte[4096]));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/big.gif", maxBytes: 1024));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.TooLargeMessage(1024)));
    }

    [Test]
    public async Task FetchAsync_BodyOverTheCapWithoutContentLength_IsRefusedMidStream()
    {
        // No Content-Length and no chunking: the body is delimited by connection close, so only
        // counting while streaming can enforce the cap.
        const int cap = 1024;
        using var server = new RawHttpServer(bodyBytes: 64 * 1024);
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/stream.gif", cap));

        Assert.That(ex!.Message, Is.EqualTo(PublicUrlFetchException.TooLargeMessage(cap)));
        Assert.That(await server.RequestSeen, Is.True, "the request did reach the server; the body was cut off");
    }

    [Test]
    public async Task FetchAsync_BodyExactlyAtTheCap_IsAccepted()
    {
        using var server = new RawHttpServer(bodyBytes: 1024);
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var result = await fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/stream.gif", maxBytes: 1024);

        Assert.That(result.Content, Has.Length.EqualTo(1024));
    }

    [Test]
    public void FetchAsync_NonSuccessStatus_IsRefusedWithTheStatus()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/missing.gif").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(
            () => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/missing.gif", OneMegabyte));

        Assert.That(ex!.Message, Does.Contain("404"));
    }

    #endregion

    #region Per-call options

    [Test]
    public async Task FetchAsync_TruncateAtCap_ReturnsTheFirstBytesAndFlagsTruncation()
    {
        // A caller that only needs the start of a document (an HTML head) asks for truncation
        // instead of refusal: a declared Content-Length over the cap is not a reason to refuse,
        // and reading stops at the cap.
        var server = WireMockServer.Start();
        _disposables.Add(server);
        var body = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        server.Given(Request.Create().WithPath("/page").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/html").WithBody(body));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var result = await fetcher.FetchAsync(
            $"http://127.0.0.1:{server.Port}/page",
            new PublicUrlFetchOptions { MaxBytes = 1024, TruncateAtCap = true });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Content, Is.EqualTo(body.Take(1024).ToArray()));
            Assert.That(result.Truncated, Is.True);
            Assert.That(result.MediaType, Is.EqualTo("text/html"));
        }
    }

    [Test]
    public async Task FetchAsync_TruncateAtCap_BodyWithinTheCap_IsReturnedWhole()
    {
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var result = await fetcher.FetchAsync(
            $"http://127.0.0.1:{server.Port}/anim.gif",
            new PublicUrlFetchOptions { MaxBytes = OneMegabyte, TruncateAtCap = true });

        Assert.That(result.Content, Is.EqualTo(GifBytes));
        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public async Task FetchAsync_TruncateAtCap_BodyExactlyAtTheCap_IsNotFlagged()
    {
        using var server = new RawHttpServer(bodyBytes: 1024);
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var result = await fetcher.FetchAsync(
            $"http://127.0.0.1:{server.Port}/stream.gif",
            new PublicUrlFetchOptions { MaxBytes = 1024, TruncateAtCap = true });

        Assert.That(result.Content, Has.Length.EqualTo(1024));
        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public void FetchAsync_TruncateAtCap_StillRefusesNonPublicTargets()
    {
        var server = StartGifServer("/anim.gif");
        var fetcher = CreateFetcher();

        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(
            $"http://127.0.0.1:{server.Port}/anim.gif",
            new PublicUrlFetchOptions { MaxBytes = 1024, TruncateAtCap = true }));

        Assert.That(ex!.Kind, Is.EqualTo(PublicUrlFetchFailure.NotAllowed));
        Assert.That(server.LogEntries, Is.Empty);
    }

    [Test]
    public void FetchAsync_PerCallTimeout_CutsTheFetchShort()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("late").WithDelay(TimeSpan.FromSeconds(20)));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));

        var started = DateTime.UtcNow;
        var ex = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync(
            $"http://127.0.0.1:{server.Port}/slow",
            new PublicUrlFetchOptions { MaxBytes = OneMegabyte, Timeout = TimeSpan.FromMilliseconds(500) }));

        Assert.That(ex!.Kind, Is.EqualTo(PublicUrlFetchFailure.Timeout));
        Assert.That(ex.Message, Is.EqualTo(PublicUrlFetchException.TimeoutMessage));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-1000)]
    public void PublicUrlFetchOptions_ZeroNegativeOrInfiniteTimeout_IsRejected(int milliseconds)
    {
        // Timeout.InfiniteTimeSpan is -1 ms; none of these may ever reach the body read.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new PublicUrlFetchOptions { MaxBytes = OneMegabyte, Timeout = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Test]
    public void PublicUrlFetchOptions_TimeoutOverTheMaximum_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new PublicUrlFetchOptions { MaxBytes = OneMegabyte, Timeout = PublicUrlFetchOptions.MaxTimeout + TimeSpan.FromTicks(1) });
        Assert.DoesNotThrow(() =>
            _ = new PublicUrlFetchOptions { MaxBytes = OneMegabyte, Timeout = PublicUrlFetchOptions.MaxTimeout });
    }

    [Test]
    public void UsePublicUrlPolicy_PinsHttp11_SoQuicCannotBypassTheConnectCallback()
    {
        // HTTP/3 runs over QUIC, which SocketsHttpHandler opens itself - ConnectCallback is never
        // consulted - so the guarded clients must never be allowed to negotiate it.
        // .NET's defaults already are 1.1 / RequestVersionOrLower, so an earlier registration
        // asks for HTTP/3 to prove the policy overrides whatever composition did before it.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(PublicUrlFetcher.ClientName).ConfigureHttpClient(WantHttp3);
        services.AddHttpClient<ProbeClient>().ConfigureHttpClient(WantHttp3);
        services.AddPublicUrlFetcher();
        services.AddHttpClient<ProbeClient>().UsePublicUrlPolicy();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        foreach (var name in new[] { PublicUrlFetcher.ClientName, nameof(ProbeClient) })
        {
            using var client = factory.CreateClient(name);
            Assert.That(client.DefaultRequestVersion, Is.EqualTo(HttpVersion.Version11), name);
            Assert.That(client.DefaultVersionPolicy, Is.EqualTo(HttpVersionPolicy.RequestVersionOrLower), name);
        }
    }

    [Test]
    public void FetchAsync_Failures_CarryTheirKind()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        server.Given(Request.Create().WithPath("/big").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[4096]));
        var fetcher = CreateFetcher(new LoopbackPortAllowance(server.Port));
        var unreachablePort = ReserveUnusedPort();
        var unreachable = CreateFetcher(new LoopbackPortAllowance(unreachablePort));

        var status = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/missing", OneMegabyte));
        var tooLarge = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync($"http://127.0.0.1:{server.Port}/big", 1024));
        var scheme = Assert.ThrowsAsync<PublicUrlFetchException>(() => fetcher.FetchAsync("ftp://example.com/x", OneMegabyte));
        var refused = Assert.ThrowsAsync<PublicUrlFetchException>(() => unreachable.FetchAsync($"http://127.0.0.1:{unreachablePort}/x", OneMegabyte));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status!.Kind, Is.EqualTo(PublicUrlFetchFailure.HttpStatus));
            Assert.That(tooLarge!.Kind, Is.EqualTo(PublicUrlFetchFailure.TooLarge));
            Assert.That(scheme!.Kind, Is.EqualTo(PublicUrlFetchFailure.NotAllowed));
            Assert.That(refused!.Kind, Is.EqualTo(PublicUrlFetchFailure.Unreachable));
        }
    }

    #endregion

    #region Typed clients on the same policy

    private sealed class ProbeClient(HttpClient http)
    {
        public Task<HttpResponseMessage> PostAsync(string url) => http.PostAsync(url, new StringContent("ping"));
    }

    [Test]
    public void UsePublicUrlPolicy_TypedClient_RefusesLoopbackWithoutTheAllowance()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/push").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));
        var probe = CreateProbeClient();

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => probe.PostAsync($"http://localhost:{server.Port}/push"));

        Assert.That(FindRefusal(ex!), Is.Not.Null, "the connect callback's refusal must be the cause");
        Assert.That(FindRefusal(ex!)!.Kind, Is.EqualTo(PublicUrlFetchFailure.NotAllowed));
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
    }

    [Test]
    public async Task UsePublicUrlPolicy_TypedClient_ConnectsThroughTheAllowance()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/push").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));
        var probe = CreateProbeClient(new LoopbackPortAllowance(server.Port));

        using var response = await probe.PostAsync($"http://localhost:{server.Port}/push");

        Assert.That((int)response.StatusCode, Is.EqualTo(201));
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/push" }));
    }

    [Test]
    public void UsePublicUrlPolicy_TypedClient_DoesNotFollowRedirectsOnItsOwn()
    {
        // A push service or API answering 3xx must not drag the client to an unjudged address:
        // the typed client sees the redirect status and goes no further.
        var allowed = WireMockServer.Start();
        _disposables.Add(allowed);
        var target = WireMockServer.Start();
        _disposables.Add(target);
        allowed.Given(Request.Create().WithPath("/push").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(307).WithHeader("Location", $"http://127.0.0.1:{target.Port}/elsewhere"));
        var probe = CreateProbeClient(new LoopbackPortAllowance(allowed.Port, target.Port));

        using var response = probe.PostAsync($"http://127.0.0.1:{allowed.Port}/push").GetAwaiter().GetResult();

        Assert.That((int)response.StatusCode, Is.EqualTo(307));
        Assert.That(target.LogEntries, Is.Empty);
    }

    private static void WantHttp3(HttpClient client)
    {
        client.DefaultRequestVersion = HttpVersion.Version30;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
    }

    private ProbeClient CreateProbeClient(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        services.AddHttpClient<ProbeClient>().UsePublicUrlPolicy();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return provider.GetRequiredService<ProbeClient>();
    }

    private static PublicUrlFetchException? FindRefusal(Exception ex)
    {
        for (Exception? inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is PublicUrlFetchException refused) return refused;
        }
        return null;
    }

    #endregion

    #region Helpers

    private static readonly byte[] GifBytes = "GIF89a\0\0\0\0;"u8.ToArray();

    private IPublicUrlFetcher CreateFetcher(IPublicUrlFetchAllowance? allowance = null)
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
        return provider.GetRequiredService<IPublicUrlFetcher>();
    }

    private WireMockServer StartGifServer(string path)
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "image/gif")
                .WithBody(GifBytes));
        return server;
    }

    private static int ReserveUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// One-shot HTTP/1.1 server that answers with a body framed by connection close (no
    /// Content-Length, no chunking), which WireMock cannot produce.
    /// </summary>
    private sealed class RawHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<bool> _requestSeen = new();

        public RawHttpServer(int bodyBytes)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = ServeAsync(bodyBytes);
        }

        public int Port { get; }
        public Task<bool> RequestSeen => _requestSeen.Task;

        private async Task ServeAsync(int bodyBytes)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var header = new StringBuilder();
                while (!header.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0) break;
                    header.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                _requestSeen.TrySetResult(true);

                var head = "HTTP/1.1 200 OK\r\nContent-Type: image/gif\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                var body = new byte[bodyBytes];
                await stream.WriteAsync(body);
                await stream.FlushAsync();
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                // The fetcher closes the socket early when the cap trips; nothing to do.
                _requestSeen.TrySetResult(false);
            }
        }

        public void Dispose() => _listener.Stop();
    }

    #endregion
}
