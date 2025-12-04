using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ChatHeader.razor
/// Tests Telegram-style chat header display with back/menu buttons and avatar.
/// </summary>
[TestFixture]
public class ChatHeaderTests : MudBlazorTestContext
{
    #region Chat Name Tests

    [Test]
    public void DisplaysChatName()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group Chat"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Test Group Chat"));
    }

    [Test]
    public void DisplaysChatNameInTitleElement()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "My Community"));

        // Assert
        var titleDiv = cut.Find(".chat-header-title");
        Assert.That(titleDiv.TextContent, Is.EqualTo("My Community"));
    }

    [Test]
    public void DisplaysEmptyChatName()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, ""));

        // Assert - should still render, just empty
        var titleDiv = cut.Find(".chat-header-title");
        Assert.That(titleDiv.TextContent, Is.Empty);
    }

    #endregion

    #region Chat Description Tests

    [Test]
    public void DisplaysChatDescription_WhenProvided()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatDescription, "Welcome to our group!"));

        // Assert
        var subtitleDiv = cut.Find(".chat-header-subtitle");
        Assert.That(subtitleDiv.TextContent, Is.EqualTo("Welcome to our group!"));
    }

    [Test]
    public void HidesChatDescription_WhenNull()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatDescription, null));

        // Assert
        var subtitles = cut.FindAll(".chat-header-subtitle");
        Assert.That(subtitles.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesChatDescription_WhenEmpty()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatDescription, ""));

        // Assert
        var subtitles = cut.FindAll(".chat-header-subtitle");
        Assert.That(subtitles.Count, Is.EqualTo(0));
    }

    #endregion

    #region Avatar Tests

    [Test]
    public void DisplaysAvatarImage_WhenIconPathProvided()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatIconPath, "/data/icons/group.jpg"));

        // Assert
        var img = cut.Find("img.chat-header-avatar");
        Assert.That(img.GetAttribute("src"), Does.Contain("group.jpg"));
        Assert.That(img.GetAttribute("alt"), Is.EqualTo("Test Group"));
    }

    [Test]
    public void DisplaysFallbackAvatar_WhenNoIconPath()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatIconPath, null));

        // Assert
        var fallback = cut.Find(".chat-header-avatar-fallback");
        Assert.That(fallback, Is.Not.Null);
        // Should contain an SVG icon
        var svg = cut.Find(".chat-header-avatar-fallback svg");
        Assert.That(svg, Is.Not.Null);
    }

    [Test]
    public void DisplaysFallbackAvatar_WhenEmptyIconPath()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatIconPath, ""));

        // Assert
        var fallback = cut.Find(".chat-header-avatar-fallback");
        Assert.That(fallback, Is.Not.Null);
    }

    [Test]
    public void SetsLazyLoadingOnAvatar()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatIconPath, "/data/icons/group.jpg"));

        // Assert
        var img = cut.Find("img.chat-header-avatar");
        Assert.That(img.GetAttribute("loading"), Is.EqualTo("lazy"));
    }

    #endregion

    #region Back Button Tests

    [Test]
    public void ShowsBackButton_WhenEnabled()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowBackButton, true));

        // Assert
        var backButton = cut.Find(".back-button");
        Assert.That(backButton, Is.Not.Null);
    }

    [Test]
    public void HidesBackButton_WhenDisabled()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowBackButton, false));

        // Assert
        var backButtons = cut.FindAll(".back-button");
        Assert.That(backButtons.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesBackButton_ByDefault()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group"));

        // Assert
        var backButtons = cut.FindAll(".back-button");
        Assert.That(backButtons.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task InvokesOnBackClicked_WhenBackButtonClicked()
    {
        // Arrange
        var backClicked = false;
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowBackButton, true)
            .Add(x => x.OnBackClicked, EventCallback.Factory.Create(this, () => backClicked = true)));

        // Act
        var backButton = cut.Find(".back-button");
        await backButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(backClicked, Is.True);
    }

    #endregion

    #region Menu Button Tests

    [Test]
    public void ShowsMenuButton_WhenEnabled()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowMenuButton, true));

        // Assert
        var menuButton = cut.Find(".menu-button");
        Assert.That(menuButton, Is.Not.Null);
    }

    [Test]
    public void HidesMenuButton_WhenDisabled()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowMenuButton, false));

        // Assert
        var menuButtons = cut.FindAll(".menu-button");
        Assert.That(menuButtons.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesMenuButton_ByDefault()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group"));

        // Assert
        var menuButtons = cut.FindAll(".menu-button");
        Assert.That(menuButtons.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task InvokesOnMenuClicked_WhenMenuButtonClicked()
    {
        // Arrange
        var menuClicked = false;
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ShowMenuButton, true)
            .Add(x => x.OnMenuClicked, EventCallback.Factory.Create(this, () => menuClicked = true)));

        // Act
        var menuButton = cut.Find(".menu-button");
        await menuButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(menuClicked, Is.True);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasCorrectStructure()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test Group")
            .Add(x => x.ChatDescription, "Description")
            .Add(x => x.ShowBackButton, true)
            .Add(x => x.ShowMenuButton, true));

        // Assert
        Assert.That(cut.FindAll(".chat-header").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".chat-header-info").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".chat-header-title").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".chat-header-subtitle").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".back-button").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".menu-button").Count, Is.EqualTo(1));
    }

    [Test]
    public void BackButtonHasCorrectType()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test")
            .Add(x => x.ShowBackButton, true));

        // Assert - button should have type="button" to prevent form submission
        var backButton = cut.Find(".back-button");
        Assert.That(backButton.GetAttribute("type"), Is.EqualTo("button"));
    }

    [Test]
    public void MenuButtonHasCorrectType()
    {
        // Arrange & Act
        var cut = Render<ChatHeader>(p => p
            .Add(x => x.ChatName, "Test")
            .Add(x => x.ShowMenuButton, true));

        // Assert
        var menuButton = cut.Find(".menu-button");
        Assert.That(menuButton.GetAttribute("type"), Is.EqualTo("button"));
    }

    #endregion
}
