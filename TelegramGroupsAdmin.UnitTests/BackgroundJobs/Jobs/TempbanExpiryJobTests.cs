using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

[TestFixture]
public class TempbanExpiryJobTests
{
    private IBotModerationService _moderation = null!;
    private IUserIdentityService _identities = null!;
    private TempbanExpiryJob _job = null!;

    [SetUp]
    public void SetUp()
    {
        _moderation = Substitute.For<IBotModerationService>();
        _identities = Substitute.For<IUserIdentityService>();
        _job = new TempbanExpiryJob(NullLogger<TempbanExpiryJob>.Instance, _moderation, _identities, new JobMetrics());
    }

    private static IJobExecutionContext ContextWith(TempbanExpiryJobPayload payload)
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap { { JobDataKeys.PayloadJson, JsonSerializer.Serialize(payload) } });
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    [Test]
    public async Task Execute_ResolvesUserByIdBeforeUnbanning()
    {
        // The payload holds the identity captured when the tempban was issued; the unban
        // uses the identity re-resolved by id.
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(UserIdentity.ForTest(7, "Current"));
        _moderation.UnbanUserAsync(Arg.Any<UnbanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        await _job.Execute(ContextWith(new TempbanExpiryJobPayload(UserIdentity.ForTest(7, "Stale"), "spam", DateTimeOffset.UtcNow)));

        await _moderation.Received(1).UnbanUserAsync(
            Arg.Is<UnbanIntent>(i => i!.User.Id == 7 && i.User.DisplayName == "Current"), Arg.Any<CancellationToken>());
    }
}
