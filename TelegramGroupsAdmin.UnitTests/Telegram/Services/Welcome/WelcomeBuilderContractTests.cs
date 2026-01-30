using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Contract tests verifying that keyboard builders produce data
/// that parsers can correctly consume. These round-trip tests ensure
/// the builders and parsers agree on format conventions.
///
/// Created: 2025-12-01 (REFACTOR-4 - Review feedback)
/// </summary>
[TestFixture]
public class WelcomeBuilderContractTests
{
    #region Keyboard Builder → Callback Parser Round-Trip Tests

    [Test]
    public void ChatModeKeyboard_AcceptButton_RoundTripsToParser()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "✅ Accept",
            DenyButtonText = "❌ Deny"
        };
        long expectedUserId = 12345;

        // Act: Build keyboard
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, expectedUserId);
        var acceptCallbackData = keyboard.InlineKeyboard.First().First().CallbackData;

        // Act: Parse callback data
        var parsed = WelcomeCallbackParser.ParseCallbackData(acceptCallbackData);

        // Assert: Round trip succeeds
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Type, Is.EqualTo(WelcomeCallbackType.Accept));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
        Assert.That(parsed.ChatId, Is.Null);
    }

    [Test]
    public void ChatModeKeyboard_DenyButton_RoundTripsToParser()
    {
        // Arrange
        var config = new WelcomeConfig
        {
            AcceptButtonText = "Accept",
            DenyButtonText = "Decline"
        };
        long expectedUserId = 99999;

        // Act: Build keyboard
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, expectedUserId);
        var denyCallbackData = keyboard.InlineKeyboard.First().Last().CallbackData;

        // Act: Parse callback data
        var parsed = WelcomeCallbackParser.ParseCallbackData(denyCallbackData);

        // Assert: Round trip succeeds
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Type, Is.EqualTo(WelcomeCallbackType.Deny));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
    }

    [Test]
    public void DmAcceptKeyboard_RoundTripsToParser()
    {
        // Arrange
        var config = new WelcomeConfig { AcceptButtonText = "Accept Rules" };
        long expectedGroupChatId = -1001234567890;
        long expectedUserId = 55555;

        // Act: Build keyboard
        var keyboard = WelcomeKeyboardBuilder.BuildDmAcceptKeyboard(config, expectedGroupChatId, expectedUserId);
        var callbackData = keyboard.InlineKeyboard.First().First().CallbackData;

        // Act: Parse callback data
        var parsed = WelcomeCallbackParser.ParseCallbackData(callbackData);

        // Assert: Round trip succeeds with all fields
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Type, Is.EqualTo(WelcomeCallbackType.DmAccept));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
        Assert.That(parsed.ChatId, Is.EqualTo(expectedGroupChatId));
    }

    [Test]
    public void ChatModeKeyboard_LargeUserId_RoundTripsCorrectly()
    {
        // Arrange: Large Telegram user IDs
        var config = new WelcomeConfig
        {
            AcceptButtonText = "Accept",
            DenyButtonText = "Deny"
        };
        long largeUserId = 9876543210123;

        // Act
        var keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, largeUserId);
        var callbackData = keyboard.InlineKeyboard.First().First().CallbackData;
        var parsed = WelcomeCallbackParser.ParseCallbackData(callbackData);

        // Assert
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.UserId, Is.EqualTo(largeUserId));
    }

    #endregion

    #region Deep Link Builder → Parser Round-Trip Tests

    [Test]
    public void DmModeKeyboard_DeepLink_RoundTripsToParser()
    {
        // Arrange
        var config = new WelcomeConfig { DmButtonText = "📋 Read Guidelines" };
        long expectedChatId = -1001234567890;
        long expectedUserId = 12345;
        string botUsername = "TestBot";

        // Act: Build keyboard with deep link
        var keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, expectedChatId, expectedUserId, botUsername);
        var deepLinkUrl = keyboard.InlineKeyboard.First().First().Url;

        // Extract payload from URL (everything after ?start=)
        var payload = deepLinkUrl!.Split("?start=")[1];

        // Act: Parse payload
        var parsed = WelcomeDeepLinkBuilder.ParseStartPayload(payload);

        // Assert: Round trip succeeds
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.ChatId, Is.EqualTo(expectedChatId));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
    }

    [Test]
    public void BuildStartDeepLink_RoundTripsToParser()
    {
        // Arrange
        long expectedChatId = -1009999999999;
        long expectedUserId = 1111111111;

        // Act: Build deep link directly
        var deepLink = WelcomeDeepLinkBuilder.BuildStartDeepLink("MyBot", expectedChatId, expectedUserId);

        // Extract payload
        var payload = deepLink.Split("?start=")[1];

        // Act: Parse payload
        var parsed = WelcomeDeepLinkBuilder.ParseStartPayload(payload);

        // Assert: Round trip succeeds
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.ChatId, Is.EqualTo(expectedChatId));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
    }

    [Test]
    public void BuildStartDeepLink_WithUnderscoresInBotName_RoundTripsCorrectly()
    {
        // Arrange: Bot usernames can contain underscores
        long expectedChatId = -100123;
        long expectedUserId = 456;

        // Act
        var deepLink = WelcomeDeepLinkBuilder.BuildStartDeepLink("My_Awesome_Bot", expectedChatId, expectedUserId);
        var payload = deepLink.Split("?start=")[1];
        var parsed = WelcomeDeepLinkBuilder.ParseStartPayload(payload);

        // Assert
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.ChatId, Is.EqualTo(expectedChatId));
        Assert.That(parsed.UserId, Is.EqualTo(expectedUserId));
    }

    #endregion
}
