using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
/// The rename audit is written by the identity service after the repository has recorded the names,
/// so it never carries a message id (the message is stored later, and a ProfileChange row pointing at
/// it would fail the composite foreign key). Real repositories and identity service on canonical
/// data; the rescan gate is substituted. Each ObserveAsync here is the assertion subject.
/// </summary>
[TestFixture]
public class UserIdentityServiceRenameAuditTests
{
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
    public async Task RenameThenRenameBack_WritesTwoHistoryAndTwoAuditRows()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        var mainChat = GoldenDatasetConstants.Chats.MainChatId;
        var profileChange = (int)UserActionType.ProfileChange;
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.FirstName, Is.Not.EqualTo("Renamed"));
        var historyBefore = await ctx.UsernameHistory.CountAsync(h => h.UserId == id);
        var auditBefore = await ctx.UserActions.CountAsync(a => a.UserId == id && a.ActionType == profileChange);
        var context = new ProfileChangeContext(ChatIdentity.FromId(mainChat));
        var t = DateTimeOffset.UtcNow;

        await using var scope = _provider!.CreateAsyncScope();
        var identities = scope.ServiceProvider.GetRequiredService<IUserIdentityService>();
        await identities.ObserveAsync(
            new ObservedUser(id, "Renamed", before.LastName, before.Username, IsBot: false, ObservationSource.ChatMember, t),
            context);
        await identities.ObserveAsync(
            new ObservedUser(id, before.FirstName, before.LastName, before.Username, IsBot: false, ObservationSource.ChatMember, t.AddSeconds(1)),
            context);

        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(historyBefore + 2));
        var audit = await ctx.UserActions.AsNoTracking()
            .Where(a => a.UserId == id && a.ActionType == profileChange).ToListAsync();
        Assert.That(audit, Has.Count.EqualTo(auditBefore + 2));
        var written = audit.Where(a => a.Reason != null && a.Reason.Contains("Renamed")).ToList();
        Assert.That(written, Has.Count.EqualTo(2));
        Assert.That(written.Select(a => a.ChatId), Is.All.EqualTo(mainChat));
        Assert.That(written.Select(a => a.MessageId), Is.All.Null);
    }
}
