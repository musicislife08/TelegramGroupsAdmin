using Bunit;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramUserMessage.razor
/// Tests the Telegram-style user message bubble with customizable text, time, and checkmarks.
/// </summary>
[TestFixture]
public class TelegramUserMessageTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasMessageWrapper()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Hello"));

        // Assert
        var wrapper = cut.Find(".telegram-message-wrapper");
        Assert.That(wrapper, Is.Not.Null);
        Assert.That(wrapper.ClassList, Does.Contain("telegram-user-message"));
    }

    [Test]
    public void HasMessageBubble()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Hello"));

        // Assert
        var bubble = cut.Find(".telegram-message-bubble");
        Assert.That(bubble, Is.Not.Null);
        Assert.That(bubble.ClassList, Does.Contain("telegram-bubble-user"));
    }

    [Test]
    public void HasTextElement()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test message"));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text, Is.Not.Null);
    }

    [Test]
    public void HasTimeElement()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert
        var time = cut.Find(".telegram-message-time");
        Assert.That(time, Is.Not.Null);
    }

    [Test]
    public void BubbleIsInsideWrapper()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert
        var wrapper = cut.Find(".telegram-message-wrapper");
        Assert.That(wrapper.InnerHtml, Does.Contain("telegram-message-bubble"));
    }

    #endregion

    #region Text Parameter Tests

    [Test]
    public void DisplaysText()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Hello, World!"));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text.TextContent, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void DisplaysEmptyText()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, ""));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text.TextContent, Is.Empty);
    }

    [Test]
    public void DisplaysLongText()
    {
        // Arrange
        var longText = "This is a very long message that might span multiple lines in the message bubble.";

        // Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, longText));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text.TextContent, Is.EqualTo(longText));
    }

    [Test]
    public void DisplaysTextWithEmoji()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Hello! 👋 How are you? 😊"));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text.TextContent, Does.Contain("👋"));
        Assert.That(text.TextContent, Does.Contain("😊"));
    }

    [Test]
    public void EscapesHtmlInText()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "<script>alert('xss')</script>"));

        // Assert - Should be HTML escaped
        Assert.That(cut.Markup, Does.Contain("&lt;script&gt;"));
        Assert.That(cut.Markup, Does.Not.Contain("<script>"));
    }

    #endregion

    #region Time Parameter Tests

    [Test]
    public void DisplaysTime()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.Time, "3:45 PM"));

        // Assert
        var time = cut.Find(".telegram-message-time");
        Assert.That(time.TextContent, Does.Contain("3:45 PM"));
    }

    [Test]
    public void DisplaysDefaultTime()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert - Default is "8:52 PM"
        var time = cut.Find(".telegram-message-time");
        Assert.That(time.TextContent, Does.Contain("8:52 PM"));
    }

    [Test]
    public void DisplaysCustomTimeFormat()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.Time, "14:30"));

        // Assert
        var time = cut.Find(".telegram-message-time");
        Assert.That(time.TextContent, Does.Contain("14:30"));
    }

    #endregion

    #region ShowCheckmarks Parameter Tests

    [Test]
    public void ShowsCheckmarks_WhenTrue()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.ShowCheckmarks, true));

        // Assert
        var checkmarks = cut.Find(".telegram-checkmarks");
        Assert.That(checkmarks, Is.Not.Null);
        Assert.That(checkmarks.TextContent, Does.Contain("✓✓"));
    }

    [Test]
    public void HidesCheckmarks_WhenFalse()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.ShowCheckmarks, false));

        // Assert
        var checkmarks = cut.FindAll(".telegram-checkmarks");
        Assert.That(checkmarks.Count, Is.EqualTo(0));
    }

    [Test]
    public void ShowsCheckmarksByDefault()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert - Default is true
        var checkmarks = cut.Find(".telegram-checkmarks");
        Assert.That(checkmarks, Is.Not.Null);
    }

    [Test]
    public void CheckmarksAreDoubleCheckmarks()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.ShowCheckmarks, true));

        // Assert - Should have double checkmarks (✓✓)
        var checkmarks = cut.Find(".telegram-checkmarks");
        Assert.That(checkmarks.TextContent.Trim(), Is.EqualTo("✓✓"));
    }

    [Test]
    public void CheckmarksAreInsideTimeElement()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.ShowCheckmarks, true));

        // Assert
        var time = cut.Find(".telegram-message-time");
        Assert.That(time.InnerHtml, Does.Contain("telegram-checkmarks"));
    }

    #endregion

    #region Element Hierarchy Tests

    [Test]
    public void TextIsInsideBubble()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test message"));

        // Assert
        var bubble = cut.Find(".telegram-message-bubble");
        Assert.That(bubble.InnerHtml, Does.Contain("telegram-message-text"));
    }

    [Test]
    public void TimeIsInsideBubble()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert
        var bubble = cut.Find(".telegram-message-bubble");
        Assert.That(bubble.InnerHtml, Does.Contain("telegram-message-time"));
    }

    [Test]
    public void ElementsAreInCorrectOrder()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert - text should come before time
        var bubble = cut.Find(".telegram-message-bubble");
        var children = bubble.Children.ToList();

        Assert.That(children.Count, Is.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children[0].ClassList, Does.Contain("telegram-message-text"));
            Assert.That(children[1].ClassList, Does.Contain("telegram-message-time"));
        }
    }

    #endregion

    #region Rendering Tests

    [Test]
    public void WrapperIsDiv()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert
        var wrapper = cut.Find(".telegram-message-wrapper");
        Assert.That(wrapper.TagName.ToLower(), Is.EqualTo("div"));
    }

    [Test]
    public void BubbleIsDiv()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert
        var bubble = cut.Find(".telegram-message-bubble");
        Assert.That(bubble.TagName.ToLower(), Is.EqualTo("div"));
    }

    [Test]
    public void CheckmarksIsSpan()
    {
        // Arrange & Act
        var cut = Render<TelegramUserMessage>(p => p
            .Add(x => x.Text, "Test")
            .Add(x => x.ShowCheckmarks, true));

        // Assert
        var checkmarks = cut.Find(".telegram-checkmarks");
        Assert.That(checkmarks.TagName.ToLower(), Is.EqualTo("span"));
    }

    #endregion
}
