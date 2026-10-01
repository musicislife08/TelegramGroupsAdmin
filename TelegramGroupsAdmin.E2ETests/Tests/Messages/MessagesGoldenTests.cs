using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Messages;

/// <summary>
/// The Messages page's chat sidebar on canonical messages, as the Owner (who sees every non-deleted chat).
/// Read-only. Each chat's last-message preview is the chat's newest message — its media display name, else
/// its text, truncated to 40 characters plus "..." — and the expected previews are derived from this test's
/// clone, never pasted. Chats are picked by data shape and found by exact title, so the test names no chat.
/// </summary>
[TestFixture]
public class MessagesGoldenTests : GoldenE2ETestBase
{
    private record Anchor(string ChatName, string ExpectedPreview);

    private MessagesPage _messages = null!;
    private Anchor _truncatedText = null!;
    private Anchor _otherTruncatedText = null!;
    private string _emptyChatName = null!;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var chats = await context.ManagedChats.AsNoTracking().Where(c => !c.IsDeleted)
            .Select(c => new { c.ChatId, c.ChatName }).ToListAsync();
        var chatNames = chats.Select(c => c.ChatName!).ToList();

        Assert.That(chatNames, Is.Unique, "the page object finds chats by exact title");

        // Classify EVERY chat's newest message, so a canonical change that adds a shape this test does not
        // assert fails here instead of passing blind.
        var shapes = new Dictionary<string, string>();
        var longAnchors = new List<Anchor>();
        foreach (var chat in chats)
        {
            var top = await context.Messages.AsNoTracking()
                .Where(m => m.ChatId == chat.ChatId)
                .OrderByDescending(m => m.Timestamp)
                .Select(m => new { m.Timestamp, m.MessageText, m.MediaType, m.PhotoFileId })
                .Take(2).ToListAsync();

            string shape;
            if (top.Count == 0)
            {
                shape = "empty";
            }
            else if (top.Count > 1 && top[0].Timestamp == top[1].Timestamp)
            {
                shape = "tied-newest";
            }
            else if (top[0].MediaType is not null)
            {
                shape = top[0].MediaType == MediaType.None ? "media-none" : "media";
            }
            else if (string.IsNullOrWhiteSpace(top[0].MessageText))
            {
                shape = string.IsNullOrEmpty(top[0].PhotoFileId) ? "blank-text" : "photo-only";
            }
            else if (top[0].MessageText!.Length > 40)
            {
                var text = top[0].MessageText!;
                shape = CutsASurrogatePair(text) ? "long-surrogate-cut" : "long";
                if (shape == "long")
                {
                    longAnchors.Add(new Anchor(chat.ChatName!, Normalize(text[..40] + "...")));
                }
            }
            else
            {
                shape = "short";
            }
            shapes[chat.ChatName!] = shape;
        }

        // Asserted below: long text (truncated) and empty chats. Ties are skipped (no defined "newest").
        // Anything else (short, media, photo-only, blank) has no assertion here yet.
        var unasserted = shapes.Where(kv => kv.Value is not ("long" or "empty" or "tied-newest")).ToList();
        Assert.That(unasserted, Is.Empty,
            "canonical now carries a newest-message shape this test does not assert: extend the preview assertions ("
            + string.Join(", ", unasserted.Select(kv => kv.Value).Distinct()) + ")");

        Assert.That(longAnchors, Has.Count.GreaterThanOrEqualTo(2));
        _truncatedText = longAnchors[0];
        _otherTruncatedText = longAnchors[1];
        Assert.That(_truncatedText.ExpectedPreview, Is.Not.EqualTo(_otherTruncatedText.ExpectedPreview));

        _emptyChatName = shapes.OrderBy(kv => kv.Key, StringComparer.Ordinal).First(kv => kv.Value == "empty").Key;
    }

    // HTML collapses whitespace runs and Playwright's string text assertion normalises them, so compare
    // normalised text; a cut inside a surrogate pair would render a replacement character.
    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool CutsASurrogatePair(string text) =>
        text.Length > 40 && (char.IsHighSurrogate(text[39]) || char.IsLowSurrogate(text[39]));

    [SetUp]
    public void CreatePageObject() => _messages = new MessagesPage(Page);

    [Test]
    public async Task ChatList_ShowsTheNewestMessageAsEachChatsPreview_TruncatedPastFortyCharacters_AndNoMessagesYetForAnEmptyChat()
    {
        await LoginAsOwnerAsync();
        await _messages.NavigateAsync();

        await Expect(_messages.ChatLastMessagePreview(_truncatedText.ChatName)).ToHaveTextAsync(_truncatedText.ExpectedPreview);
        await Expect(_messages.ChatLastMessagePreview(_otherTruncatedText.ChatName)).ToHaveTextAsync(_otherTruncatedText.ExpectedPreview);
        await Expect(_messages.ChatLastMessagePreview(_emptyChatName)).ToHaveTextAsync("No messages yet");
    }

    [Test]
    public async Task OpenChat_OnAPhoneViewport_FitsTheViewportWithTheInputReachable()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await LoginAsOwnerAsync();
        await _messages.NavigateAsync();
        await _messages.ChatItem(_truncatedText.ChatName).ClickAsync();
        await _messages.WaitForChatViewActiveAsync();
        await Expect(_messages.SelectedChatTitle).ToHaveTextAsync(_truncatedText.ChatName);

        // The pane is bounded by the layout, so the message list scrolls inside it and the input stays on screen.
        // Ratio 1 = entirely inside the viewport: an unbounded pane would extend far below the fold.
        await Expect(_messages.ChatInput).ToBeInViewportAsync(new() { Ratio = 1 });
        await Expect(_messages.MessagesContainer).ToBeInViewportAsync(new() { Ratio = 1 });
    }

    [Test]
    public async Task BackButton_OnAPhoneViewport_ReturnsFromTheOpenChatToTheChatList()
    {
        // The back button is hidden from 769px up; below it the chat list and the open chat are full-width
        // panes that slide over each other.
        await Page.SetViewportSizeAsync(390, 844);
        await LoginAsOwnerAsync();
        await _messages.NavigateAsync();

        // The sidebar's title is its top edge: in view while the list is shown, off-screen while a chat is open.
        var item = _messages.ChatItem(_truncatedText.ChatName);
        await Expect(_messages.SidebarTitle).ToBeInViewportAsync();

        await item.ClickAsync();
        await _messages.WaitForChatViewActiveAsync();
        await Expect(_messages.SelectedChatTitle).ToHaveTextAsync(_truncatedText.ChatName);
        // The list pane has slid out of view behind the open chat.
        await Expect(_messages.SidebarTitle).Not.ToBeInViewportAsync();

        await _messages.ClickBackButtonAsync();

        await Expect(_messages.SidebarTitle).ToBeInViewportAsync();
        await Expect(_messages.ActiveChatView).ToHaveCountAsync(0);
    }
}
