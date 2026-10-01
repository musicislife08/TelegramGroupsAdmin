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

        var anchors = new List<(Anchor Anchor, string Shape)>();
        foreach (var chat in chats)
        {
            var top = await context.Messages.AsNoTracking()
                .Where(m => m.ChatId == chat.ChatId)
                .OrderByDescending(m => m.Timestamp)
                .Select(m => new { m.Timestamp, m.MessageText, m.MediaType, m.PhotoFileId })
                .Take(2).ToListAsync();
            // Skip chats without messages and chats whose newest message ties with the runner-up.
            if (top.Count == 0 || (top.Count > 1 && top[0].Timestamp == top[1].Timestamp))
            {
                continue;
            }
            // The page-object lookup is by exact title; a title that is a substring elsewhere is fine, but
            // ambiguous duplicates are not.
            if (chatNames.Count(n => n == chat.ChatName) != 1)
            {
                continue;
            }

            var latest = top[0];
            if (latest.MediaType is { } media and not MediaType.None)
            {
                anchors.Add((new Anchor(chat.ChatName!, MediaDisplayName(media)), "media"));
            }
            else if (latest.MediaType is null && !string.IsNullOrWhiteSpace(latest.MessageText)
                     && !CutsASurrogatePair(latest.MessageText))
            {
                var text = latest.MessageText;
                var preview = text.Length > 40 ? text[..40] + "..." : text;
                anchors.Add((new Anchor(chat.ChatName!, Normalize(preview)), text.Length > 40 ? "long" : "short"));
            }
        }

        // Canonical's newest message in every chat with messages is a long text (no short or media one),
        // so the 40-character boundary and the media labels have no canonical anchor: two truncated chats.
        var longAnchors = anchors.Where(a => a.Shape == "long").Select(a => a.Anchor).ToList();
        Assert.That(longAnchors, Has.Count.GreaterThanOrEqualTo(2));
        _truncatedText = longAnchors[0];
        _otherTruncatedText = longAnchors[1];
        Assert.That(_truncatedText.ExpectedPreview, Is.Not.EqualTo(_otherTruncatedText.ExpectedPreview));
        Assert.That(anchors.Any(a => a.Shape is "short" or "media"), Is.False,
            "canonical now carries a short or media newest message: extend the preview assertions to cover it");

        var withMessages = await context.Messages.AsNoTracking().Select(m => m.ChatId).Distinct().ToListAsync();
        _emptyChatName = chats.Where(c => !withMessages.Contains(c.ChatId) && chatNames.Count(n => n == c.ChatName) == 1)
            .Select(c => c.ChatName!).First();
    }

    // Independent mirror of the sidebar's media labels.
    private static string MediaDisplayName(MediaType media) => media switch
    {
        MediaType.Animation => "GIF",
        MediaType.Video => "Video",
        MediaType.Audio => "Audio",
        MediaType.Voice => "Voice message",
        MediaType.Sticker => "Sticker",
        MediaType.VideoNote => "Video message",
        MediaType.Document => "Document",
        _ => "Media"
    };

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
