using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using TL;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// The join flow records the joiner's names (ObserveAsync with source ChatMember, which records a
/// rename without scanning) and then runs the join profile scan. A user who renamed since their
/// last scan must have the new name scored: the scan sees the username_history row the join
/// observation wrote. Real repositories, identity service and scan service on canonical data; only
/// the Telegram User API client and the AI scorer are faked. The ObserveAsync call that records the
/// rename is part of the scenario under test (the join flow's own write), not seeded setup.
/// </summary>
[TestFixture]
public class JoinRenameRescanTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private IProfileScoringEngine _scoring = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _sessions = Substitute.For<ITelegramSessionManager>();
        _scoring = Substitute.For<IProfileScoringEngine>();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(0m, ProfileScanOutcome.Clean, 0m, 0m, null, null));

        _provider = new ServiceCollection()
            .AddDataServices(_testHelper.ConnectionString)
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .AddScoped<IProfileScanResultsRepository, ProfileScanResultsRepository>()
            .AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>()
            .AddScoped<IUserIdentityService, UserIdentityService>()
            .AddSingleton(Substitute.For<IProfileScanGate>())
            .AddSingleton(_scoring)
            .AddSingleton(Substitute.For<IConfigService>())
            .AddSingleton(Substitute.For<IBotModerationService>())
            .AddSingleton(Substitute.For<IReportsRepository>())
            .AddSingleton(Substitute.For<IAdminNotificationService>())
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task Join_AfterRenameSinceLastScan_ScoresTheNewName()
    {
        var id = GoldenDatasetConstants.IdentityService.ScannedCleanUserId;
        const string newName = "Free Crypto Signals";
        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.Multiple(() =>
        {
            Assert.That(row.IsTrusted, Is.False);
            Assert.That(row.ProfileScanScore, Is.Not.Null);
            Assert.That(row.ProfileScannedAt, Is.LessThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.That(row.FirstName, Is.Not.EqualTo(newName));
        });
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        // The live profile matches the stored one except for the new first name.
        ClientReturning(new TL.User { id = id, access_hash = 1, first_name = newName, username = row.Username });
        var chat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId);

        // WelcomeService Step 2, then Step 9: record the joiner's names, then scan on join.
        await using var scope = _provider!.CreateAsyncScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ObserveAsync(
            new ObservedUser(id, newName, null, row.Username, IsBot: false, ObservationSource.ChatMember, DateTimeOffset.UtcNow),
            new ProfileChangeContext(chat, MessageId: null));

        // The join observation records the rename and does not scan (source ChatMember).
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
        await _provider!.GetRequiredService<IProfileScanGate>().DidNotReceiveWithAnyArgs()
            .ScanIfEligibleAsync(default!, default, default, default, default);

        await NewScanService().ScanUserProfileAsync(identity, triggeringChat: null, CancellationToken.None);

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    [Test]
    public async Task Join_WithoutRename_ReusesCachedScore()
    {
        // Same harness, no rename: the join observation carries the stored names and the live
        // profile is unchanged, so the join scan reuses the cached score.
        var id = GoldenDatasetConstants.IdentityService.ScannedCleanUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.Multiple(() =>
        {
            Assert.That(row.IsTrusted, Is.False);
            Assert.That(row.ProfileScanScore, Is.Not.Null);
            Assert.That(row.ProfileScannedAt, Is.LessThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
        ClientReturning(new TL.User
        {
            id = id, access_hash = 1, first_name = row.FirstName, last_name = row.LastName, username = row.Username
        });
        var chat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId);

        await using var scope = _provider!.CreateAsyncScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ObserveAsync(
            new ObservedUser(id, row.FirstName, row.LastName, row.Username, IsBot: false, ObservationSource.ChatMember,
                DateTimeOffset.UtcNow),
            new ProfileChangeContext(chat, MessageId: null));
        await NewScanService().ScanUserProfileAsync(identity, triggeringChat: null, CancellationToken.None);

        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        await _scoring.DidNotReceiveWithAnyArgs().ScoreAsync(default!, default!, default, default, default, default);
    }

    [Test]
    public async Task Scan_WhenStoredNamesPredateTheRename_ScoresTheNewName()
    {
        // Control: the same scan with the stored row still holding the old name. Proves the
        // harness reaches scoring, so the join test above isolates the observe-first order.
        var id = GoldenDatasetConstants.IdentityService.ScannedCleanUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(row.FirstName, Is.Not.EqualTo("Free Crypto Signals"));
        ClientReturning(new TL.User { id = id, access_hash = 1, first_name = "Free Crypto Signals", username = row.Username });

        await NewScanService().ScanUserProfileAsync(
            UserIdentity.ForTest(id, row.FirstName, row.LastName, row.Username), triggeringChat: null, CancellationToken.None);

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    private ProfileScanService NewScanService() => new(
        _sessions,
        _provider!.GetRequiredService<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        Substitute.For<IImageProcessor>(),
        NullLogger<ProfileScanService>.Instance);

    // Resolves through the username strategy (no triggering chat), then returns the full user
    // with an empty UserFull: no bio, personal channel 0, no stories — the canonical row's profile.
    private void ClientReturning(TL.User liveUser)
    {
        var client = Substitute.For<IWTelegramApiClient>();
        client.Contacts_ResolveUsername(liveUser.username).Returns(new Contacts_ResolvedPeer
        {
            peer = new PeerUser { user_id = liveUser.id },
            users = new Dictionary<long, TL.User> { [liveUser.id] = liveUser },
            chats = new Dictionary<long, ChatBase>()
        });
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).Returns(new Users_UserFull
        {
            full_user = new UserFull(),
            users = new Dictionary<long, TL.User> { [liveUser.id] = liveUser },
            chats = new Dictionary<long, ChatBase>()
        });
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(client);
        _sessions.GetClientForChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(client);
    }
}
