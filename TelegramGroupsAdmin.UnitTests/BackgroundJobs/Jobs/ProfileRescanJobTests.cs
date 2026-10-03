using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

[TestFixture]
public class ProfileRescanJobTests
{
#pragma warning disable NUnit1032 // NSubstitute substitute, nothing to dispose
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private ITelegramUserRepository _users = null!;
    private IProfileScanService _scanner = null!;
    private IUserIdentityService _identities = null!;
    private ProfileRescanJob _job = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = Substitute.For<ITelegramSessionManager>();
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);
        _users = Substitute.For<ITelegramUserRepository>();
        _scanner = Substitute.For<IProfileScanService>();
        _scanner.ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ci => new ProfileScanResult(ci.Arg<UserIdentity>().Id, null, null, null, null, false, null,
                false, false, false, 0m, ProfileScanOutcome.Clean, null, null));
        _identities = Substitute.For<IUserIdentityService>();

        _job = new ProfileRescanJob(NullLogger<ProfileRescanJob>.Instance, Substitute.For<IBackgroundJobConfigService>(),
            _sessions, _users, _scanner, _identities, new JobMetrics());
    }

    [Test]
    public async Task Execute_ScansIdentitiesResolvedOnceForTheBatch()
    {
        // The batch's ids are resolved in one call; each scan gets that user's resolved identity.
        _users.GetEligibleUsersForRescanAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([7L, 8L]);
        var seven = UserIdentity.ForTest(7, "Current Seven");
        var eight = UserIdentity.ForTest(8, "Current Eight");
        _identities.ResolveManyAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids!.SequenceEqual(new[] { 7L, 8L })), Arg.Any<CancellationToken>())
            .Returns([seven, eight]);
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        await _job.Execute(context);

        await _identities.Received(1).ResolveManyAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default);
        await _scanner.Received(1).ScanUserProfileAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.Received(1).ScanUserProfileAsync(eight, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }
}
