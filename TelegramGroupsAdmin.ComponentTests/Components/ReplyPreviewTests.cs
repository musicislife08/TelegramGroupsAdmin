using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ReplyPreview.razor
/// Tests conditional rendering, text truncation, and fallback displays.
/// </summary>
[TestFixture]
public class ReplyPreviewTests : MudBlazorTestContext
{
    #region Conditional Rendering Tests

    [Test]
    public void RendersNothing_WhenReplyToMessageIdIsNull()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, null)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, "Hello"));

        // Assert - should render nothing
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void RendersNothing_WhenNoUserOrText()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, null)
            .Add(x => x.ReplyToText, null));

        // Assert - should render nothing when both user and text are missing
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void RendersNothing_WhenUserAndTextAreEmpty()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "")
            .Add(x => x.ReplyToText, ""));

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void Renders_WhenMessageIdAndUserProvided()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, null));

        // Assert - should render with user
        Assert.That(cut.Markup, Does.Contain("reply-preview"));
        Assert.That(cut.Markup, Does.Contain("John"));
    }

    [Test]
    public void Renders_WhenMessageIdAndTextProvided()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, null)
            .Add(x => x.ReplyToText, "Hello world"));

        // Assert - should render with text
        Assert.That(cut.Markup, Does.Contain("reply-preview"));
        Assert.That(cut.Markup, Does.Contain("Hello world"));
    }

    #endregion

    #region User Display Tests

    [Test]
    public void DisplaysUserName()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "Alice")
            .Add(x => x.ReplyToText, "Some text"));

        // Assert
        var userDiv = cut.Find(".reply-user");
        Assert.That(userDiv.TextContent, Is.EqualTo("Alice"));
    }

    [Test]
    public void DisplaysUnknownUser_WhenUserIsNull()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, null)
            .Add(x => x.ReplyToText, "Some text"));

        // Assert
        var userDiv = cut.Find(".reply-user");
        Assert.That(userDiv.TextContent, Is.EqualTo("Unknown User"));
    }

    [Test]
    public void DisplaysUnknownUser_WhenUserIsEmpty()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "")
            .Add(x => x.ReplyToText, "Some text"));

        // Assert
        var userDiv = cut.Find(".reply-user");
        Assert.That(userDiv.TextContent, Is.EqualTo("Unknown User"));
    }

    #endregion

    #region Text Display Tests

    [Test]
    public void DisplaysReplyText()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, "This is the reply text"));

        // Assert
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Is.EqualTo("This is the reply text"));
    }

    [Test]
    public void DisplaysDeletedMessage_WhenTextIsNull()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, null));

        // Assert
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Is.EqualTo("[Deleted message]"));
    }

    [Test]
    public void DisplaysDeletedMessage_WhenTextIsEmpty()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, ""));

        // Assert
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Is.EqualTo("[Deleted message]"));
    }

    #endregion

    #region Text Truncation Tests

    [Test]
    public void DoesNotTruncateShortText()
    {
        // Arrange - text under 60 characters
        const string shortText = "Short message";
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, shortText));

        // Assert - full text shown, no ellipsis
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Is.EqualTo(shortText));
        Assert.That(textDiv.TextContent, Does.Not.Contain("..."));
    }

    [Test]
    public void TruncatesLongText()
    {
        // Arrange - text over 60 characters
        const string longText = "This is a very long message that definitely exceeds sixty characters and should be truncated";
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, longText));

        // Assert - should be truncated with ellipsis
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Does.EndWith("..."));
        Assert.That(textDiv.TextContent.Length, Is.LessThanOrEqualTo(63)); // 60 chars + "..."
    }

    [Test]
    public void TruncatesAtExactly60Characters()
    {
        // Arrange - exactly 60 character text
        var exactText = new string('A', 60);
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, exactText));

        // Assert - should NOT be truncated (equal to max length)
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Is.EqualTo(exactText));
        Assert.That(textDiv.TextContent, Does.Not.Contain("..."));
    }

    [Test]
    public void TruncatesAt61Characters()
    {
        // Arrange - 61 character text (one over limit)
        var longText = new string('A', 61);
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, longText));

        // Assert - should be truncated
        var textDiv = cut.Find(".reply-text");
        Assert.That(textDiv.TextContent, Does.EndWith("..."));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnReplyClick_WhenClicked()
    {
        // Arrange
        long? clickedMessageId = null;
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 456L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, "Hello")
            .Add(x => x.OnReplyClick, EventCallback.Factory.Create<long>(this, id => clickedMessageId = id)));

        // Act
        var preview = cut.Find(".reply-preview");
        await preview.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(clickedMessageId, Is.EqualTo(456L));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasCorrectStructure()
    {
        // Arrange & Act
        var cut = Render<ReplyPreview>(p => p
            .Add(x => x.ReplyToMessageId, 123L)
            .Add(x => x.ReplyToUser, "John")
            .Add(x => x.ReplyToText, "Hello"));

        using (Assert.EnterMultipleScope())
        {
            // Assert - verify expected elements exist
            Assert.That(cut.FindAll(".reply-preview").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".reply-bar").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".reply-content").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".reply-user").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".reply-text").Count, Is.EqualTo(1));
        }
    }

    #endregion
}
