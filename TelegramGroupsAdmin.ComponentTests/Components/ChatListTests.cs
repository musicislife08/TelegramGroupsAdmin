using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ChatList.razor
/// Tests Telegram-style chat list sidebar with selection, counts, and previews.
/// </summary>
[TestFixture]
public class ChatListTests : MudBlazorTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a ManagedChatRecord with realistic data.
    /// </summary>
    private static ManagedChatRecord CreateChat(
        long chatId = -1001329174109,
        string? chatName = "Test Group",
        ManagedChatType chatType = ManagedChatType.Supergroup,
        BotChatStatus botStatus = BotChatStatus.Administrator,
        bool isAdmin = true,
        bool isActive = true,
        bool isDeleted = false,
        string? chatIconPath = null)
    {
        return new ManagedChatRecord(
            Identity: new ChatIdentity(chatId, chatName),
            ChatType: chatType,
            BotStatus: botStatus,
            IsAdmin: isAdmin,
            AddedAt: DateTimeOffset.UtcNow.AddDays(-30),
            IsActive: isActive,
            IsDeleted: isDeleted,
            LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            SettingsJson: null,
            ChatIconPath: chatIconPath);
    }

    #endregion

    #region Empty State Tests

    [Test]
    public void DisplaysEmptyState_WhenNoChats()
    {
        // Arrange & Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, []));

        // Assert
        Assert.That(cut.Markup, Does.Contain("No chats available"));
    }

    [Test]
    public void DisplaysEmptyState_WhenChatsIsNull()
    {
        // Arrange & Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, null!));

        // Assert
        Assert.That(cut.Markup, Does.Contain("No chats available"));
    }

    [Test]
    public void DisplaysEmptyIcon_WhenNoChats()
    {
        // Arrange & Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, []));

        // Assert
        var emptyIcon = cut.Find(".empty-icon");
        Assert.That(emptyIcon, Is.Not.Null);
    }

    #endregion

    #region Chat List Display Tests

    [Test]
    public void DisplaysAllChats()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111, chatName: "Group One"),
            CreateChat(chatId: -100222, chatName: "Group Two"),
            CreateChat(chatId: -100333, chatName: "Group Three")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Group One"));
        Assert.That(cut.Markup, Does.Contain("Group Two"));
        Assert.That(cut.Markup, Does.Contain("Group Three"));
    }

    [Test]
    public void DisplaysChatTitles()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatName: "My Awesome Chat")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        var titleSpan = cut.Find(".chat-title");
        Assert.That(titleSpan.TextContent, Is.EqualTo("My Awesome Chat"));
    }

    [Test]
    public void DisplaysChatListItems()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111),
            CreateChat(chatId: -100222)
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        var items = cut.FindAll(".chat-list-item");
        Assert.That(items.Count, Is.EqualTo(2));
    }

    [Test]
    public void SortsChatsByName()
    {
        // Arrange - provide chats in non-sorted order
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100333, chatName: "Third"),
            CreateChat(chatId: -100111, chatName: "First"),
            CreateChat(chatId: -100222, chatName: "Second")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert - should be sorted alphabetically by chat name
        var items = cut.FindAll(".chat-list-item");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0].TextContent, Does.Contain("First"));
            Assert.That(items[1].TextContent, Does.Contain("Second"));
            Assert.That(items[2].TextContent, Does.Contain("Third"));
        }
    }

    #endregion

    #region Avatar Tests

    [Test]
    public void DisplaysChatAvatar_WhenIconPathProvided()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatIconPath: "/data/icons/chat.jpg", chatName: "Test Chat")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        var img = cut.Find("img.chat-avatar");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(img.GetAttribute("src"), Does.Contain("chat.jpg"));
            Assert.That(img.GetAttribute("alt"), Is.EqualTo("Test Chat"));
        }
    }

    [Test]
    public void DisplaysFallbackAvatar_WhenNoIconPath()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatIconPath: null)
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        var fallback = cut.Find(".chat-avatar-fallback");
        Assert.That(fallback, Is.Not.Null);
    }

    [Test]
    public void SetsLazyLoadingOnAvatars()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatIconPath: "/data/icons/chat.jpg")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        // Assert
        var img = cut.Find("img.chat-avatar");
        Assert.That(img.GetAttribute("loading"), Is.EqualTo("lazy"));
    }

    #endregion

    #region Selection Tests

    [Test]
    public void HighlightsSelectedChat()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111, chatName: "Chat One"),
            CreateChat(chatId: -100222, chatName: "Chat Two")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.SelectedChatId, -100222L));

        // Assert - selected chat should have "active" class
        var items = cut.FindAll(".chat-list-item");
        var selectedItem = items.First(i => i.TextContent.Contains("Chat Two"));
        Assert.That(selectedItem.ClassList, Does.Contain("active"));
    }

    [Test]
    public void DoesNotHighlightUnselectedChats()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111, chatName: "Chat One"),
            CreateChat(chatId: -100222, chatName: "Chat Two")
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.SelectedChatId, -100222L));

        // Assert
        var items = cut.FindAll(".chat-list-item");
        var unselectedItem = items.First(i => i.TextContent.Contains("Chat One"));
        Assert.That(unselectedItem.ClassList, Does.Not.Contain("active"));
    }

    [Test]
    public void NoSelectionHighlight_WhenSelectedChatIdIsNull()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.SelectedChatId, null));

        // Assert
        var activeItems = cut.FindAll(".chat-list-item.active");
        Assert.That(activeItems.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task InvokesOnChatSelected_WhenChatClicked()
    {
        // Arrange
        long? selectedChatId = null;
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111, chatName: "Chat One"),
            CreateChat(chatId: -100222, chatName: "Chat Two")
        ];

        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.OnChatSelected, EventCallback.Factory.Create<long>(
                this, id => selectedChatId = id)));

        // Act - click on "Chat Two"
        var items = cut.FindAll(".chat-list-item");
        var chatTwo = items.First(i => i.TextContent.Contains("Chat Two"));
        await chatTwo.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(selectedChatId, Is.EqualTo(-100222L));
    }

    #endregion

    #region Message Count Tests

    [Test]
    public void DisplaysMessageCount_WhenProvided()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var messageCounts = new Dictionary<long, int> { { -100111, 42 } };

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.MessageCounts, messageCounts));

        // Assert
        var countSpan = cut.Find(".chat-count");
        Assert.That(countSpan.TextContent, Is.EqualTo("42"));
    }

    [Test]
    public void HidesMessageCount_WhenZero()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var messageCounts = new Dictionary<long, int> { { -100111, 0 } };

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.MessageCounts, messageCounts));

        // Assert
        var countSpans = cut.FindAll(".chat-count");
        Assert.That(countSpans.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesMessageCount_WhenNotInDictionary()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var messageCounts = new Dictionary<long, int>(); // Empty

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.MessageCounts, messageCounts));

        // Assert
        var countSpans = cut.FindAll(".chat-count");
        Assert.That(countSpans.Count, Is.EqualTo(0));
    }

    #endregion

    #region Last Message Preview Tests

    [Test]
    public void DisplaysLastMessagePreview_WhenProvided()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var previews = new Dictionary<long, string> { { -100111, "Hello everyone!" } };

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.LastMessagePreviews, previews));

        // Assert
        var lastMessage = cut.Find(".chat-last-message");
        Assert.That(lastMessage.TextContent, Is.EqualTo("Hello everyone!"));
    }

    [Test]
    public void DisplaysNoMessagesYet_WhenNoPreview()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var previews = new Dictionary<long, string>(); // Empty

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.LastMessagePreviews, previews));

        // Assert
        var lastMessage = cut.Find(".chat-last-message");
        Assert.That(lastMessage.TextContent, Is.EqualTo("No messages yet"));
    }

    [Test]
    public void TruncatesLongPreview()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var longMessage = "This is a very long message that should definitely be truncated because it exceeds the maximum length allowed for message previews";
        var previews = new Dictionary<long, string> { { -100111, longMessage } };

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.LastMessagePreviews, previews));

        // Assert - should be truncated to 40 chars + "..."
        var lastMessage = cut.Find(".chat-last-message");
        Assert.That(lastMessage.TextContent, Does.EndWith("..."));
        Assert.That(lastMessage.TextContent.Length, Is.LessThanOrEqualTo(43)); // 40 + "..."
    }

    [Test]
    public void DoesNotTruncateShortPreview()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat(chatId: -100111)
        ];
        var shortMessage = "Short message";
        var previews = new Dictionary<long, string> { { -100111, shortMessage } };

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats)
            .Add(x => x.LastMessagePreviews, previews));

        // Assert
        var lastMessage = cut.Find(".chat-last-message");
        Assert.That(lastMessage.TextContent, Is.EqualTo(shortMessage));
        Assert.That(lastMessage.TextContent, Does.Not.EndWith("..."));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasCorrectStructure()
    {
        // Arrange
        List<ManagedChatRecord> chats =
        [
            CreateChat()
        ];

        // Act
        var cut = Render<ChatList>(p => p
            .Add(x => x.Chats, chats));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(cut.FindAll(".chat-list").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".chat-list-item").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".chat-avatar-wrapper").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".chat-info").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".chat-title-row").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".chat-subtitle-row").Count, Is.EqualTo(1));
        }
    }

    #endregion
}
