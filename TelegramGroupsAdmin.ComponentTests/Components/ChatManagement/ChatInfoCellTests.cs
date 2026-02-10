using TelegramGroupsAdmin.Components.Shared.ChatManagement;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components.ChatManagement;

[TestFixture]
public class ChatInfoCellTests : MudBlazorTestContext
{
    private static ManagedChatRecord CreateChat(
        long chatId = -1001234567890,
        string chatName = "Test Chat",
        bool isActive = true,
        bool isDeleted = false,
        ManagedChatType chatType = ManagedChatType.Supergroup)
    {
        return new ManagedChatRecord(
            Chat: new ChatIdentity(chatId, chatName),
            ChatType: chatType,
            BotStatus: BotChatStatus.Administrator,
            IsAdmin: true,
            AddedAt: DateTimeOffset.UtcNow,
            IsActive: isActive,
            IsDeleted: isDeleted,
            LastSeenAt: null,
            SettingsJson: null,
            ChatIconPath: null
        );
    }

    [Test]
    public void RendersChatName()
    {
        var chat = CreateChat(chatName: "My Awesome Group");

        var cut = Render<ChatInfoCell>(p => p.Add(x => x.Chat, chat));

        Assert.That(cut.Markup, Does.Contain("My Awesome Group"));
    }

    [Test]
    public void RendersChatId()
    {
        var chat = CreateChat(chatId: -1009876543210);

        var cut = Render<ChatInfoCell>(p => p.Add(x => x.Chat, chat));

        Assert.That(cut.Markup, Does.Contain("ID: -1009876543210"));
    }

    [Test]
    public void ActiveChat_DoesNotShowInactiveBadge()
    {
        var chat = CreateChat(isActive: true);

        var cut = Render<ChatInfoCell>(p => p.Add(x => x.Chat, chat));

        Assert.That(cut.Markup, Does.Not.Contain("Inactive"));
    }

    [Test]
    public void InactiveChat_ShowsInactiveBadge()
    {
        var chat = CreateChat(isActive: false);

        var cut = Render<ChatInfoCell>(p => p.Add(x => x.Chat, chat));

        Assert.That(cut.Markup, Does.Contain("Inactive"));
        Assert.That(cut.Markup, Does.Contain("mud-chip")); // Rendered as chip
    }

    [Test]
    public void ChatIdRenderedWithSecondaryColor()
    {
        var chat = CreateChat();

        var cut = Render<ChatInfoCell>(p => p.Add(x => x.Chat, chat));

        // The ID should be in a secondary color MudText (caption style)
        Assert.That(cut.Markup, Does.Contain("mud-typography-caption"));
    }
}
