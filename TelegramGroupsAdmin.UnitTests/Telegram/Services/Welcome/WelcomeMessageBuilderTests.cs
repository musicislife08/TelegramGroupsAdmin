using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Test suite for WelcomeMessageBuilder static methods.
/// Tests pure message formatting logic with variable substitution.
///
/// All tests are pure function tests - no mocks, no Telegram API.
///
/// Created: 2025-12-01 (REFACTOR-4 Phase 1)
/// </summary>
[TestFixture]
public class WelcomeMessageBuilderTests
{
    #region FormatWelcomeMessage Tests

    [Test]
    public void FormatWelcomeMessage_ChatMode_SubstitutesAllVariables()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 120,
            MainWelcomeMessage = "Welcome {username} to {chat_name}! You have {timeout} seconds."
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@testuser",
            chatName: "Test Chat");

        // Assert
        Assert.That(result, Is.EqualTo("Welcome @testuser to Test Chat! You have 120 seconds."));
    }

    [Test]
    public void FormatWelcomeMessage_DmMode_UsesDmTeaserMessage()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.DmWelcome,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "This should NOT be used in DM mode",
            DmChatTeaserMessage = "Hey {username}, check your DMs for {chat_name} rules! ({timeout}s)"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "John",
            chatName: "Crypto Group");

        // Assert
        Assert.That(result, Is.EqualTo("Hey John, check your DMs for Crypto Group rules! (60s)"));
    }

    [Test]
    public void FormatWelcomeMessage_UsernameWithAtSign_PreservesAtSign()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 30,
            MainWelcomeMessage = "Hello {username}!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@johndoe",
            chatName: "Any Chat");

        // Assert
        Assert.That(result, Is.EqualTo("Hello @johndoe!"));
    }

    [Test]
    public void FormatWelcomeMessage_UsernameWithoutAtSign_UsesFirstName()
    {
        // Arrange: When user has no username, we pass their first name
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 30,
            MainWelcomeMessage = "Hello {username}!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "John",
            chatName: "Any Chat");

        // Assert
        Assert.That(result, Is.EqualTo("Hello John!"));
    }

    [Test]
    public void FormatWelcomeMessage_NoVariablesInTemplate_ReturnsUnchanged()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Welcome to the group! Please read the rules."
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@ignored",
            chatName: "Ignored Chat");

        // Assert
        Assert.That(result, Is.EqualTo("Welcome to the group! Please read the rules."));
    }

    [Test]
    public void FormatWelcomeMessage_MultipleOccurrencesOfVariable_ReplacesAll()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 45,
            MainWelcomeMessage = "{username}, welcome! Remember {username}, you have {timeout}s. Yes, {timeout} seconds!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@alice",
            chatName: "Test");

        // Assert
        Assert.That(result, Is.EqualTo("@alice, welcome! Remember @alice, you have 45s. Yes, 45 seconds!"));
    }

    #endregion

    #region FormatRulesConfirmation Tests

    [Test]
    public void FormatRulesConfirmation_AddsConfirmationFooter()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Welcome {username} to {chat_name}!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatRulesConfirmation(
            config,
            username: "@testuser",
            chatName: "Test Chat");

        // Assert
        Assert.That(result, Does.StartWith("Welcome @testuser to Test Chat!"));
        Assert.That(result, Does.Contain("You're all set"));
    }

    [Test]
    public void FormatRulesConfirmation_SubstitutesAllVariables()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            TimeoutSeconds = 90,
            MainWelcomeMessage = "{username} joined {chat_name} with {timeout}s timeout"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatRulesConfirmation(
            config,
            username: "Bob",
            chatName: "Dev Group");

        // Assert
        Assert.That(result, Does.Contain("Bob joined Dev Group with 90s timeout"));
    }

    #endregion

    #region FormatDmAcceptanceConfirmation Tests

    [Test]
    public void FormatDmAcceptanceConfirmation_IncludesChatName()
    {
        // Act
        var result = WelcomeMessageBuilder.FormatDmAcceptanceConfirmation("Awesome Group");

        // Assert
        Assert.That(result, Does.Contain("Awesome Group"));
        Assert.That(result, Does.Contain("Welcome"));
    }

    [Test]
    public void FormatDmAcceptanceConfirmation_IndicatesSuccess()
    {
        // Act
        var result = WelcomeMessageBuilder.FormatDmAcceptanceConfirmation("Any Chat");

        // Assert: Should have success indicator
        Assert.That(result, Does.Contain("✅").Or.Contain("participate"));
    }

    #endregion

    #region FormatWrongUserWarning Tests

    [Test]
    public void FormatWrongUserWarning_IncludesUsername()
    {
        // Act
        var result = WelcomeMessageBuilder.FormatWrongUserWarning("@intruder");

        // Assert
        Assert.That(result, Does.Contain("@intruder"));
    }

    [Test]
    public void FormatWrongUserWarning_IndicatesWarning()
    {
        // Act
        var result = WelcomeMessageBuilder.FormatWrongUserWarning("John");

        // Assert: Should indicate this is a warning
        Assert.That(result, Does.Contain("⚠️").Or.Contain("not for you"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void FormatWelcomeMessage_EmptyUsername_HandlesGracefully()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Hello {username}!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "",
            chatName: "Test");

        // Assert: Empty string substituted (caller's responsibility to provide valid username)
        Assert.That(result, Is.EqualTo("Hello !"));
    }

    [Test]
    public void FormatWelcomeMessage_SpecialCharactersInChatName_PreservesThem()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Welcome to {chat_name}!"
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@user",
            chatName: "🚀 Crypto & NFT <Group>");

        // Assert
        Assert.That(result, Is.EqualTo("Welcome to 🚀 Crypto & NFT <Group>!"));
    }

    #endregion

    #region Empty Config Property Tests

    [Test]
    public void FormatWelcomeMessage_EmptyMainWelcomeMessage_ReturnsEmptyString()
    {
        // Arrange: Config with empty message template (default value)
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            MainWelcomeMessage = string.Empty
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@user",
            chatName: "Test Chat");

        // Assert: Empty template returns empty result
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void FormatWelcomeMessage_EmptyDmTeaserMessage_ReturnsEmptyString()
    {
        // Arrange: DM mode with empty teaser template
        var config = new WelcomeConfig
        {
            Mode = WelcomeMode.DmWelcome,
            TimeoutSeconds = 60,
            DmChatTeaserMessage = string.Empty
        };

        // Act
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            config,
            username: "@user",
            chatName: "Test Chat");

        // Assert: Empty template returns empty result
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void FormatRulesConfirmation_EmptyMainWelcomeMessage_ReturnsOnlyFooter()
    {
        // Arrange: Config with empty message template
        var config = new WelcomeConfig
        {
            TimeoutSeconds = 60,
            MainWelcomeMessage = string.Empty
        };

        // Act
        var result = WelcomeMessageBuilder.FormatRulesConfirmation(
            config,
            username: "@user",
            chatName: "Test Chat");

        // Assert: Should still have the footer even with empty base message
        Assert.That(result, Does.Contain("You're all set"));
    }

    #endregion
}
