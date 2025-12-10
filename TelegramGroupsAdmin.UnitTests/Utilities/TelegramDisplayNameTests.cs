using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// Unit tests for TelegramDisplayName utility.
/// Tests both Format() for UI display and FormatMention() for bot messages.
/// </summary>
[TestFixture]
public class TelegramDisplayNameTests
{
    #region Format - Priority Order Tests

    [Test]
    public void Format_WithFirstAndLastName_ReturnsFullName()
    {
        var result = TelegramDisplayName.Format("John", "Doe", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    [Test]
    public void Format_WithOnlyFirstName_ReturnsFirstName()
    {
        var result = TelegramDisplayName.Format("John", null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("John"));
    }

    [Test]
    public void Format_WithOnlyLastName_ReturnsLastName()
    {
        var result = TelegramDisplayName.Format(null, "Doe", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("Doe"));
    }

    [Test]
    public void Format_WithNoName_ReturnsUsername()
    {
        var result = TelegramDisplayName.Format(null, null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void Format_WithNoNameOrUsername_ReturnsUserId()
    {
        var result = TelegramDisplayName.Format(null, null, null, 12345);
        Assert.That(result, Is.EqualTo("User 12345"));
    }

    [Test]
    public void Format_WithNothing_ReturnsUnknownUser()
    {
        var result = TelegramDisplayName.Format(null, null, null, null);
        Assert.That(result, Is.EqualTo("Unknown User"));
    }

    #endregion

    #region Format - Whitespace Handling

    [Test]
    public void Format_EmptyFirstName_FallsToUsername()
    {
        var result = TelegramDisplayName.Format("", null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void Format_WhitespaceFirstName_FallsToUsername()
    {
        var result = TelegramDisplayName.Format("   ", null, "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void Format_EmptyUsername_FallsToUserId()
    {
        var result = TelegramDisplayName.Format(null, null, "", 12345);
        Assert.That(result, Is.EqualTo("User 12345"));
    }

    [Test]
    public void Format_WhitespaceUsername_FallsToUserId()
    {
        var result = TelegramDisplayName.Format(null, null, "   ", 12345);
        Assert.That(result, Is.EqualTo("User 12345"));
    }

    [Test]
    public void Format_PreservesWhitespaceInNames()
    {
        // INTENTIONAL: We do NOT trim whitespace from names.
        // If Telegram allows spaces in names, we display them exactly as Telegram provides.
        // We are not in the business of "correcting" user data from the upstream platform.
        var result = TelegramDisplayName.Format("  John  ", "  Doe  ", null, 12345);
        Assert.That(result, Is.EqualTo("  John     Doe  "));
    }

    [Test]
    public void Format_BothNamesEmptyWithValidUsername_ReturnsUsername()
    {
        // Edge case: both firstName and lastName are empty but username is valid
        var result = TelegramDisplayName.Format("", "", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void Format_BothNamesWhitespaceWithValidUsername_ReturnsUsername()
    {
        var result = TelegramDisplayName.Format("   ", "   ", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    [Test]
    public void Format_EmptyFirstNameValidLastName_ReturnsLastName()
    {
        var result = TelegramDisplayName.Format("", "Doe", null, 12345);
        Assert.That(result, Is.EqualTo("Doe"));
    }

    [Test]
    public void Format_ValidFirstNameEmptyLastName_ReturnsFirstName()
    {
        var result = TelegramDisplayName.Format("John", "", null, 12345);
        Assert.That(result, Is.EqualTo("John"));
    }

    [Test]
    public void Format_WhitespaceFirstNameValidLastName_ReturnsLastName()
    {
        var result = TelegramDisplayName.Format("   ", "Doe", null, 12345);
        Assert.That(result, Is.EqualTo("Doe"));
    }

    #endregion

    #region Format - Service Account Tests

    [Test]
    public void Format_ServiceAccountWithChatName_ReturnsChatName()
    {
        var result = TelegramDisplayName.Format("Telegram", null, null, TelegramConstants.ServiceAccountUserId, "My Channel");
        Assert.That(result, Is.EqualTo("My Channel"));
    }

    [Test]
    public void Format_ServiceAccountWithoutChatName_ReturnsFallback()
    {
        var result = TelegramDisplayName.Format("Telegram", null, null, TelegramConstants.ServiceAccountUserId, null);
        Assert.That(result, Is.EqualTo("Telegram Service Account"));
    }

    [Test]
    public void Format_ServiceAccountEmptyChatName_ReturnsFallback()
    {
        var result = TelegramDisplayName.Format("Telegram", null, null, TelegramConstants.ServiceAccountUserId, "");
        Assert.That(result, Is.EqualTo("Telegram Service Account"));
    }

    [Test]
    public void Format_ServiceAccountWhitespaceChatName_ReturnsFallback()
    {
        var result = TelegramDisplayName.Format("Telegram", null, null, TelegramConstants.ServiceAccountUserId, "   ");
        Assert.That(result, Is.EqualTo("Telegram Service Account"));
    }

    [Test]
    public void Format_NonServiceAccountWithChatName_IgnoresChatName()
    {
        // chatName parameter should only apply to service account
        var result = TelegramDisplayName.Format("John", "Doe", null, 12345, "Some Channel");
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    #endregion

    #region Format - No @ Prefix

    [Test]
    public void Format_Username_NoAtPrefix()
    {
        // Format() is for UI display, should NOT have @ prefix
        var result = TelegramDisplayName.Format(null, null, "johndoe", 12345);
        Assert.That(result, Does.Not.StartWith("@"));
        Assert.That(result, Is.EqualTo("johndoe"));
    }

    #endregion

    #region FormatMention - Priority Order Tests

    [Test]
    public void FormatMention_WithUsername_ReturnsAtUsername()
    {
        var result = TelegramDisplayName.FormatMention("John", "Doe", "johndoe", 12345);
        Assert.That(result, Is.EqualTo("@johndoe"));
    }

    [Test]
    public void FormatMention_WithoutUsername_ReturnsFullName()
    {
        var result = TelegramDisplayName.FormatMention("John", "Doe", null, 12345);
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    [Test]
    public void FormatMention_WithoutUsernameOrLastName_ReturnsFirstName()
    {
        var result = TelegramDisplayName.FormatMention("John", null, null, 12345);
        Assert.That(result, Is.EqualTo("John"));
    }

    [Test]
    public void FormatMention_WithoutUsernameOrFirstName_ReturnsLastName()
    {
        var result = TelegramDisplayName.FormatMention(null, "Doe", null, 12345);
        Assert.That(result, Is.EqualTo("Doe"));
    }

    [Test]
    public void FormatMention_WithNoNameOrUsername_ReturnsUserId()
    {
        var result = TelegramDisplayName.FormatMention(null, null, null, 12345);
        Assert.That(result, Is.EqualTo("User 12345"));
    }

    [Test]
    public void FormatMention_WithNothing_ReturnsUnknownUser()
    {
        var result = TelegramDisplayName.FormatMention(null, null, null, null);
        Assert.That(result, Is.EqualTo("Unknown User"));
    }

    #endregion

    #region FormatMention - Whitespace Handling

    [Test]
    public void FormatMention_EmptyUsername_FallsToName()
    {
        var result = TelegramDisplayName.FormatMention("John", "Doe", "", 12345);
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    [Test]
    public void FormatMention_WhitespaceUsername_FallsToName()
    {
        var result = TelegramDisplayName.FormatMention("John", "Doe", "   ", 12345);
        Assert.That(result, Is.EqualTo("John Doe"));
    }

    #endregion

    #region FormatMention - @ Prefix

    [Test]
    public void FormatMention_Username_HasAtPrefix()
    {
        // FormatMention() is for bot messages, should have @ prefix for usernames
        var result = TelegramDisplayName.FormatMention(null, null, "johndoe", 12345);
        Assert.That(result, Does.StartWith("@"));
        Assert.That(result, Is.EqualTo("@johndoe"));
    }

    [Test]
    public void FormatMention_NoUsername_NoAtPrefix()
    {
        // When no username, should NOT add @ prefix to name
        var result = TelegramDisplayName.FormatMention("John", "Doe", null, 12345);
        Assert.That(result, Does.Not.StartWith("@"));
    }

    #endregion

    #region Edge Cases - Boundary Values

    [Test]
    public void Format_ZeroUserId_ReturnsUserZero()
    {
        // Boundary case: userId = 0 should still format as "User 0", not "Unknown User"
        var result = TelegramDisplayName.Format(null, null, null, 0);
        Assert.That(result, Is.EqualTo("User 0"));
    }

    [Test]
    public void FormatMention_ZeroUserId_ReturnsUserZero()
    {
        var result = TelegramDisplayName.FormatMention(null, null, null, 0);
        Assert.That(result, Is.EqualTo("User 0"));
    }

    [Test]
    public void Format_UsernameWithAtPrefix_DoesNotDoublePrefix()
    {
        // Data corruption scenario: username already has @ prefix stored
        // Format should NOT add another @ prefix - it passes through as-is
        var result = TelegramDisplayName.Format(null, null, "@johndoe", 12345);
        Assert.That(result, Is.EqualTo("@johndoe"));
        Assert.That(result, Does.Not.StartWith("@@"));
    }

    [Test]
    public void FormatMention_UsernameWithAtPrefix_ProducesDoublePrefix()
    {
        // INTENTIONAL: We do NOT strip the @ prefix before adding our own.
        // Telegram usernames cannot contain @ (only a-z, 0-9, underscore allowed).
        // If we see "@johndoe" in the username field, that's DATA CORRUPTION on our side.
        // Producing "@@johndoe" makes this bug VISIBLE so we can find and fix the root cause.
        // Silently trimming would hide the bug and make it harder to diagnose.
        var result = TelegramDisplayName.FormatMention(null, null, "@johndoe", 12345);
        Assert.That(result, Is.EqualTo("@@johndoe"));
    }

    #endregion

    #region Real-World Bug Scenario Tests

    [Test]
    public void Format_UserWithNameButNoUsername_ShowsFullName()
    {
        // This was the original bug: User "Jim Smith" with no username was showing "Unknown User"
        // because old code used `Username ?? FirstName` and didn't consider LastName
        var result = TelegramDisplayName.Format("Jim", "Smith", null, 1395388788);
        Assert.That(result, Is.EqualTo("Jim Smith"));
    }

    [Test]
    public void Format_UserWithEmptyUsername_ShowsFullName()
    {
        // Database has username='' (empty string), not null
        var result = TelegramDisplayName.Format("Jim", "Smith", "", 1395388788);
        Assert.That(result, Is.EqualTo("Jim Smith"));
    }

    [Test]
    public void FormatMention_UserWithNoUsername_ShowsFullNameWithoutAt()
    {
        // When mentioning in bot messages, users without username get their name (no @)
        var result = TelegramDisplayName.FormatMention("Jim", "Smith", null, 1395388788);
        Assert.That(result, Is.EqualTo("Jim Smith"));
        Assert.That(result, Does.Not.StartWith("@"));
    }

    #endregion
}
