using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// Unit tests for LogDisplayName utility.
/// Tests Info vs Debug patterns for chats, Telegram users, and web users.
/// </summary>
[TestFixture]
public class LogDisplayNameTests
{
    #region ChatInfo Tests

    [Test]
    public void ChatInfo_WithName_ReturnsNameOnly()
    {
        var result = LogDisplayName.ChatInfo("My Chat", -100123456);
        Assert.That(result, Is.EqualTo("My Chat"));
    }

    [Test]
    public void ChatInfo_WithNullName_ReturnsChatId()
    {
        var result = LogDisplayName.ChatInfo(null, -100123456);
        Assert.That(result, Is.EqualTo("Chat -100123456"));
    }

    [Test]
    public void ChatInfo_WithEmptyName_ReturnsChatId()
    {
        var result = LogDisplayName.ChatInfo("", -100123456);
        Assert.That(result, Is.EqualTo("Chat -100123456"));
    }

    [Test]
    public void ChatInfo_WithWhitespaceName_ReturnsChatId()
    {
        var result = LogDisplayName.ChatInfo("   ", -100123456);
        Assert.That(result, Is.EqualTo("Chat -100123456"));
    }

    #endregion

    #region ChatDebug Tests

    [Test]
    public void ChatDebug_WithName_ReturnsNameAndId()
    {
        var result = LogDisplayName.ChatDebug("My Chat", -100123456);
        Assert.That(result, Is.EqualTo("My Chat (-100123456)"));
    }

    [Test]
    public void ChatDebug_WithNullName_ReturnsUnknownChatAndId()
    {
        var result = LogDisplayName.ChatDebug(null, -100123456);
        Assert.That(result, Is.EqualTo("Unknown Chat (-100123456)"));
    }

    [Test]
    public void ChatDebug_WithEmptyName_ReturnsUnknownChatAndId()
    {
        var result = LogDisplayName.ChatDebug("", -100123456);
        Assert.That(result, Is.EqualTo("Unknown Chat (-100123456)"));
    }

    [Test]
    public void ChatDebug_WithWhitespaceName_ReturnsUnknownChatAndId()
    {
        var result = LogDisplayName.ChatDebug("   ", -100123456);
        Assert.That(result, Is.EqualTo("Unknown Chat (-100123456)"));
    }

    #endregion

    #region UserInfo Tests (4-parameter overload)

