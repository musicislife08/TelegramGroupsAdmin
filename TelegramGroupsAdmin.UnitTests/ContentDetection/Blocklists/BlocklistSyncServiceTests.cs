using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;
using TelegramGroupsAdmin.Core.Http;
using TelegramGroupsAdmin.Core.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection.Blocklists;

/// <summary>
/// Blocklist subscriptions are admin-entered URLs the server downloads on a schedule. Downloads go
/// through the public-url fetcher under a bounded cap: a subscription pointing at a loopback or
/// private address is refused before any request is made, the refusal is reported for that
/// subscription by name, and the remaining subscriptions still sync.
/// </summary>
[TestFixture]
public class BlocklistSyncServiceTests
{
    private const string ListBody = "# comment\nevil.example\nbad.example\n";

    private readonly List<IDisposable> _disposables = [];
    private IBlocklistSubscriptionsRepository _subscriptions = null!;
    private ICachedBlockedDomainsRepository _cache = null!;
    private IDomainFiltersRepository _filters = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBlocklistSubscriptionsRepository>();
        _cache = Substitute.For<ICachedBlockedDomainsRepository>();
        _filters = Substitute.For<IDomainFiltersRepository>();
        _filters.GetAllAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [TearDown]
    public void DisposeAll()
    {
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
    }

