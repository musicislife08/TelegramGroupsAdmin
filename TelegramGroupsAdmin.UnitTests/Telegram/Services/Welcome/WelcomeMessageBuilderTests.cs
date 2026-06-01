using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Tests for WelcomeMessageBuilder — entity-based messages built from admin templates via
/// AppendTemplate. The {username} token becomes a text_mention whose display name comes from
/// TelegramDisplayName.Format (first/last name, no @), so a user named "Test" renders as "Test".
/// </summary>
[TestFixture]
public class WelcomeMessageBuilderTests
{
    private static readonly UserIdentity TestUser = new(123, "Test", null, "testuser");

    private static WelcomeConfig CreateConfig(
        string mainMessage = "Welcome {username} to {chat_name}!",
        string dmTeaser = "Hi {username}, check your DMs for {chat_name}",
        int timeoutSeconds = 300,
        WelcomeMode mode = WelcomeMode.ChatAcceptDeny)
    {
        return new WelcomeConfig
        {
            MainWelcomeMessage = mainMessage,
            DmChatTeaserMessage = dmTeaser,
            TimeoutSeconds = timeoutSeconds,
            Mode = mode
        };
    }

    private static bool HasMentionOf(TelegramMessage message, long userId) =>
        message.Entities.Any(e => e.Type == MessageEntityType.TextMention && e.User!.Id == userId);

    [Test]
    public void FormatWelcomeMessage_ChatAcceptDeny_UsesMainMessage_WithMention()
    {
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(CreateConfig(), TestUser, "TestChat");

        Assert.That(result.Text, Does.Contain("Test"));
        Assert.That(result.Text, Does.Contain("TestChat"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void FormatWelcomeMessage_DmWelcomeMode_UsesTeaserMessage()
    {
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            CreateConfig(mode: WelcomeMode.DmWelcome), TestUser, "TestChat");

        Assert.That(result.Text, Does.Contain("check your DMs"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void FormatWelcomeMessage_SubstitutesTimeout()
    {
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            CreateConfig(mainMessage: "Welcome! You have {timeout} to respond."), TestUser, "TestChat");

        // 300 seconds = "5 minutes" via Humanizer
        Assert.That(result.Text, Does.Contain("5 minutes"));
    }

    [Test]
    public void FormatWelcomeMessage_UsernamelessUser_StillClickableViaTextMention()
    {
        var noUsername = new UserIdentity(999, "Friend", null, null);

        var result = WelcomeMessageBuilder.FormatWelcomeMessage(CreateConfig(), noUsername, "TestChat");

        Assert.That(result.Text, Does.Contain("Friend"));
        Assert.That(HasMentionOf(result, 999), Is.True);
    }

    [Test]
    public void FormatWelcomeMessage_UnknownToken_RendersAsLiteralText()
    {
        var result = WelcomeMessageBuilder.FormatWelcomeMessage(
            CreateConfig(mainMessage: "Hi {usernam}!"), TestUser, "TestChat");

        // A mistyped placeholder is not substituted — it survives verbatim, visibly.
        Assert.That(result.Text, Is.EqualTo("Hi {usernam}!"));
        Assert.That(result.Entities, Is.Empty);
    }

    [Test]
    public void FormatRulesConfirmation_AddsFooter_AndMentions()
    {
        var result = WelcomeMessageBuilder.FormatRulesConfirmation(CreateConfig(), TestUser, "TestChat");

        Assert.That(result.Text, Does.Contain("You're all set"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void FormatExamIntro_UsesMainMessage_WithMention()
    {
        var result = WelcomeMessageBuilder.FormatExamIntro(CreateConfig(), TestUser, "TestChat");

        Assert.That(result.Text, Does.Contain("Test"));
        Assert.That(result.Text, Does.Contain("TestChat"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void FormatDmAcceptanceConfirmation_IncludesChatNameAndWelcome()
    {
        var result = WelcomeMessageBuilder.FormatDmAcceptanceConfirmation("TestChat");

        Assert.That(result, Does.Contain("TestChat"));
        Assert.That(result, Does.Contain("Welcome"));
    }
}
