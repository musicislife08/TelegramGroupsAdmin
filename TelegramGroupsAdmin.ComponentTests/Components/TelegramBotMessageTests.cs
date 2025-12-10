using Bunit;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramBotMessage.razor
/// Tests text and time display in Telegram-style bot message bubble.
/// </summary>
[TestFixture]
public class TelegramBotMessageTests : MudBlazorTestContext
{
    #region Text Display Tests

    [Test]
    public void DisplaysText()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Hello from bot!"));

        // Assert
        var textDiv = cut.Find(".telegram-message-text");
        Assert.That(textDiv.TextContent, Is.EqualTo("Hello from bot!"));
    }

    [Test]
    public void DisplaysMultilineText()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Line 1\nLine 2\nLine 3"));

        // Assert - component uses white-space: pre-wrap so newlines are preserved
        var textDiv = cut.Find(".telegram-message-text");
        Assert.That(textDiv.TextContent, Does.Contain("Line 1"));
        Assert.That(textDiv.TextContent, Does.Contain("Line 2"));
        Assert.That(textDiv.TextContent, Does.Contain("Line 3"));
    }

    [Test]
    public void DisplaysEmptyText()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, ""));

        // Assert - should still render, just empty
        var textDiv = cut.Find(".telegram-message-text");
        Assert.That(textDiv.TextContent, Is.Empty);
    }

    [Test]
    public void PreservesSpecialCharacters()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Hello <world> & 'friends'"));

        // Assert - special characters should be escaped in HTML
        var textDiv = cut.Find(".telegram-message-text");
        Assert.That(textDiv.TextContent, Is.EqualTo("Hello <world> & 'friends'"));
    }

    #endregion

    #region Time Display Tests

    [Test]
    public void DisplaysDefaultTime()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Message"));

        // Assert - default time is "8:52 PM"
        var timeDiv = cut.Find(".telegram-message-time");
        Assert.That(timeDiv.TextContent, Is.EqualTo("8:52 PM"));
    }

    [Test]
    public void DisplaysCustomTime()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Message")
            .Add(x => x.Time, "3:45 PM"));

        // Assert
        var timeDiv = cut.Find(".telegram-message-time");
        Assert.That(timeDiv.TextContent, Is.EqualTo("3:45 PM"));
    }

    [Test]
    public void Displays24HourTime()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Message")
            .Add(x => x.Time, "15:45"));

        // Assert
        var timeDiv = cut.Find(".telegram-message-time");
        Assert.That(timeDiv.TextContent, Is.EqualTo("15:45"));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasCorrectStructure()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Test message"));

        // Assert - verify expected CSS classes exist
        Assert.That(cut.FindAll(".telegram-message-wrapper").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".telegram-bot-message").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".telegram-message-bubble").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".telegram-bubble-bot").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".telegram-message-text").Count, Is.EqualTo(1));
        Assert.That(cut.FindAll(".telegram-message-time").Count, Is.EqualTo(1));
    }

    [Test]
    public void HasBotMessageClass()
    {
        // Arrange & Act
        var cut = Render<TelegramBotMessage>(p => p
            .Add(x => x.Text, "Test"));

        // Assert - should have bot-specific styling class
        var wrapper = cut.Find(".telegram-message-wrapper");
        Assert.That(wrapper.ClassList, Does.Contain("telegram-bot-message"));
    }

    #endregion
}
