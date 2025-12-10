using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Test suite for WelcomeCallbackParser static methods.
/// Tests pure callback data parsing and validation logic.
///
/// All tests are pure function tests - no mocks, no Telegram API.
///
/// Created: 2025-12-01 (REFACTOR-4 Phase 3)
/// </summary>
[TestFixture]
public class WelcomeCallbackParserTests
{
    #region ParseCallbackData - Accept Format Tests

    [Test]
    public void ParseCallbackData_WelcomeAccept_ParsesCorrectly()
    {
        // Act
        var result = WelcomeCallbackParser.ParseCallbackData("welcome_accept:12345");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo(WelcomeCallbackType.Accept));
        Assert.That(result.UserId, Is.EqualTo(12345));
        Assert.That(result.ChatId, Is.Null);
    }

    [Test]
    public void ParseCallbackData_WelcomeAccept_LargeUserId()
    {
        // Arrange: Large Telegram user ID
        var data = "welcome_accept:9876543210";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(9876543210L));
    }

    #endregion

    #region ParseCallbackData - Deny Format Tests

    [Test]
    public void ParseCallbackData_WelcomeDeny_ParsesCorrectly()
    {
        // Act
        var result = WelcomeCallbackParser.ParseCallbackData("welcome_deny:99999");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo(WelcomeCallbackType.Deny));
        Assert.That(result.UserId, Is.EqualTo(99999));
        Assert.That(result.ChatId, Is.Null);
    }

    #endregion

    #region ParseCallbackData - DmAccept Format Tests

    [Test]
    public void ParseCallbackData_DmAccept_ParsesCorrectly()
    {
        // Act: dm_accept:chatId:userId format
        var result = WelcomeCallbackParser.ParseCallbackData("dm_accept:-1001234567890:55555");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo(WelcomeCallbackType.DmAccept));
        Assert.That(result.ChatId, Is.EqualTo(-1001234567890));
        Assert.That(result.UserId, Is.EqualTo(55555));
    }

    [Test]
    public void ParseCallbackData_DmAccept_NegativeChatId()
    {
        // Arrange: Supergroup chat IDs are negative
        var data = "dm_accept:-1009999999999:123";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChatId, Is.EqualTo(-1009999999999));
    }

    #endregion

    #region ParseCallbackData - Invalid Format Tests

    [Test]
    public void ParseCallbackData_NullData_ReturnsNull()
    {
        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_EmptyString_ReturnsNull()
    {
        // Act
        var result = WelcomeCallbackParser.ParseCallbackData("");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_UnknownAction_ReturnsNull()
    {
        // Arrange: Not a welcome callback
        var data = "spam_report:12345";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_MissingUserId_ReturnsNull()
    {
        // Arrange: Missing user ID
        var data = "welcome_accept:";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_InvalidUserId_ReturnsNull()
    {
        // Arrange: Non-numeric user ID
        var data = "welcome_accept:abc";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_DmAccept_MissingUserId_ReturnsNull()
    {
        // Arrange: dm_accept missing user ID (only 2 parts)
        var data = "dm_accept:-1001234567890";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_DmAccept_InvalidChatId_ReturnsNull()
    {
        // Arrange: Non-numeric chat ID
        var data = "dm_accept:badchat:12345";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseCallbackData_NoColon_ReturnsNull()
    {
        // Arrange: No separator
        var data = "welcome_accept12345";

        // Act
        var result = WelcomeCallbackParser.ParseCallbackData(data);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ValidateCallerIsTarget Tests

    [Test]
    public void ValidateCallerIsTarget_SameUser_ReturnsTrue()
    {
        // Act
        var result = WelcomeCallbackParser.ValidateCallerIsTarget(
            callerId: 12345,
            targetUserId: 12345);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ValidateCallerIsTarget_DifferentUser_ReturnsFalse()
    {
        // Act
        var result = WelcomeCallbackParser.ValidateCallerIsTarget(
            callerId: 12345,
            targetUserId: 67890);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateCallerIsTarget_LargeUserIds_ComparesCorrectly()
    {
        // Arrange: Large Telegram user IDs
        long callerId = 9876543210123;
        long targetUserId = 9876543210123;

        // Act
        var result = WelcomeCallbackParser.ValidateCallerIsTarget(callerId, targetUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion
}
