using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Lib.Net.Http.WebPush;
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

    [SetUp]
    public void SetUp()
    {
        _notifications = Substitute.For<IWebNotificationRepository>();
        _subscriptions = Substitute.For<IPushSubscriptionsRepository>();
        _config = Substitute.For<ISystemConfigRepository>();
        _users = Substitute.For<IUserRepository>();
        _user = CreateTestUser("owner-1");

        var (publicKey, privateKey) = GenerateVapidKeys();
        _config.GetWebPushConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new WebPushConfig { Enabled = true, VapidPublicKey = publicKey, ContactEmail = "admin@example.com" });
        _config.HasVapidKeysAsync(Arg.Any<CancellationToken>()).Returns(true);
        _config.GetVapidPrivateKeyAsync(Arg.Any<CancellationToken>()).Returns(privateKey);
    }

    [TearDown]
    public void DisposeAll()
    {
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
    public async Task SendAsync_StoredEndpointResolvingToLoopback_IsRefusedAtConnectTimeWithoutTheAllowance()
    {
        // "localhost" passes the endpoint policy (it is a hostname), so only the guarded handler
        // can stop the POST: no socket is opened, the subscription is kept (DNS can be transient),
        // and SendAsync still succeeds for the in-app notification.
        using var listener = new ConnectionCounter();
        StoreSubscription($"https://localhost:{listener.Port}/push");
        var sut = CreateService();

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        Assert.That(await listener.ConnectionWithin(TimeSpan.FromSeconds(1)), Is.False, "no connection may be opened");
        await _subscriptions.DidNotReceiveWithAnyArgs().DeleteByEndpointAsync(default!, default);
    }

    [Test]
    public async Task SendAsync_StoredEndpointResolvingToLoopback_ConnectsThroughTheAllowance()
    {
        // Same endpoint, allowance registered: the handler opens the socket (the TLS handshake
        // then fails against the bare listener, which is not what is under test). This pins
        // that the allowance is the only thing between the push client and the socket.
        using var listener = new ConnectionCounter();
        StoreSubscription($"https://localhost:{listener.Port}/push");
        var sut = CreateService(new LoopbackPortAllowance(listener.Port));

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        Assert.That(await listener.ConnectionWithin(TimeSpan.FromSeconds(5)), Is.True, "the allowance opens the port");
        await _subscriptions.DidNotReceiveWithAnyArgs().DeleteByEndpointAsync(default!, default);
    }

    [Test]
    public async Task SendAsync_OneBadEndpointAmongGood_SkipsOnlyTheBadOne()
    {
        var server = StartPushServer();
        var bad = $"https://127.0.0.1:{server.Port}/push";
        var good = $"https://localhost:{server.Port}/push";
        StoreSubscription(bad, good);
        var sut = CreateService(new LoopbackPortAllowance(server.Port));

        var sent = await sut.SendAsync(_user, NotificationEventType.SpamDetected, "subject", "body");

        Assert.That(sent, Is.True);
        await _subscriptions.Received(1).DeleteByEndpointAsync(bad, Arg.Any<CancellationToken>());
        await _subscriptions.DidNotReceive().DeleteByEndpointAsync(good, Arg.Any<CancellationToken>());
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

    private IWebPushNotificationService CreateService(IPublicUrlFetchAllowance? allowance = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (allowance != null)
        {
            services.AddSingleton(allowance);
        }
        // Mirrors the production registration in ServiceCollectionExtensions.
        services.AddHttpClient<PushServiceClient>().UsePublicUrlPolicy();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);

        return new WebPushNotificationService(
            _notifications,
            _subscriptions,
            _config,
            _users,
            provider.GetRequiredService<PushServiceClient>(),
            NullLogger<WebPushNotificationService>.Instance);
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

    /// <summary>A bare loopback listener that only records whether anything connected to it.</summary>
    private sealed class ConnectionCounter : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<bool> _connected = new();

        public ConnectionCounter()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        public int Port { get; }

        public async Task<bool> ConnectionWithin(TimeSpan wait)
        {
            try
            {
                return await _connected.Task.WaitAsync(wait);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                _connected.TrySetResult(true);
            }
            catch (Exception)
            {
                // Listener stopped before anything connected.
                _connected.TrySetResult(false);
            }
        }

        public void Dispose() => _listener.Stop();
    }

    #endregion
}
