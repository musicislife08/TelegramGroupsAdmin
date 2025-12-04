using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramPreview.razor
/// Tests the Telegram-style message preview container component.
/// </summary>
[TestFixture]
public class TelegramPreviewTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasPreviewContainer()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var container = cut.Find(".telegram-preview-container");
        Assert.That(container, Is.Not.Null);
    }

    [Test]
    public void HasChatHeader()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var header = cut.Find(".telegram-chat-header");
        Assert.That(header, Is.Not.Null);
    }

    [Test]
    public void HasBotAvatar()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var avatar = cut.Find(".telegram-bot-avatar");
        Assert.That(avatar, Is.Not.Null);
    }

    [Test]
    public void HasBotName()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var name = cut.Find(".telegram-bot-name");
        Assert.That(name, Is.Not.Null);
    }

    [Test]
    public void HasBotStatus()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var status = cut.Find(".telegram-bot-status");
        Assert.That(status, Is.Not.Null);
        Assert.That(status.TextContent, Is.EqualTo("bot"));
    }

    [Test]
    public void HasMessagesArea()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var messagesArea = cut.Find(".telegram-messages-area");
        Assert.That(messagesArea, Is.Not.Null);
    }

    #endregion

    #region BotName Parameter Tests

    [Test]
    public void DisplaysCustomBotName()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>(p => p
            .Add(x => x.BotName, "MyCustomBot"));

        // Assert
        var name = cut.Find(".telegram-bot-name");
        Assert.That(name.TextContent, Is.EqualTo("MyCustomBot"));
    }

    [Test]
    public void DisplaysDefaultBotName()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var name = cut.Find(".telegram-bot-name");
        Assert.That(name.TextContent, Is.EqualTo("tgadmin_test_bot"));
    }

    #endregion

    #region BotInitial Parameter Tests

    [Test]
    public void DisplaysCustomBotInitial()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>(p => p
            .Add(x => x.BotInitial, "M"));

        // Assert
        var avatar = cut.Find(".telegram-bot-avatar");
        Assert.That(avatar.TextContent, Is.EqualTo("M"));
    }

    [Test]
    public void DisplaysDefaultBotInitial()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var avatar = cut.Find(".telegram-bot-avatar");
        Assert.That(avatar.TextContent, Is.EqualTo("T"));
    }

    #endregion

    #region ChildContent Tests

    [Test]
    public void RendersChildContent()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "test-message");
                builder.AddContent(2, "Hello from bot!");
                builder.CloseElement();
            })));

        // Assert
        var message = cut.Find(".test-message");
        Assert.That(message.TextContent, Is.EqualTo("Hello from bot!"));
    }

    [Test]
    public void ChildContentIsInsideMessagesArea()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "id", "child-element");
                builder.CloseElement();
            })));

        // Assert
        var messagesArea = cut.Find(".telegram-messages-area");
        Assert.That(messagesArea.InnerHtml, Does.Contain("child-element"));
    }

    [Test]
    public void RendersWithoutChildContent()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert - Should render container even without child content
        var messagesArea = cut.Find(".telegram-messages-area");
        Assert.That(messagesArea, Is.Not.Null);
    }

    [Test]
    public void RendersMultipleChildElements()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "msg-1");
                builder.AddContent(2, "First message");
                builder.CloseElement();

                builder.OpenElement(3, "div");
                builder.AddAttribute(4, "class", "msg-2");
                builder.AddContent(5, "Second message");
                builder.CloseElement();
            })));

        // Assert
        Assert.That(cut.Markup, Does.Contain("First message"));
        Assert.That(cut.Markup, Does.Contain("Second message"));
    }

    #endregion

    #region Element Hierarchy Tests

    [Test]
    public void HeaderIsInsideContainer()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var container = cut.Find(".telegram-preview-container");
        Assert.That(container.InnerHtml, Does.Contain("telegram-chat-header"));
    }

    [Test]
    public void AvatarIsInsideHeader()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var header = cut.Find(".telegram-chat-header");
        Assert.That(header.InnerHtml, Does.Contain("telegram-bot-avatar"));
    }

    [Test]
    public void MessagesAreaIsInsideContainer()
    {
        // Arrange & Act
        var cut = Render<TelegramPreview>();

        // Assert
        var container = cut.Find(".telegram-preview-container");
        Assert.That(container.InnerHtml, Does.Contain("telegram-messages-area"));
    }

    #endregion
}
