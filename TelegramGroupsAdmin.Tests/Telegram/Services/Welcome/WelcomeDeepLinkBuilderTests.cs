using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.Tests.Telegram.Services.Welcome;

/// <summary>
/// Test suite for WelcomeDeepLinkBuilder static methods.
/// Tests pure URL construction logic for Telegram deep links.
///
/// All tests are pure function tests - no mocks, no Telegram API.
///
/// Created: 2025-12-01 (REFACTOR-4 Phase 4)
/// </summary>
[TestFixture]
public class WelcomeDeepLinkBuilderTests
{
    #region BuildStartDeepLink Tests

    [Test]
    public void BuildStartDeepLink_FormatsCorrectly()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.BuildStartDeepLink(
            botUsername: "TestBot",
            chatId: -1001234567890,
            userId: 12345);

        // Assert
        Assert.That(result, Is.EqualTo("https://t.me/TestBot?start=welcome_-1001234567890_12345"));
    }

    [Test]
    public void BuildStartDeepLink_HandlesLargeIds()
    {
        // Arrange: Large Telegram IDs
        long chatId = -1009876543210123;
        long userId = 9876543210;

        // Act
        var result = WelcomeDeepLinkBuilder.BuildStartDeepLink("Bot", chatId, userId);

        // Assert
        Assert.That(result, Does.Contain("-1009876543210123"));
        Assert.That(result, Does.Contain("9876543210"));
    }

    [Test]
    public void BuildStartDeepLink_BotUsernameWithoutAt()
    {
        // Arrange: Username should not have @ prefix
        var result = WelcomeDeepLinkBuilder.BuildStartDeepLink("MyBot", -100, 123);

        // Assert: URL should NOT have @ in it
        Assert.That(result, Does.Not.Contain("@"));
        Assert.That(result, Does.StartWith("https://t.me/MyBot"));
    }

    #endregion

    #region BuildPublicChatLink Tests

    [Test]
    public void BuildPublicChatLink_WithUsername_ReturnsLink()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.BuildPublicChatLink("cryptotraders");

        // Assert
        Assert.That(result, Is.EqualTo("https://t.me/cryptotraders"));
    }

    [Test]
    public void BuildPublicChatLink_NullUsername_ReturnsNull()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.BuildPublicChatLink(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildPublicChatLink_EmptyUsername_ReturnsNull()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.BuildPublicChatLink("");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildPublicChatLink_WhitespaceUsername_ReturnsNull()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.BuildPublicChatLink("   ");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildPublicChatLink_UsernameWithoutAt()
    {
        // Arrange: Telegram Chat.Username property doesn't include @
        var result = WelcomeDeepLinkBuilder.BuildPublicChatLink("TestGroup");

        // Assert
        Assert.That(result, Is.EqualTo("https://t.me/TestGroup"));
    }

    #endregion

    #region ParseStartPayload Tests

    [Test]
    public void ParseStartPayload_ValidFormat_ParsesCorrectly()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("welcome_-1001234567890_12345");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChatId, Is.EqualTo(-1001234567890));
        Assert.That(result.UserId, Is.EqualTo(12345));
    }

    [Test]
    public void ParseStartPayload_InvalidPrefix_ReturnsNull()
    {
        // Arrange: Not a welcome deep link
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("other_-100_123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseStartPayload_MissingParts_ReturnsNull()
    {
        // Arrange: Only 2 parts instead of 3
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("welcome_-100");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseStartPayload_InvalidChatId_ReturnsNull()
    {
        // Arrange: Non-numeric chat ID
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("welcome_badchat_123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseStartPayload_InvalidUserId_ReturnsNull()
    {
        // Arrange: Non-numeric user ID
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("welcome_-100_baduser");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseStartPayload_NullPayload_ReturnsNull()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.ParseStartPayload(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseStartPayload_EmptyPayload_ReturnsNull()
    {
        // Act
        var result = WelcomeDeepLinkBuilder.ParseStartPayload("");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion
}
