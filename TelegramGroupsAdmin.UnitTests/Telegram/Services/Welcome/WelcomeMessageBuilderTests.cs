using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
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

    // -----------------------------------------------------------------------
    // BuildFromTemplate — the shared raw-template path used by the StartCommand
    // deep-link welcome send and the admin live-preview. {timeout} is humanized
    // (Humanizer), NOT a raw integer.
    // -----------------------------------------------------------------------

    [Test]
    public void BuildFromTemplate_AllPlaceholders_MentionAndHumanizedTimeout()
    {
        const string template = "Hello {username}, welcome to {chat_name}! You have {timeout} left.";

        var result = WelcomeMessageBuilder.BuildFromTemplate(template, TestUser, "Test Chat", 90);

        Assert.That(result.Text, Does.Contain("Test Chat"));
        // 90 seconds humanizes to "1 minute, 30 seconds" (precision: 2) — not the raw "90".
        Assert.That(result.Text, Does.Contain("1 minute, 30 seconds"));
        Assert.That(result.Text, Does.Not.Contain("90"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void BuildFromTemplate_UsernamelessUser_StillEmitsTextMention()
    {
        var noUsername = new UserIdentity(456, "Bob", null, null);

        var result = WelcomeMessageBuilder.BuildFromTemplate("Hi {username}!", noUsername, "My Group", 30);

        Assert.That(result.Text, Does.Contain("Bob"));
        Assert.That(HasMentionOf(result, 456), Is.True);
    }

    [Test]
    public void BuildFromTemplate_PlaceholdersInAnyOrder_AllSubstituted()
    {
        const string template = "{timeout} left, {username}, in {chat_name}.";

        var result = WelcomeMessageBuilder.BuildFromTemplate(template, TestUser, "Speedy Group", 10);

        Assert.That(result.Text, Does.Contain("10 seconds"));
        Assert.That(result.Text, Does.Contain("Speedy Group"));
        Assert.That(result.Text, Does.Not.Contain("{timeout}"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void BuildFromTemplate_RepeatedUsername_EmitsTwoMentions()
    {
        const string template = "{username} is here. Say hi to {username}!";

        var result = WelcomeMessageBuilder.BuildFromTemplate(template, TestUser, "Greet Chat", 60);

        var mentionCount = result.Entities.Count(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == 123);
        Assert.That(mentionCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildFromTemplate_NoPlaceholders_PlainTextPassedThrough()
    {
        const string template = "Welcome! Please read the rules.";

        var result = WelcomeMessageBuilder.BuildFromTemplate(template, TestUser, "Any Chat", 30);

        Assert.That(result.Text, Is.EqualTo("Welcome! Please read the rules."));
        Assert.That(result.Entities, Is.Empty);
    }

    // -----------------------------------------------------------------------
    // BuildBypassTemplate — trusted-bypass announcements. Only {username} and
    // {chat_name} are substituted; {timeout} is NOT a bypass variable.
    // -----------------------------------------------------------------------

    [Test]
    public void BuildBypassTemplate_SubstitutesMentionAndChatName()
    {
        const string template = "{username} welcomed automatically into {chat_name}.";

        var result = WelcomeMessageBuilder.BuildBypassTemplate(template, TestUser, "Example Chat");

        Assert.That(result.Text, Does.Contain("Test"));
        Assert.That(result.Text, Does.Contain("Example Chat"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }

    [Test]
    public void BuildBypassTemplate_TimeoutTokenNotASubstitution_RendersLiteral()
    {
        const string template = "{username} joined. {timeout}";

        var result = WelcomeMessageBuilder.BuildBypassTemplate(template, TestUser, "Example Chat");

        // {timeout} is not a bypass variable — it falls through as literal text, visibly.
        Assert.That(result.Text, Does.Contain("{timeout}"));
        Assert.That(HasMentionOf(result, 123), Is.True);
    }
}