    [Test]
    public void SyncSubscriptionAsync_UrlResolvingToLoopback_IsRefusedWithoutTheAllowance()
    {
        var server = StartListServer("/list.txt");
        var sub = Subscription(1, "Loopback list", $"http://localhost:{server.Port}/list.txt");
        Register(sub);
        var sut = CreateService();

        var ex = Assert.ThrowsAsync<BlocklistSyncException>(() => sut.SyncSubscriptionAsync(sub.Id));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain("Loopback list"));
            Assert.That(ex.Message, Does.Contain(PublicUrlFetchException.NotAllowedMessage));
            Assert.That(ex.Failures.Select(f => f.SubscriptionId), Is.EqualTo(new[] { 1L }));
        }
        Assert.That(server.LogEntries, Is.Empty, "the server must never see a request");
        _cache.DidNotReceiveWithAnyArgs().DeleteBySourceAsync(default!, default, default);
        _cache.DidNotReceiveWithAnyArgs().BulkInsertAsync(default!, default);
        _subscriptions.DidNotReceiveWithAnyArgs().UpdateFetchMetadataAsync(default, default, default, default);
    }

    [TestCase("http://127.0.0.1:{port}/list.txt")]
    [TestCase("https://10.0.0.1/list.txt")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    public void SyncSubscriptionAsync_NonPublicLiteral_IsRefused(string template)
    {
        var server = StartListServer("/list.txt");
        var sub = Subscription(1, "Literal list", template.Replace("{port}", server.Port.ToString()));
        Register(sub);
        var sut = CreateService();

        var ex = Assert.ThrowsAsync<BlocklistSyncException>(() => sut.SyncSubscriptionAsync(sub.Id));

        Assert.That(ex!.Message, Does.Contain(PublicUrlFetchException.NotAllowedMessage));
        Assert.That(server.LogEntries, Is.Empty);
        _cache.DidNotReceiveWithAnyArgs().BulkInsertAsync(default!, default);
    }

    [Test]
    public async Task SyncSubscriptionAsync_ThroughTheAllowance_CachesTheDomains()
    {
        var server = StartListServer("/list.txt");
        var sub = Subscription(1, "Allowed list", $"http://localhost:{server.Port}/list.txt");
        Register(sub);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        await sut.SyncSubscriptionAsync(sub.Id);

        await _cache.Received(1).DeleteBySourceAsync("subscription", 1, Arg.Any<CancellationToken>());
        await _cache.Received(1).BulkInsertAsync(
            Arg.Is<List<CachedBlockedDomain>>(d => d!.Count == 2 && d.All(x => x.SourceSubscriptionId == 1)),
            Arg.Any<CancellationToken>());
        await _subscriptions.Received(1).UpdateFetchMetadataAsync(1, Arg.Any<DateTimeOffset>(), 2, Arg.Any<CancellationToken>());
        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/list.txt" }));
    }

    [Test]
    public void SyncSubscriptionAsync_ListLargerThanTheCap_IsRefused()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/huge.txt").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/plain")
                .WithBody(new byte[BlocklistSyncService.MaxBlocklistBytes + 1]));
        var sub = Subscription(1, "Huge list", $"http://localhost:{server.Port}/huge.txt");
        Register(sub);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        var ex = Assert.ThrowsAsync<BlocklistSyncException>(() => sut.SyncSubscriptionAsync(sub.Id));

        Assert.That(ex!.Message, Does.Contain(PublicUrlFetchException.TooLargeMessage(BlocklistSyncService.MaxBlocklistBytes)));
        _cache.DidNotReceiveWithAnyArgs().BulkInsertAsync(default!, default);
    }

    [Test]
    public void SyncAllAsync_OneRefusedSubscription_SyncsTheOthersAndReportsIt()
    {
        var allowed = StartListServer("/list.txt");
        var refused = StartListServer("/list.txt");
        var first = Subscription(1, "First", $"http://localhost:{allowed.Port}/list.txt");
        var internalList = Subscription(2, "Internal", $"http://localhost:{refused.Port}/list.txt");
        var third = Subscription(3, "Third", $"http://localhost:{allowed.Port}/list.txt");
        Register(first, internalList, third);
        var sut = CreateService(new LoopbackPortAllowance(allowed.Port));

        var ex = Assert.ThrowsAsync<BlocklistSyncException>(() => sut.SyncAllAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Failures.Select(f => f.SubscriptionId), Is.EqualTo(new[] { 2L }));
            Assert.That(ex.Message, Does.Contain("Internal"));
            Assert.That(ex.Message, Does.Contain(PublicUrlFetchException.NotAllowedMessage));
            Assert.That(ex.Message, Does.Not.Contain("First"));
            Assert.That(ex.Message, Does.Not.Contain("Third"));
        }
        Assert.That(refused.LogEntries, Is.Empty);
        _cache.Received(1).BulkInsertAsync(Arg.Is<List<CachedBlockedDomain>>(d => d!.All(x => x.SourceSubscriptionId == 1)), Arg.Any<CancellationToken>());
        _cache.Received(1).BulkInsertAsync(Arg.Is<List<CachedBlockedDomain>>(d => d!.All(x => x.SourceSubscriptionId == 3)), Arg.Any<CancellationToken>());
        _cache.DidNotReceive().BulkInsertAsync(Arg.Is<List<CachedBlockedDomain>>(d => d!.Any(x => x.SourceSubscriptionId == 2)), Arg.Any<CancellationToken>());
        _subscriptions.Received(1).UpdateFetchMetadataAsync(3, Arg.Any<DateTimeOffset>(), 2, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncAllAsync_AllSubscriptionsAllowed_Succeeds()
    {
        var server = StartListServer("/list.txt");
        Register(
            Subscription(1, "One", $"http://localhost:{server.Port}/list.txt"),
            Subscription(2, "Two", $"http://localhost:{server.Port}/list.txt"));
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        await sut.SyncAllAsync();

        await _cache.Received(2).BulkInsertAsync(Arg.Any<List<CachedBlockedDomain>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void RebuildCacheAsync_OneRefusedSubscription_StillSyncsManualFiltersAndReportsIt()
    {
        var refused = StartListServer("/list.txt");
        Register(Subscription(1, "Internal", $"http://localhost:{refused.Port}/list.txt"));
        _filters.GetAllAsync(0, Arg.Any<CancellationToken>()).Returns(
        [
            new DomainFilter(10, 0, "manual.example", DomainFilterType.Blacklist, BlockMode.Hard, true, Actor.SystemSeed, DateTimeOffset.UtcNow, null)
        ]);
        var sut = CreateService();

        var ex = Assert.ThrowsAsync<BlocklistSyncException>(() => sut.RebuildCacheAsync());

        Assert.That(ex!.Failures.Select(f => f.SubscriptionName), Is.EqualTo(new[] { "Internal" }));
        Assert.That(refused.LogEntries, Is.Empty);
        _cache.Received(1).DeleteAllAsync(0, Arg.Any<CancellationToken>());
        _cache.Received(1).BulkInsertAsync(
            Arg.Is<List<CachedBlockedDomain>>(d => d!.Count == 1 && d[0].Domain == "manual.example" && d[0].SourceSubscriptionId == null),
            Arg.Any<CancellationToken>());
    }

    // The HTTPS upgrade falls back to plain HTTP only when the HTTPS endpoint is unreachable or answers an
    // error status (what HttpRequestException covered before the fetcher). A slow or oversized HTTPS answer
    // fails the sync rather than waiting out, or re-downloading, the same list in plaintext.
    [TestCase(PublicUrlFetchFailure.Unreachable, true)]
    [TestCase(PublicUrlFetchFailure.HttpStatus, true)]
    [TestCase(PublicUrlFetchFailure.Timeout, false)]
    [TestCase(PublicUrlFetchFailure.TooLarge, false)]
    [TestCase(PublicUrlFetchFailure.NotAllowed, false)]
    public async Task SyncSubscriptionAsync_HttpsUpgradeFailure_FallsBackToHttpOnlyWhenUnreachableOrHttpStatus(
        PublicUrlFetchFailure httpsFailure, bool expectFallback)
    {
        const string httpUrl = "http://lists.example/list.txt";
        const string httpsUrl = "https://lists.example/list.txt";
        var fetcher = Substitute.For<IPublicUrlFetcher>();
        fetcher.FetchAsync(httpsUrl, Arg.Any<PublicUrlFetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PublicUrlFetchResult>(new PublicUrlFetchException(httpsFailure, "https failed", "https failed")));
        fetcher.FetchAsync(httpUrl, Arg.Any<PublicUrlFetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new PublicUrlFetchResult(System.Text.Encoding.UTF8.GetBytes(ListBody), "text/plain", new Uri(httpUrl)));
        var sub = Subscription(1, "Upgraded list", httpUrl);
        Register(sub);
        var sut = CreateService(fetcher);

        if (expectFallback)
        {
            await sut.SyncSubscriptionAsync(sub.Id);
            await _cache.Received(1).BulkInsertAsync(Arg.Is<List<CachedBlockedDomain>>(d => d!.Count == 2), Arg.Any<CancellationToken>());
        }
        else
        {
            Assert.ThrowsAsync<BlocklistSyncException>(() => sut.SyncSubscriptionAsync(sub.Id));
            await _cache.DidNotReceiveWithAnyArgs().BulkInsertAsync(default!, default);
        }

        await fetcher.Received(expectFallback ? 1 : 0)
            .FetchAsync(httpUrl, Arg.Any<PublicUrlFetchOptions>(), Arg.Any<CancellationToken>());
    }

    #region Helpers

    private static BlocklistSubscription Subscription(long id, string name, string url) => new(
        Id: id,
        ChatId: 0,
        Name: name,
        Url: url,
        Format: BlocklistFormat.NewlineDomains,
        BlockMode: BlockMode.Hard,
        IsBuiltIn: false,
        Enabled: true,
        LastFetched: null,
        EntryCount: null,
        RefreshIntervalHours: 24,
        AddedBy: Actor.SystemSeed,
        AddedDate: DateTimeOffset.UtcNow,
        Notes: null);

    private void Register(params BlocklistSubscription[] subs)
    {
        _subscriptions.GetAllAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(subs.ToList());
        foreach (var sub in subs)
        {
            _subscriptions.GetByIdAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);
        }
    }

    private IBlocklistSyncService CreateService(IPublicUrlFetcher fetcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_subscriptions);
        services.AddSingleton(_cache);
        services.AddSingleton(_filters);
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return new BlocklistSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(), fetcher, NullLogger<BlocklistSyncService>.Instance);
    }

    private IBlocklistSyncService CreateService(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        services.AddPublicUrlFetcher();
        services.AddSingleton(_subscriptions);
        services.AddSingleton(_cache);
        services.AddSingleton(_filters);
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return new BlocklistSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IPublicUrlFetcher>(),
            NullLogger<BlocklistSyncService>.Instance);
    }

    private WireMockServer StartListServer(string path)
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/plain").WithBody(ListBody));
        return server;
    }

    #endregion
}
