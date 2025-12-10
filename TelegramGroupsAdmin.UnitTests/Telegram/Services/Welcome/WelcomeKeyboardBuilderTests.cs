using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Test suite for WelcomeKeyboardBuilder static methods.
/// Tests pure keyboard construction logic - callback data format, button text.
///
/// Note: We test the logical output (callback data strings, button labels)
/// rather than the Telegram.Bot.Types.ReplyMarkups objects directly.
/// This keeps tests focused on our logic, not Telegram SDK internals.
///
/// Created: 2025-12-01 (REFACTOR-4 Phase 2)
/// </summary>
[TestFixture]
public class WelcomeKeyboardBuilderTests
{
    #region BuildChatModeKeyboard Tests

    [Test]
    public void BuildChatModeKeyboard_ReturnsAcceptAndDenyButtons()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "✅ Accept",
            DenyButtonText = "❌ Deny"
        };
        long userId = 12345;

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, userId);

        // Assert: Should have exactly 1 row with 2 buttons
        Assert.That(keyboard.InlineKeyboard.Count(), Is.EqualTo(1));
        var buttons = keyboard.InlineKeyboard.First().ToList();
        Assert.That(buttons.Count, Is.EqualTo(2));
    }

    [Test]
    public void BuildChatModeKeyboard_AcceptButton_HasCorrectCallbackData()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "✅ Accept",
            DenyButtonText = "❌ Deny"
        };
        long userId = 98765;

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, userId);
        var acceptButton = keyboard.InlineKeyboard.First().First();

        // Assert
        Assert.That(acceptButton.CallbackData, Is.EqualTo("welcome_accept:98765"));
        Assert.That(acceptButton.Text, Is.EqualTo("✅ Accept"));
    }

    [Test]
    public void BuildChatModeKeyboard_DenyButton_HasCorrectCallbackData()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "Accept",
            DenyButtonText = "Decline"
        };
        long userId = 11111;

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, userId);
        var denyButton = keyboard.InlineKeyboard.First().Last();

        // Assert
        Assert.That(denyButton.CallbackData, Is.EqualTo("welcome_deny:11111"));
        Assert.That(denyButton.Text, Is.EqualTo("Decline"));
    }

    [Test]
    public void BuildChatModeKeyboard_UsesConfigButtonText()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "I Agree to Rules",
            DenyButtonText = "No Thanks"
        };

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, userId: 1);
        var buttons = keyboard.InlineKeyboard.First().ToList();

        // Assert
        Assert.That(buttons[0].Text, Is.EqualTo("I Agree to Rules"));
        Assert.That(buttons[1].Text, Is.EqualTo("No Thanks"));
    }

    #endregion

    #region BuildDmModeKeyboard Tests

    [Test]
    public void BuildDmModeKeyboard_ReturnsSingleDeepLinkButton()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            DmButtonText = "📋 Read Guidelines"
        };
        long chatId = -1001234567890;
        long userId = 55555;
        string botUsername = "TestBot";

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, chatId, userId, botUsername);

        // Assert: Single row with single button
        Assert.That(keyboard.InlineKeyboard.Count(), Is.EqualTo(1));
        Assert.That(keyboard.InlineKeyboard.First().Count(), Is.EqualTo(1));
    }

    [Test]
    public void BuildDmModeKeyboard_ButtonHasCorrectDeepLink()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            DmButtonText = "Open DM"
        };
        long chatId = -1001234567890;
        long userId = 12345;
        string botUsername = "MyAwesomeBot";

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, chatId, userId, botUsername);
        var button = keyboard.InlineKeyboard.First().First();

        // Assert
        Assert.That(button.Url, Is.EqualTo("https://t.me/MyAwesomeBot?start=welcome_-1001234567890_12345"));
        Assert.That(button.Text, Is.EqualTo("Open DM"));
    }

    [Test]
    public void BuildDmModeKeyboard_UsesConfigButtonText()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            DmButtonText = "🔐 Verify in Private"
        };

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, chatId: -100, userId: 1, botUsername: "Bot");
        var button = keyboard.InlineKeyboard.First().First();

        // Assert
        Assert.That(button.Text, Is.EqualTo("🔐 Verify in Private"));
    }

    #endregion

    #region BuildReturnToChatKeyboard Tests

    [Test]
    public void BuildReturnToChatKeyboard_ReturnsSingleButton()
    {
        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard("Test Chat", "https://t.me/testchat");

        // Assert
        Assert.That(keyboard.InlineKeyboard.Count(), Is.EqualTo(1));
        Assert.That(keyboard.InlineKeyboard.First().Count(), Is.EqualTo(1));
    }

    [Test]
    public void BuildReturnToChatKeyboard_ButtonHasCorrectUrl()
    {
        // Arrange
        string chatName = "Crypto Traders";
        string chatLink = "https://t.me/cryptotraders";

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard(chatName, chatLink);
        var button = keyboard.InlineKeyboard.First().First();

        // Assert
        Assert.That(button.Url, Is.EqualTo("https://t.me/cryptotraders"));
        Assert.That(button.Text, Does.Contain("Crypto Traders"));
    }

    [Test]
    public void BuildReturnToChatKeyboard_ButtonTextIncludesChatName()
    {
        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard("My Awesome Group", "https://t.me/awesome");
        var button = keyboard.InlineKeyboard.First().First();

        // Assert
        Assert.That(button.Text, Does.Contain("My Awesome Group"));
    }

    #endregion

    #region BuildDmAcceptKeyboard Tests

    [Test]
    public void BuildDmAcceptKeyboard_ReturnsSingleButton()
    {
        // Arrange
        var config = new WelcomeConfig { AcceptButtonText = "✅ Accept" };

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildDmAcceptKeyboard(config, groupChatId: -100, userId: 123);

        // Assert
        Assert.That(keyboard.InlineKeyboard.Count(), Is.EqualTo(1));
        Assert.That(keyboard.InlineKeyboard.First().Count(), Is.EqualTo(1));
    }

    [Test]
    public void BuildDmAcceptKeyboard_HasCorrectCallbackData()
    {
        // Arrange
        var config = new WelcomeConfig { AcceptButtonText = "Accept Rules" };
        long groupChatId = -1001234567890;
        long userId = 99999;

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildDmAcceptKeyboard(config, groupChatId, userId);
        var button = keyboard.InlineKeyboard.First().First();

        // Assert: dm_accept:groupChatId:userId format
        Assert.That(button.CallbackData, Is.EqualTo("dm_accept:-1001234567890:99999"));
        Assert.That(button.Text, Is.EqualTo("Accept Rules"));
    }

    #endregion
}
