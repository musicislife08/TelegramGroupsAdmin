using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// A replied-to user the bot never observed (its message arrived while the app was down) is recorded
/// by the reply observation, so the ban status that follows lands on a real telegram_users row.
/// Real repositories and identity service on canonical data; the rescan gate is substituted.
/// </summary>
[TestFixture]
public class ReplyTargetObservationTests
{
    // Outside the canonical user range, so no row exists until the observation creates it
    private const long NeverObservedId = 4242424243L;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _provider = new ServiceCollection()
            .AddDataServices(_testHelper.ConnectionString)
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .AddScoped<IUserActionsRepository, UserActionsRepository>()
            .AddScoped<IUserIdentityService, UserIdentityService>()
            .AddSingleton(Substitute.For<IProfileScanGate>())
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
    public async Task NeverObservedReplyTarget_IsRecorded_AndCanBeBanned()
    {
        await using (var before = _testHelper!.GetDbContext())
            Assert.That(await before.TelegramUsers.AnyAsync(u => u.TelegramUserId == NeverObservedId), Is.False);
        var context = new ProfileChangeContext(ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId));

        await using var scope = _provider!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ObserveAsync(
            new ObservedUser(NeverObservedId, "Reply", "Target", "reply_target", IsBot: false,
                ObservationSource.ReplyTarget, DateTimeOffset.UtcNow),
            context);
        await scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
            .SetBanStatusAsync(NeverObservedId, isBanned: true);

        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == NeverObservedId);
        Assert.That(row.FirstName, Is.EqualTo("Reply"));
        Assert.That(row.LastName, Is.EqualTo("Target"));
        Assert.That(row.Username, Is.EqualTo("reply_target"));
        Assert.That(row.IsBanned, Is.True);
        Assert.That(row.IsTrusted, Is.False);
    }

    [Test]
    public async Task SystemAccountObservedAsReplyTarget_IsTrusted()
    {
        const long systemId = TelegramConstants.GroupAnonymousBotUserId;
        await using (var before = _testHelper!.GetDbContext())
            Assert.That(await before.TelegramUsers.AnyAsync(u => u.TelegramUserId == systemId), Is.False);

        await using var scope = _provider!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ObserveAsync(
            new ObservedUser(systemId, "Group", null, "GroupAnonymousBot", IsBot: true,
                ObservationSource.ReplyTarget, DateTimeOffset.UtcNow),
            new ProfileChangeContext(ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId)));

        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == systemId);
        Assert.That(row.IsTrusted, Is.True);
    }
}
