using Bunit;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramMessagePreview.razor
/// Tests the compact Telegram-style message preview component.
/// </summary>
[TestFixture]
public class TelegramMessagePreviewTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasPreviewContainer()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world"));

        // Assert
        var container = cut.Find(".telegram-compact-preview");
        Assert.That(container, Is.Not.Null);
    }

    [Test]
    public void HasBotMessageWrapper()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world"));

        // Assert
        var wrapper = cut.Find(".telegram-bot-message");
        Assert.That(wrapper, Is.Not.Null);
    }

    [Test]
    public void HasMessageBubble()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world"));

        // Assert
        var bubble = cut.Find(".telegram-bubble-bot");
        Assert.That(bubble, Is.Not.Null);
    }

    [Test]
    public void HasMessageText()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world"));

        // Assert
        var text = cut.Find(".telegram-message-text");
        Assert.That(text, Is.Not.Null);
    }

    [Test]
    public void HasMessageTime()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world"));

        // Assert
        var time = cut.Find(".telegram-message-time");
        Assert.That(time, Is.Not.Null);
    }

    #endregion

    #region PreviewText Parameter Tests

    [Test]
    public void DisplaysPreviewText()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello from the bot!"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Hello from the bot!"));
    }

    [Test]
    public void DisplaysEmptyTextGracefully()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, ""));

        // Assert - Should still render the container
        var container = cut.Find(".telegram-compact-preview");
        Assert.That(container, Is.Not.Null);
    }

    #endregion

    #region ShowUserCommand Parameter Tests

    [Test]
    public void ShowsUserCommand_ByDefault()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("/start"));
        Assert.That(cut.Markup, Does.Contain("telegram-user-message"));
    }

    [Test]
    public void ShowsUserCommand_WhenTrue()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.ShowUserCommand, true));

        // Assert
        Assert.That(cut.Markup, Does.Contain("/start"));
        Assert.That(cut.Markup, Does.Contain("telegram-bubble-user"));
    }

    [Test]
    public void HidesUserCommand_WhenFalse()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.ShowUserCommand, false));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        Assert.That(cut.Markup, Does.Not.Contain("/start"));
        var userMessages = cut.FindAll(".telegram-user-message");
        Assert.That(userMessages.Count, Is.EqualTo(0));
    }

    #endregion

    #region Buttons Parameter Tests

    [Test]
    public void DisplaysInlineButtons()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.Buttons, [["Accept", "Decline"]]));

        // Assert
        Assert.That(cut.Markup, Does.Contain("telegram-inline-keyboard"));
        Assert.That(cut.Markup, Does.Contain("Accept"));
        Assert.That(cut.Markup, Does.Contain("Decline"));
    }

    [Test]
    public void DisplaysButtonRow()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.Buttons, [["Button1"]]));

        // Assert
        var row = cut.Find(".telegram-button-row");
        Assert.That(row, Is.Not.Null);
    }

    [Test]
    public void DisplaysMultipleButtonRows()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.Buttons, [
                ["Row1-Btn1", "Row1-Btn2"],
                ["Row2-Btn1"]
            ]));

        // Assert
        var rows = cut.FindAll(".telegram-button-row");
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(cut.Markup, Does.Contain("Row1-Btn1"));
        Assert.That(cut.Markup, Does.Contain("Row2-Btn1"));
    }

    [Test]
    public void HidesButtons_WhenNull()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.Buttons, null));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        var keyboards = cut.FindAll(".telegram-inline-keyboard");
        Assert.That(keyboards.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesButtons_WhenEmpty()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello")
            .Add(x => x.Buttons, []));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        var keyboards = cut.FindAll(".telegram-inline-keyboard");
        Assert.That(keyboards.Count, Is.EqualTo(0));
    }

    #endregion

    #region ShowWarning and ValidVariables Tests

    [Test]
    public void ShowsWarning_WhenInvalidVariables()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello {name}, welcome to {invalid}")
            .Add(x => x.ShowWarning, true)
            .Add(x => x.ValidVariables, ["name"]));

        // Assert
        Assert.That(cut.Markup, Does.Contain("telegram-preview-warning"));
        Assert.That(cut.Markup, Does.Contain("Invalid or incomplete variables detected"));
    }

    [Test]
    public void HidesWarning_WhenAllVariablesValid()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello {name}, welcome to {group}")
            .Add(x => x.ShowWarning, true)
            .Add(x => x.ValidVariables, ["name", "group"]));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        var warnings = cut.FindAll(".telegram-preview-warning");
        Assert.That(warnings.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesWarning_WhenShowWarningFalse()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello {invalid}")
            .Add(x => x.ShowWarning, false)
            .Add(x => x.ValidVariables, ["name"]));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        var warnings = cut.FindAll(".telegram-preview-warning");
        Assert.That(warnings.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesWarning_WhenNoVariablesInText()
    {
        // Arrange & Act
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(x => x.PreviewText, "Hello world, no variables here")
            .Add(x => x.ShowWarning, true)
            .Add(x => x.ValidVariables, ["name"]));

        // Assert - Use FindAll to check element doesn't exist (CSS contains the class name)
        var warnings = cut.FindAll(".telegram-preview-warning");
        Assert.That(warnings.Count, Is.EqualTo(0));
    }

    #endregion
}
