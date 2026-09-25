using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using Anchors = TelegramGroupsAdmin.IntegrationTests.TestData.GoldenDatasetConstants.DmCelebrations;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for <see cref="BanCelebrationSubscriberRepository"/> against the canonical
/// dataset. Anchors: <c>canonical/36_ban_celebration_subscribers.sql</c> (approved canonical
/// addition 2026-09-25). SetUp re-reads every anchor's shape so a canonical change fails loudly.
/// </summary>
[TestFixture]
public class BanCelebrationSubscriberRepositoryTests
{
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IBanCelebrationSubscriberRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IBanCelebrationSubscriberRepository>();

        await AssertCanonicalAnchorsAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task AssertCanonicalAnchorsAsync()
    {
        await using var ctx = _testHelper!.GetDbContext();

        var rows = await ctx.BanCelebrationSubscribers.AsNoTracking()
            .Select(s => new { s.TelegramUserId, s.ChatId })
            .ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(5), "canonical ban_celebration_subscribers changed");

        var users = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == Anchors.DeliverableSubscriberId
                        || u.TelegramUserId == Anchors.UndeliverableSubscriberId
                        || u.TelegramUserId == Anchors.TwoChatSubscriberId
                        || u.TelegramUserId == Anchors.UnsubscribedMemberId
                        || u.TelegramUserId == Anchors.BannedSubscriberId)
            .ToDictionaryAsync(u => u.TelegramUserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(users[Anchors.DeliverableSubscriberId].BotDmEnabled, Is.True);
            Assert.That(users[Anchors.DeliverableSubscriberId].IsBanned, Is.False);
            Assert.That(users[Anchors.UndeliverableSubscriberId].BotDmEnabled, Is.False);
            Assert.That(users[Anchors.TwoChatSubscriberId].BotDmEnabled, Is.False);
            Assert.That(users[Anchors.UnsubscribedMemberId].BotDmEnabled, Is.True);
            Assert.That(users[Anchors.BannedSubscriberId].BotDmEnabled, Is.True);
            Assert.That(users[Anchors.BannedSubscriberId].IsBanned, Is.True);
            Assert.That(rows.Any(r => r.TelegramUserId == Anchors.BannedSubscriberId && r.ChatId == Anchors.WorkshopAlumniChatId), Is.True);
        }
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithDmEnabledSubscriber_ReturnsTrue()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(Anchors.WorkshopAlumniChatId), Is.True);
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithOnlyDmDisabledSubscribers_ReturnsFalse()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(Anchors.PoultryCommunityChatId), Is.False);
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithNoSubscribers_ReturnsFalse()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(MainChatId), Is.False);
    }

    [Test]
    public async Task GetDeliverableSubscribersAsync_ExcludesBannedSubscriberEvenWithDmsEnabled()
    {
        // Workshop Alumni holds a row for a banned user with DMs enabled (the ban-time removal
        // never ran). A banned user must never receive a celebration DM, whatever the row says.
        var subscribers = await _repository.GetDeliverableSubscribersAsync(Anchors.WorkshopAlumniChatId);

        Assert.That(subscribers.Select(s => s.Id), Does.Not.Contain(Anchors.BannedSubscriberId));
    }

    [Test]
    public async Task GetDeliverableSubscribersAsync_ReturnsOnlyDmEnabledSubscribersWithIdentity()
    {
        var subscribers = await _repository.GetDeliverableSubscribersAsync(Anchors.WorkshopAlumniChatId);

        Assert.That(subscribers, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subscribers[0].Id, Is.EqualTo(Anchors.DeliverableSubscriberId));
            Assert.That(subscribers[0].Username, Is.EqualTo("magnetismvoucher"));
        }
    }

    [Test]
    public async Task UpsertAsync_NewSubscription_CreatesRowAndReturnsTrue()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var created = await _repository.UpsertAsync(Anchors.UnsubscribedMemberId, MainChatId);

        var row = await _repository.GetAsync(Anchors.UnsubscribedMemberId, MainChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.True);
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.SubscribedAt, Is.GreaterThan(before));
            Assert.That(row.PromptMessageId, Is.Null);
            Assert.That(row.PromptDeleteJobId, Is.Null);
        }
    }

    [Test]
    public async Task UpsertAsync_ExistingSubscription_ReturnsFalseAndLeavesRowUntouched()
    {
        var created = await _repository.UpsertAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);

        var row = await _repository.GetAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.False);
            Assert.That(row!.PromptMessageId, Is.EqualTo(Anchors.StalePromptMessageId));
            Assert.That(row.PromptDeleteJobId, Is.EqualTo(Anchors.StalePromptJobId));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesOnlyThatChatsRow()
    {
        var deleted = await _repository.DeleteAsync(Anchors.TwoChatSubscriberId, Anchors.PoultryCommunityChatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _repository.GetAsync(Anchors.TwoChatSubscriberId, Anchors.PoultryCommunityChatId), Is.Null);
            Assert.That(await _repository.GetAsync(Anchors.TwoChatSubscriberId, Anchors.WorkshopAlumniChatId), Is.Not.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_NoRow_ReturnsFalse()
    {
        Assert.That(await _repository.DeleteAsync(Anchors.UnsubscribedMemberId, MainChatId), Is.False);
    }

    [Test]
    public async Task DeleteAllForUserAsync_RemovesEveryChatAndReturnsCount()
    {
        var removed = await _repository.DeleteAllForUserAsync(Anchors.TwoChatSubscriberId);

        await using var ctx = _testHelper!.GetDbContext();
        var remaining = await ctx.BanCelebrationSubscribers.CountAsync(s => s.TelegramUserId == Anchors.TwoChatSubscriberId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(remaining, Is.Zero);
        }
    }

    [Test]
    public async Task SetPromptAsync_StoresMessageAndJobIds()
    {
        await _repository.SetPromptAsync(Anchors.DeliverableSubscriberId, Anchors.WorkshopAlumniChatId, 5150, "job-5150");

        var row = await _repository.GetAsync(Anchors.DeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row!.PromptMessageId, Is.EqualTo(5150));
            Assert.That(row.PromptDeleteJobId, Is.EqualTo("job-5150"));
        }
    }

    [Test]
    public async Task ClearPromptAsync_NullsBothPromptColumns()
    {
        await _repository.ClearPromptAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);

        var row = await _repository.GetAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row!.PromptMessageId, Is.Null);
            Assert.That(row.PromptDeleteJobId, Is.Null);
        }
    }
}
