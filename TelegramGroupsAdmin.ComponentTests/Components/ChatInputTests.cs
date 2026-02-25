using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ChatInput.razor
/// Tests the Telegram-style chat input component.
/// </summary>
[TestFixture]
public class ChatInputTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasInputContainer()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var container = cut.Find(".chat-input-container");
        Assert.That(container, Is.Not.Null);
    }

    [Test]
    public void HasInputWrapper()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var wrapper = cut.Find(".chat-input-wrapper");
        Assert.That(wrapper, Is.Not.Null);
    }

    [Test]
    public void HasModeToggle()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var toggle = cut.Find(".mode-toggle");
        Assert.That(toggle, Is.Not.Null);
    }

    [Test]
    public void HasMessageInput()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input, Is.Not.Null);
    }

    [Test]
    public void HasSendButton()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var sendButton = cut.Find(".send-button");
        Assert.That(sendButton, Is.Not.Null);
    }

    [Test]
    public void HasAttachButton()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var attachButton = cut.Find(".attach-button");
        Assert.That(attachButton, Is.Not.Null);
    }

    [Test]
    public void HasEmojiButton()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var emojiButton = cut.Find(".emoji-button");
        Assert.That(emojiButton, Is.Not.Null);
    }

    #endregion

    #region IsEnabled Parameter Tests

    [Test]
    public void InputIsDisabled_ByDefault()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void InputIsEnabled_WhenIsEnabledTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEnabled, true));

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void InputIsDisabled_WhenIsEnabledFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEnabled, false));

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.HasAttribute("disabled"), Is.True);
    }

    #endregion

    #region PlaceholderText Parameter Tests

    [Test]
    public void DisplaysDefaultPlaceholder()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.GetAttribute("placeholder"), Is.EqualTo("Type a message..."));
    }

    [Test]
    public void DisplaysCustomPlaceholder()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.PlaceholderText, "Write something..."));

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.GetAttribute("placeholder"), Is.EqualTo("Write something..."));
    }

    #endregion

    #region MaxMessageLength Parameter Tests

    [Test]
    public void HasDefaultMaxLength()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.GetAttribute("maxlength"), Is.EqualTo("4096"));
    }

    [Test]
    public void HasCustomMaxLength()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.MaxMessageLength, 1000));

        // Assert
        var input = cut.Find(".message-input");
        Assert.That(input.GetAttribute("maxlength"), Is.EqualTo("1000"));
    }

    #endregion

    #region IsEditMode Parameter Tests

    [Test]
    public void HidesEditIndicator_WhenNotEditMode()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEditMode, false));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("edit-indicator"));
    }

    [Test]
    public void ShowsEditIndicator_WhenEditMode()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEditMode, true));

        // Assert
        var editIndicator = cut.Find(".edit-indicator");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editIndicator, Is.Not.Null);
            Assert.That(cut.Markup, Does.Contain("Edit message"));
        }
    }

    [Test]
    public void ShowsCheckIcon_InEditMode()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEditMode, true)
            .Add(x => x.IsEnabled, true));

        // Assert - Check icon path for checkmark
        Assert.That(cut.Markup, Does.Contain("M21,7L9,19L3.5,13.5"));
    }

    [Test]
    public void ShowsSendIcon_InNormalMode()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.IsEditMode, false)
            .Add(x => x.IsEnabled, true));

        // Assert - Send icon path
        Assert.That(cut.Markup, Does.Contain("M2,21L23,12L2,3"));
    }

    #endregion

    #region Reply Mode Tests

    [Test]
    public void HidesReplySection_WhenNoReply()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.ReplyToMessageId, null));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("reply-section"));
    }

    [Test]
    public void ShowsReplySection_WhenReplying()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.ReplyToMessageId, 12345)
            .Add(x => x.ReplyToUser, "TestUser")
            .Add(x => x.ReplyToText, "Original message"));

        // Assert
        var replySection = cut.Find(".reply-section");
        Assert.That(replySection, Is.Not.Null);
    }

    [Test]
    public void ShowsCancelButton_WhenReplying()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.ReplyToMessageId, 12345)
            .Add(x => x.ReplyToUser, "TestUser")
            .Add(x => x.ReplyToText, "Original message"));

        // Assert
        var cancelButton = cut.Find(".reply-section .cancel-button");
        Assert.That(cancelButton, Is.Not.Null);
    }

    #endregion

    #region Mode Toggle Tests

    [Test]
    public void ModeToggleIsDisabled()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert - Phase 1: Mode toggle is disabled
        var modeButton = cut.Find(".mode-button");
        Assert.That(modeButton.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void DisplaysBotLabel()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert
        var modeLabel = cut.Find(".mode-label");
        Assert.That(modeLabel.TextContent, Is.EqualTo("Bot"));
    }

    #endregion

    #region Disabled Buttons Tests (Phase 1)

    [Test]
    public void AttachButtonIsDisabled()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert - Phase 1: Attach button is disabled
        var attachButton = cut.Find(".attach-button");
        Assert.That(attachButton.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void EmojiButtonIsDisabled()
    {
        // Arrange & Act
        var cut = Render<ChatInput>();

        // Assert - Phase 1: Emoji button is disabled
        var emojiButton = cut.Find(".emoji-button");
        Assert.That(emojiButton.HasAttribute("disabled"), Is.True);
    }

    #endregion

    #region Toggle Display Tests

    [Test]
    public void ModeButton_ShowsMeLabel_WhenSendAsUserIsTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, true)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var label = cut.Find("span.mode-label");
        Assert.That(label.TextContent, Is.EqualTo("Me"));
    }

    [Test]
    public void ModeButton_HasUserModeClass_WhenSendAsUserIsTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, true)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var button = cut.Find("button.mode-button");
        Assert.That(button.ClassList, Does.Contain("user-mode"));
    }

    [Test]
    public void ModeButton_ShowsBotLabel_WhenSendAsUserIsFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, false)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var label = cut.Find("span.mode-label");
        Assert.That(label.TextContent, Is.EqualTo("Bot"));
    }

    [Test]
    public void ModeButton_HasBotModeClass_WhenSendAsUserIsFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, false)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var button = cut.Find("button.mode-button");
        Assert.That(button.ClassList, Does.Contain("bot-mode"));
    }

    [Test]
    public void ModeButton_IsDisabled_WhenUserApiAvailableForChatIsFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.UserApiAvailableForChat, false)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var button = cut.Find("button.mode-button");
        Assert.That(button.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void ModeButton_IsEnabled_WhenUserApiAvailableForChatIsTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var button = cut.Find("button.mode-button");
        Assert.That(button.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void ModeToggle_ShowsSkeleton_WhenCheckingUserApiIsTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.CheckingUserApi, true));

        // Assert
        var skeletons = cut.FindAll(".mode-skeleton");
        Assert.That(skeletons.Count, Is.EqualTo(1));
    }

    [Test]
    public void ModeToggle_HidesModeButton_WhenCheckingUserApiIsTrue()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.CheckingUserApi, true));

        // Assert
        var buttons = cut.FindAll("button.mode-button");
        Assert.That(buttons.Count, Is.EqualTo(0));
    }

    [Test]
    public void ModeToggle_ShowsModeButton_WhenCheckingUserApiIsFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var buttons = cut.FindAll("button.mode-button");
        Assert.That(buttons.Count, Is.EqualTo(1));
    }

    [Test]
    public void ModeToggle_HidesSkeleton_WhenCheckingUserApiIsFalse()
    {
        // Arrange & Act
        var cut = Render<ChatInput>(p => p
            .Add(x => x.CheckingUserApi, false));

        // Assert
        var skeletons = cut.FindAll(".mode-skeleton");
        Assert.That(skeletons.Count, Is.EqualTo(0));
    }

    #endregion

    #region Toggle Interaction Tests

    [Test]
    public async Task ModeButton_Click_InvokesOnSendAsUserChanged_WithFalse_WhenSendAsUserIsTrue()
    {
        // Arrange
        bool? receivedValue = null;
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, true)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false)
            .Add(x => x.OnSendAsUserChanged, EventCallback.Factory.Create<bool>(
                this, value => receivedValue = value)));

        // Act
        var button = cut.Find("button.mode-button");
        await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedValue, Is.EqualTo(false));
    }

    [Test]
    public async Task ModeButton_Click_InvokesOnSendAsUserChanged_WithTrue_WhenSendAsUserIsFalse()
    {
        // Arrange
        bool? receivedValue = null;
        var cut = Render<ChatInput>(p => p
            .Add(x => x.SendAsUser, false)
            .Add(x => x.UserApiAvailableForChat, true)
            .Add(x => x.CheckingUserApi, false)
            .Add(x => x.OnSendAsUserChanged, EventCallback.Factory.Create<bool>(
                this, value => receivedValue = value)));

        // Act
        var button = cut.Find("button.mode-button");
        await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedValue, Is.EqualTo(true));
    }

    #endregion
}
