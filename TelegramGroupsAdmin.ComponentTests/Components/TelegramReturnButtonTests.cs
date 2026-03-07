using Bunit;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramReturnButton.razor
/// Tests the "Return to [Group]" button styled like Telegram.
/// </summary>
[TestFixture]
public class TelegramReturnButtonTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasButtonWrapper()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert
        var button = cut.Find(".telegram-return-button");
        Assert.That(button, Is.Not.Null);
    }

    [Test]
    public void HasIconElement()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert
        var icon = cut.Find(".telegram-return-icon");
        Assert.That(icon, Is.Not.Null);
        Assert.That(icon.TextContent, Does.Contain("💬"));
    }

    [Test]
    public void HasTextElement()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert
        var text = cut.Find(".telegram-return-text");
        Assert.That(text, Is.Not.Null);
    }

    [Test]
    public void HasArrowElement()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert
        var arrow = cut.Find(".telegram-return-arrow");
        Assert.That(arrow, Is.Not.Null);
        Assert.That(arrow.TextContent, Does.Contain("↗"));
    }

    #endregion

    #region GroupName Parameter Tests

    [Test]
    public void DisplaysGroupName()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>(p => p
            .Add(x => x.GroupName, "My Test Group"));

        // Assert
        var text = cut.Find(".telegram-return-text");
        Assert.That(text.TextContent, Does.Contain("Return to My Test Group"));
    }

    [Test]
    public void DisplaysDefaultGroupName()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert - Default is "TSP Spam Bot Test Group"
        var text = cut.Find(".telegram-return-text");
        Assert.That(text.TextContent, Does.Contain("Return to TSP Spam Bot Test Group"));
    }

    [Test]
    public void DisplaysEmptyGroupName()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>(p => p
            .Add(x => x.GroupName, ""));

        // Assert - Should show "Return to " with empty name
        var text = cut.Find(".telegram-return-text");
        Assert.That(text.TextContent, Is.EqualTo("Return to "));
    }

    [Test]
    public void DisplaysLongGroupName()
    {
        // Arrange
        var longName = "This Is A Very Long Group Name That Might Overflow";

        // Act
        var cut = Render<TelegramReturnButton>(p => p
            .Add(x => x.GroupName, longName));

        // Assert
        var text = cut.Find(".telegram-return-text");
        Assert.That(text.TextContent, Does.Contain(longName));
    }

    [Test]
    public void DisplaysGroupNameWithSpecialCharacters()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>(p => p
            .Add(x => x.GroupName, "Group <&> \"Test\""));

        // Assert - Angle brackets and ampersand should be HTML escaped
        // Note: Quotes are NOT escaped in Blazor text content
        Assert.That(cut.Markup, Does.Contain("&lt;&amp;&gt;"));
        Assert.That(cut.Markup, Does.Contain("\"Test\""));
    }

    [Test]
    public void DisplaysGroupNameWithEmoji()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>(p => p
            .Add(x => x.GroupName, "🚀 Crypto Group 💎"));

        // Assert
        var text = cut.Find(".telegram-return-text");
        Assert.That(text.TextContent, Does.Contain("🚀"));
        Assert.That(text.TextContent, Does.Contain("💎"));
    }

    #endregion

    #region Element Order Tests

    [Test]
    public void ElementsAreInCorrectOrder()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert - Order should be: icon, text, arrow
        var button = cut.Find(".telegram-return-button");
        var children = button.Children.ToList();

        Assert.That(children.Count, Is.EqualTo(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children[0].ClassList, Does.Contain("telegram-return-icon"));
            Assert.That(children[1].ClassList, Does.Contain("telegram-return-text"));
            Assert.That(children[2].ClassList, Does.Contain("telegram-return-arrow"));
        }
    }

    #endregion

    #region Rendering Tests

    [Test]
    public void RendersAsDiv()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert - Button is actually a styled div
        var button = cut.Find(".telegram-return-button");
        Assert.That(button.TagName.ToLower(), Is.EqualTo("div"));
    }

    [Test]
    public void AllChildrenAreDivs()
    {
        // Arrange & Act
        var cut = Render<TelegramReturnButton>();

        // Assert
        var icon = cut.Find(".telegram-return-icon");
        var text = cut.Find(".telegram-return-text");
        var arrow = cut.Find(".telegram-return-arrow");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(icon.TagName.ToLower(), Is.EqualTo("div"));
            Assert.That(text.TagName.ToLower(), Is.EqualTo("div"));
            Assert.That(arrow.TagName.ToLower(), Is.EqualTo("div"));
        }
    }

    #endregion
}
