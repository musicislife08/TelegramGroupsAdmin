using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Single-flight sharing and freshness-bypass behaviour of ProfileScanService.
/// The core scan is observed through ITelegramSessionManager: a null client yields an
/// empty result, which is enough to count how many scans actually ran.
/// </summary>
[TestFixture]
public class ProfileScanServiceSingleFlightTests
{
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private ITelegramUserRepository _users = null!;
    private ServiceProvider _provider = null!;
    private ProfileScanService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = Substitute.For<ITelegramSessionManager>();
        _users = Substitute.For<ITelegramUserRepository>();
        _provider = new ServiceCollection()
            .AddSingleton(_users)
            .AddSingleton(Substitute.For<IConfigService>())
            .BuildServiceProvider();

        _sut = new ProfileScanService(
            _sessions,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new PipelineMetrics(),
            new RecyclableMemoryStreamManager(),
            Substitute.For<IImageProcessor>(),
            NullLogger<ProfileScanService>.Instance);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task ConcurrentScansForOneUser_ShareOneRun()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");

        var first = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        var second = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        gate.SetResult(null);
        await Task.WhenAll(first, second);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
        Assert.That(await second, Is.SameAs(await first));
    }

    [Test]
    public async Task CancellingFirstCaller_DoesNotCancelSharedScanForSecond()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");
        using var cts = new CancellationTokenSource();

        var first = _sut.ScanUserProfileAsync(user, null, cts.Token);
        var second = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        await cts.CancelAsync();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
        gate.SetResult(null);
        var result = await second;

        Assert.That(result, Is.Not.Null);
        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BypassRequest_DoesNotJoinFreshnessEligibleScan()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser() with { ProfileScannedAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        var user = UserIdentity.ForTest(7, "A");

        var plain = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        var bypass = _sut.ScanUserProfileAsync(user, null, CancellationToken.None, bypassFreshness: true);
        gate.SetResult(null);
        await Task.WhenAll(plain, bypass);

        await _sessions.Received(2).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BypassFreshness_ScansEvenWhenRecentlyScanned()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser());
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None, bypassFreshness: true);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithoutBypass_RecentlyScanned_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser());

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None);

        await _sessions.DidNotReceiveWithAnyArgs().GetAnyClientAsync(default);
    }

    private static TelegramUser RecentlyScannedUser()
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: 7, Username: null, FirstName: "A", LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now,
            ProfileScannedAt: now, ProfileScanScore: 1m);
    }
}
