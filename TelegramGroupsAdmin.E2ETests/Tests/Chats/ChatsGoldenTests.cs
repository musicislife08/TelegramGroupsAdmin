using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Chats;

/// <summary>
/// The Chat Management table on canonical managed chats, as the Owner (who sees every chat). Read-only
/// apart from the queued health-check job, which is asserted at the factory's <c>IJobScheduler</c> mock.
/// Expected rows, ids and statuses are read from this test's clone; the canonical inactive chats are the
/// soft-deleted ones, so they only render under "Show deleted chats".
/// </summary>
[TestFixture]
public class ChatsGoldenTests : GoldenE2ETestBase
{
    private record ChatRow(long ChatId, string Name, string BotStatus, bool IsActive, bool IsDeleted);

    private ChatsPage _chats = null!;
    private List<ChatRow> _all = [];

    // An active chat and a deleted/inactive chat whose names are not substrings of any other chat's name
    // (ChatsPage.ChatRow matches by substring).
    private ChatRow _activeChat = null!;
    private ChatRow _inactiveChat = null!;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var rows = await context.ManagedChats.AsNoTracking().ToListAsync();
        _all = rows
            .Select(c => new ChatRow(c.ChatId, c.ChatName!, c.BotStatus.ToString(), c.IsActive, c.IsDeleted))
            .ToList();

        bool Unique(ChatRow c) => _all.Count(o => o.Name.Contains(c.Name, StringComparison.Ordinal)) == 1;

        _activeChat = _all.First(c => c is { IsActive: true, IsDeleted: false } && Unique(c));
        _inactiveChat = _all.First(c => c is { IsActive: false, IsDeleted: true } && Unique(c));
        Assert.That(_inactiveChat.BotStatus, Is.Not.EqualTo(_activeChat.BotStatus),
            "the anchors must differ in bot status so the chip assertions discriminate");
    }

    [SetUp]
    public async Task OpenChatsAsOwner()
    {
        _chats = new ChatsPage(Page);
        await LoginAsOwnerAsync();
        await _chats.NavigateAsync();
        await _chats.WaitForLoadAsync();
    }

    [Test]
    public async Task Search_ByChatId_ShowsOnlyThatChatWithItsIdCaption()
    {
        var idText = _activeChat.ChatId.ToString();
        // The table's predicate: name or id contains the term, deleted chats hidden by default.
        var expected = _all.Count(c => !c.IsDeleted
            && (c.Name.Contains(idText, StringComparison.OrdinalIgnoreCase) || c.ChatId.ToString().Contains(idText)));
        Assert.That(expected, Is.EqualTo(1));

        await _chats.SearchChatsAsync(idText);

        await _chats.ExpectChatCountAsync(expected);
        await Expect(_chats.ChatName(_activeChat.Name)).ToBeVisibleAsync();
        await Expect(_chats.ChatIdCaption(_activeChat.Name)).ToHaveTextAsync($"ID: {_activeChat.ChatId}");
    }

    [Test]
    public async Task BotStatusChip_ShowsTheStoredStatus_AndInactiveChipOnlyOnInactiveChats()
    {
        // Narrow by name first: the table pages at 10 rows and the anchors may sit on a later page.
        await _chats.SearchChatsAsync(_activeChat.Name);
        await Expect(_chats.BotStatusChip(_activeChat.Name)).ToHaveTextAsync(_activeChat.BotStatus);

        await _chats.SearchChatsAsync(_inactiveChat.Name);
        await Expect(_chats.ChatName(_inactiveChat.Name)).ToHaveCountAsync(0);
        await _chats.ShowDeletedChatsAsync();

        // Deleted chats render now; their row is the post-action settle point for the absence check.
        await Expect(_chats.InactiveChip(_inactiveChat.Name)).ToHaveCountAsync(1);
        await Expect(_chats.BotStatusChip(_inactiveChat.Name)).ToHaveTextAsync(_inactiveChat.BotStatus);

        await _chats.SearchChatsAsync(_activeChat.Name);
        await Expect(_chats.ChatName(_activeChat.Name)).ToBeVisibleAsync();
        await Expect(_chats.InactiveChip(_activeChat.Name)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task RefreshHealth_QueuesAHealthCheckJobForThatChat()
    {
        await _chats.ClickRefreshHealthAsync(_activeChat.Name);

        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("Chat health refresh queued");

        await Factory.MockJobScheduler.Received(1).ScheduleJobAsync(
            BackgroundJobNames.ChatHealthCheck,
            Arg.Is<ChatHealthCheckPayload>(p => p!.ChatId == _activeChat.ChatId),
            0,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
