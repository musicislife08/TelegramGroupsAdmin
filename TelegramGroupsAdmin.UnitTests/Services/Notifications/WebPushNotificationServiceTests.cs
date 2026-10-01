using System.Net;
using System.Security.Cryptography;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Http;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using PushSubscription = TelegramGroupsAdmin.Core.Models.PushSubscription;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

/// <summary>
/// The server POSTs to whatever endpoint a push subscription row holds, and that endpoint came
/// from a user's browser. Sending is guarded twice: a stored endpoint that fails the endpoint
/// policy (non-https, non-public IP literal) is skipped and removed like an expired one, and the
/// push client's connections go through the public-url handler so a hostname that resolves to a
/// loopback or private address is refused at the socket. Neither path throws out of SendAsync.
/// </summary>
[TestFixture]
public class WebPushNotificationServiceTests
{
    private readonly List<IDisposable> _disposables = [];
    private IWebNotificationRepository _notifications = null!;
    private IPushSubscriptionsRepository _subscriptions = null!;
    private ISystemConfigRepository _config = null!;
    private IUserRepository _users = null!;
    private UserRecord _user = null!;
    private VapidAuthentication _vapid = null!;

    [SetUp]
    public void SetUp()
    {
        _notifications = Substitute.For<IWebNotificationRepository>();
        _subscriptions = Substitute.For<IPushSubscriptionsRepository>();
        _config = Substitute.For<ISystemConfigRepository>();
        _users = Substitute.For<IUserRepository>();
        _user = CreateTestUser("owner-1");

        var (publicKey, privateKey) = GenerateVapidKeys();
        _vapid = new VapidAuthentication(publicKey, privateKey) { Subject = "mailto:admin@example.com" };
        _config.GetWebPushConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new WebPushConfig { Enabled = true, VapidPublicKey = publicKey, ContactEmail = "admin@example.com" });
        _config.HasVapidKeysAsync(Arg.Any<CancellationToken>()).Returns(true);
        _config.GetVapidPrivateKeyAsync(Arg.Any<CancellationToken>()).Returns(privateKey);
    }

    [TearDown]
    public void DisposeAll()
    {
        _vapid.Dispose();
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
    }

    [TestCase("http://localhost:{port}/push")]
    [TestCase("https://127.0.0.1:{port}/push")]
    [TestCase("https://[::1]:{port}/push")]
    [TestCase("https://10.0.0.1/push")]
    [TestCase("https://169.254.169.254/latest/meta-data/")]
    public async Task SendAsync_StoredEndpointFailingThePolicy_IsSkippedAndRemovedWithoutARequest(string template)
    {
        var server = StartPushServer();
        var endpoint = template.Replace("{port}", server.Port.ToString());
        StoreSubscription(endpoint);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True, "the in-app notification is still created; only the browser push is skipped");
        Assert.That(server.LogEntries, Is.Empty, "the endpoint must never see a request");
        await _subscriptions.Received(1).DeleteByEndpointAsync(endpoint, Arg.Any<CancellationToken>());
        await _notifications.Received(1).CreateAsync(Arg.Any<WebNotification>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_HandlerRefusesTheResolvedAddress_IsLoggedAndTheSubscriptionIsKept()
    {
        // A fully qualified endpoint passes the policy; if its name resolves to a non-public
        // address the guarded handler refuses to connect (a PublicUrlFetchException wrapped in
        // HttpRequestException, as SocketsHttpHandler surfaces a ConnectCallback failure). DNS can
        // be transient, so the subscription is kept and SendAsync still succeeds.
        const string endpoint = "https://push.example.test/sub/1";
        var handler = new StubHandler(_ => throw new HttpRequestException("connect failed",
            new PublicUrlFetchException(PublicUrlFetchFailure.NotAllowed, PublicUrlFetchException.NotAllowedMessage,
                "'push.example.test:443' only resolves to non-public addresses: 127.0.0.1 is loopback (127.0.0.0/8)")));
        StoreSubscription(endpoint);
        var sut = CreateService(handler);

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        Assert.That(handler.Requests.Select(r => r.RequestUri!.AbsoluteUri), Is.EqualTo(new[] { endpoint }));
        await _subscriptions.DidNotReceiveWithAnyArgs().DeleteByEndpointAsync(default!, default);
        await _notifications.Received(1).CreateAsync(Arg.Any<WebNotification>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_DeliverableEndpoint_IsPushedAndKept()
    {
        const string endpoint = "https://push.example.test/sub/1";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        StoreSubscription(endpoint);
        var sut = CreateService(handler);

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        Assert.That(handler.Requests.Select(r => (r.Method, r.RequestUri!.AbsoluteUri)), Is.EqualTo(new[] { (HttpMethod.Post, endpoint) }));
        await _subscriptions.DidNotReceiveWithAnyArgs().DeleteByEndpointAsync(default!, default);
    }

    [Test]
    public async Task SendAsync_OneBadEndpointAmongGood_SkipsOnlyTheBadOne()
    {
        const string bad = "https://127.0.0.1:8443/push";
        const string good = "https://push.example.test/sub/2";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        StoreSubscription(bad, good);
        var sut = CreateService(handler);

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        Assert.That(handler.Requests.Select(r => r.RequestUri!.AbsoluteUri), Is.EqualTo(new[] { good }));
        await _subscriptions.Received(1).DeleteByEndpointAsync(bad, Arg.Any<CancellationToken>());
        await _subscriptions.DidNotReceive().DeleteByEndpointAsync(good, Arg.Any<CancellationToken>());
    }

    [Test]
    public void ProductionPushClient_LoopbackEndpoint_IsRefusedWithoutTheAllowance()
    {
        // Resolves PushServiceClient from the production AddWebPushClient() registration and
        // pushes straight through the library, bypassing the service's endpoint check, so only
        // the client's own handler stands between it and the socket.
        var server = StartPushServer();
        var client = CreateProductionPushClient();

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => client.RequestPushMessageDeliveryAsync(
            LibrarySubscription($"http://localhost:{server.Port}/push"), new PushMessage("hi"), _vapid));

        Assert.That(FindRefusal(ex!), Is.Not.Null, "the connect callback's refusal must be the cause");
        Assert.That(server.LogEntries, Is.Empty, "the push service must never see a request");
    }

    [Test]
    public async Task ProductionPushClient_LoopbackEndpoint_IsDeliveredThroughTheAllowance()
    {
        var server = StartPushServer();
        var client = CreateProductionPushClient(new LoopbackPortAllowance(server.Port));

        await client.RequestPushMessageDeliveryAsync(
            LibrarySubscription($"http://localhost:{server.Port}/push"), new PushMessage("hi"), _vapid);

        Assert.That(server.LogEntries.Select(e => e.RequestMessage!.Path), Is.EqualTo(new[] { "/push" }));
    }

    [Test]
    public void ProductionPushClient_PinsHttp11()
    {
        // .NET's defaults already are 1.1 / RequestVersionOrLower, so an earlier registration
        // asks for HTTP/3 to prove the production registration overrides it.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<PushServiceClient>().ConfigureHttpClient(c =>
        {
            c.DefaultRequestVersion = HttpVersion.Version30;
            c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        });
        services.AddWebPushClient();
        using var provider = services.BuildServiceProvider();

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PushServiceClient));

        Assert.That(http.DefaultRequestVersion, Is.EqualTo(HttpVersion.Version11));
        Assert.That(http.DefaultVersionPolicy, Is.EqualTo(HttpVersionPolicy.RequestVersionOrLower));
    }

    private PushServiceClient CreateProductionPushClient(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        services.AddWebPushClient();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return provider.GetRequiredService<PushServiceClient>();
    }

    private static Lib.Net.Http.WebPush.PushSubscription LibrarySubscription(string endpoint)
    {
        var (p256dh, auth) = GenerateSubscriberKeys();
        return new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = endpoint,
            Keys = new Dictionary<string, string> { ["p256dh"] = p256dh, ["auth"] = auth }
        };
    }

    private static PublicUrlFetchException? FindRefusal(Exception ex)
    {
        for (Exception? inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is PublicUrlFetchException refused) return refused;
        }
        return null;
    }

    #region Helpers

    private void StoreSubscription(params string[] endpoints)
    {
        var (p256dh, auth) = GenerateSubscriberKeys();
        var rows = endpoints.Select((endpoint, i) => new PushSubscription
        {
            Id = i + 1,
            UserId = _user.WebUser.Id,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth
        }).ToList();
        _subscriptions.GetByUserIdAsync(_user.WebUser.Id, Arg.Any<CancellationToken>())
            .Returns(rows);
    }

    private IWebPushNotificationService CreateService(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        _disposables.Add(http);
        return CreateService(new PushServiceClient(http));
    }

    private IWebPushNotificationService CreateService(PushServiceClient pushClient) =>
        new WebPushNotificationService(
            _notifications,
            _subscriptions,
            _config,
            _users,
            pushClient,
            NullLogger<WebPushNotificationService>.Instance);

    private IWebPushNotificationService CreateService(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        // The production registration, not a copy of it.
        services.AddWebPushClient();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);

        return CreateService(provider.GetRequiredService<PushServiceClient>());
    }

    private WireMockServer StartPushServer()
    {
        var server = WireMockServer.Start();
        _disposables.Add(server);
        server.Given(Request.Create().WithPath("/push").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));
        return server;
    }

    private static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, publicKey, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, publicKey, 33, 32);
        return (Base64Url(publicKey), Base64Url(p.D!));
    }

    private static (string P256dh, string Auth) GenerateSubscriberKeys()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdh.ExportParameters(includePrivateParameters: false);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, publicKey, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, publicKey, 33, 32);
        return (Base64Url(publicKey), Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static UserRecord CreateTestUser(string id) =>
        new(
            WebUser: new WebUserIdentity(id, $"{id}@test.com", PermissionLevel.Owner),
            NormalizedEmail: $"{id}@TEST.COM",
            PasswordHash: "hash",
            SecurityStamp: "stamp",
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: false,
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: true,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null);

    /// <summary>Stands in for the push service: records each request and answers (or throws) as configured.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    #endregion
}