    [Test]
    public void UserInfo_WithFullName_ReturnsFullName()
    {
        var result = LogDisplayName.UserInfo("John", "Doe", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    [Test]
    public void UserInfo_WithOnlyUsername_ReturnsUsername()
    {
        var result = LogDisplayName.UserInfo(null, null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void UserInfo_WithNoNameOrUsername_ReturnsUserId()
    {
        var result = LogDisplayName.UserInfo(null, null, null, 12345);
        Assert.That(result, Is.EqualTo("User 12345"));
    }

    [Test]
    public void UserInfo_DelegatesToTelegramDisplayNameFormat()
    {
        // UserInfo should produce identical output to TelegramDisplayName.Format
        var logResult = LogDisplayName.UserInfo("John", "Doe", "johndoe", 12345);
        var telegramResult = TelegramDisplayName.Format("John", "Doe", "johndoe", 12345);
        Assert.That(logResult, Is.EqualTo(telegramResult));
    }

    #endregion

    #region UserDebug Tests (4-parameter overload)

    [Test]
    public void UserDebug_WithFullName_ReturnsNameAndId()
    {
        var result = LogDisplayName.UserDebug("John", "Doe", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("John Doe (12345)"));
    }

    [Test]
    public void UserDebug_WithOnlyUsername_ReturnsUsernameAndId()
    {
        var result = LogDisplayName.UserDebug(null, null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe (12345)"));
    }

    [Test]
    public void UserDebug_WithNoNameOrUsername_ReturnsUserIdTwice()
    {
        // When no name available, shows "User 12345 (12345)"
        var result = LogDisplayName.UserDebug(null, null, null, 12345);
        Assert.That(result, Is.EqualTo("User 12345 (12345)"));
    }

    [Test]
    public void UserDebug_AlwaysIncludesId()
    {
        // Even with a name, the ID should always be appended
        var result = LogDisplayName.UserDebug("John", "Doe", "johndoe", 12345);
        Assert.That(result, Does.Contain("(12345)"));
    }

    #endregion

    #region UserDebug Tests (2-parameter overload - pre-formatted display name)

    [Test]
    public void UserDebug_PreFormatted_WithDisplayName_ReturnsNameAndId()
    {
        var result = LogDisplayName.UserDebug("John Doe", 12345);
        Assert.That(result, Is.EqualTo("John Doe (12345)"));
    }

    [Test]
    public void UserDebug_PreFormatted_WithNullDisplayName_ReturnsFallbackAndId()
    {
        var result = LogDisplayName.UserDebug(null, 12345);
        Assert.That(result, Is.EqualTo("User 12345 (12345)"));
    }

    [Test]
    public void UserDebug_PreFormatted_WithUsername_ReturnsUsernameAndId()
    {
        var result = LogDisplayName.UserDebug("johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe (12345)"));
    }

    [Test]
    public void UserDebug_PreFormatted_AlwaysIncludesId()
    {
        var result = LogDisplayName.UserDebug("Any Name", 99999);
        Assert.That(result, Does.EndWith("(99999)"));
    }

    [Test]
    public void UserDebug_PreFormatted_EmptyString_ReturnsFallbackAndId()
    {
        // Empty string should be treated same as null - falls back to User ID
        var result = LogDisplayName.UserDebug("", 12345);
        Assert.That(result, Is.EqualTo("User 12345 (12345)"));
    }

    [Test]
    public void UserDebug_PreFormatted_WhitespaceString_ReturnsFallbackAndId()
    {
        // Whitespace should be treated same as null - falls back to User ID
        var result = LogDisplayName.UserDebug("   ", 12345);
        Assert.That(result, Is.EqualTo("User 12345 (12345)"));
    }

    #endregion

    #region WebUserInfo Tests

    [Test]
    public void WebUserInfo_WithEmail_ReturnsEmail()
    {
        var result = LogDisplayName.WebUserInfo("john@example.com", "abc-123");
        Assert.That(result, Is.EqualTo("john@example.com"));
    }

    [Test]
    public void WebUserInfo_WithNullEmail_ReturnsUserId()
    {
        var result = LogDisplayName.WebUserInfo(null, "abc-123");
        Assert.That(result, Is.EqualTo("User abc-123"));
    }

    [Test]
    public void WebUserInfo_WithEmptyEmail_ReturnsUserId()
    {
        var result = LogDisplayName.WebUserInfo("", "abc-123");
        Assert.That(result, Is.EqualTo("User abc-123"));
    }

    [Test]
    public void WebUserInfo_WithWhitespaceEmail_ReturnsUserId()
    {
        var result = LogDisplayName.WebUserInfo("   ", "abc-123");
        Assert.That(result, Is.EqualTo("User abc-123"));
    }

    #endregion

    #region WebUserDebug Tests

    [Test]
    public void WebUserDebug_WithEmail_ReturnsEmailAndId()
    {
        var result = LogDisplayName.WebUserDebug("john@example.com", "abc-123");
        Assert.That(result, Is.EqualTo("john@example.com (abc-123)"));
    }

    [Test]
    public void WebUserDebug_WithNullEmail_ReturnsUnknownUserAndId()
    {
        var result = LogDisplayName.WebUserDebug(null, "abc-123");
        Assert.That(result, Is.EqualTo("Unknown User (abc-123)"));
    }

    [Test]
    public void WebUserDebug_WithEmptyEmail_ReturnsUnknownUserAndId()
    {
        var result = LogDisplayName.WebUserDebug("", "abc-123");
        Assert.That(result, Is.EqualTo("Unknown User (abc-123)"));
    }

    [Test]
    public void WebUserDebug_WithWhitespaceEmail_ReturnsUnknownUserAndId()
    {
        var result = LogDisplayName.WebUserDebug("   ", "abc-123");
        Assert.That(result, Is.EqualTo("Unknown User (abc-123)"));
    }

    [Test]
    public void WebUserDebug_AlwaysIncludesId()
    {
        var result = LogDisplayName.WebUserDebug("john@example.com", "abc-123");
        Assert.That(result, Does.Contain("(abc-123)"));
    }

    #endregion

    #region Pattern Consistency Tests

    [Test]
    public void InfoMethods_NeverIncludeId_WhenNameAvailable()
    {
        using (Assert.EnterMultipleScope())
        {
            // Info methods should return name only (no ID) when name is available
            Assert.That(LogDisplayName.ChatInfo("My Chat", -100123), Does.Not.Contain("-100123"));
            Assert.That(LogDisplayName.UserInfo("John", "Doe", null, 12345), Does.Not.Contain("12345"));
            Assert.That(LogDisplayName.WebUserInfo("john@example.com", "abc-123"), Does.Not.Contain("abc-123"));
        }
    }

    [Test]
    public void DebugMethods_AlwaysIncludeId()
    {
        using (Assert.EnterMultipleScope())
        {
            // Debug methods should always include ID for investigation
            Assert.That(LogDisplayName.ChatDebug("My Chat", -100123), Does.Contain("-100123"));
            Assert.That(LogDisplayName.UserDebug("John", "Doe", null, 12345), Does.Contain("12345"));
            Assert.That(LogDisplayName.UserDebug("John Doe", 12345), Does.Contain("12345"));
            Assert.That(LogDisplayName.WebUserDebug("john@example.com", "abc-123"), Does.Contain("abc-123"));
        }
    }

    #endregion
}
