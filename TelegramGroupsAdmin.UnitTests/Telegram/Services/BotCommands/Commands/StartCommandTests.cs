using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// Unit tests for the StartCommand.BuildWelcomeMessage helper.
/// The helper is tested directly (internal + InternalsVisibleTo) because
/// HandleWelcomeDeepLinkAsync requires deep Telegram API + DI setup that would
/// not add coverage value over testing the pure formatting logic.
/// </summary>
[TestFixture]
public class StartCommandTests
{
    private static readonly UserIdentity UserWithUsername = new(
        Id: 123L,
        FirstName: "Alice",
        LastName: "Smith",
        Username: "alice_smith");

    private static readonly UserIdentity UserWithoutUsername = new(
        Id: 456L,
        FirstName: "Bob",
        LastName: null,
        Username: null);

    // -----------------------------------------------------------------------
    // Basic substitution
    // -----------------------------------------------------------------------

    [Test]
    public void BuildWelcomeMessage_AllPlaceholders_ContainsTextMentionEntity()
    {
        const string template = "Hello {username}, welcome to {chat_name}! You have {timeout} seconds.";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Test Chat", 60);

        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserWithUsername.Id));
    }

    [Test]
    public void BuildWelcomeMessage_AllPlaceholders_ContainsLiteralSegmentsInText()
    {
        const string template = "Hello {username}, welcome to {chat_name}! You have {timeout} seconds.";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Test Chat", 60);

        Assert.That(result.Text, Does.Contain("Hello "));
        Assert.That(result.Text, Does.Contain("Test Chat"));
        Assert.That(result.Text, Does.Contain("60"));
        Assert.That(result.Text, Does.Contain(" seconds."));
    }

    [Test]
    public void BuildWelcomeMessage_AllPlaceholders_NoHtmlTagsOrTgUri()
    {
        const string template = "Hello {username}, welcome to {chat_name}! You have {timeout} seconds.";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Test Chat", 60);

        Assert.That(result.Text, Does.Not.Contain("<a href"));
        Assert.That(result.Text, Does.Not.Contain("tg://"));
        Assert.That(result.Text, Does.Not.Contain("{username}"));
        Assert.That(result.Text, Does.Not.Contain("{chat_name}"));
        Assert.That(result.Text, Does.Not.Contain("{timeout}"));
    }

    // -----------------------------------------------------------------------
    // User without username
    // -----------------------------------------------------------------------

    [Test]
    public void BuildWelcomeMessage_UserWithoutUsername_StillEmitsTextMentionEntity()
    {
        const string template = "Hi {username}!";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithoutUsername, "My Group", 30);

        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserWithoutUsername.Id));
    }

    // -----------------------------------------------------------------------
    // Placeholder order variants
    // -----------------------------------------------------------------------

    [Test]
    public void BuildWelcomeMessage_ChatNameBeforeUsername_BothPresent()
    {
        const string template = "Group: {chat_name} — user: {username} — time: {timeout}s";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Cool Group", 45);

        Assert.That(result.Text, Does.Contain("Cool Group"));
        Assert.That(result.Text, Does.Contain("45"));
        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserWithUsername.Id));
    }

    [Test]
    public void BuildWelcomeMessage_TimeoutBeforeOthers_AllPresent()
    {
        const string template = "{timeout} seconds left, {username}, in {chat_name}.";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Speedy Group", 10);

        Assert.That(result.Text, Does.Contain("10"));
        Assert.That(result.Text, Does.Contain("Speedy Group"));
        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserWithUsername.Id));
        Assert.That(result.Text, Does.Not.Contain("{timeout}"));
    }

    // -----------------------------------------------------------------------
    // No placeholder
    // -----------------------------------------------------------------------

    [Test]
    public void BuildWelcomeMessage_NoPlaceholders_PlainTextPassedThrough()
    {
        const string template = "Welcome! Please read the rules.";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Any Chat", 30);

        Assert.That(result.Text, Is.EqualTo("Welcome! Please read the rules."));
        Assert.That(result.Entities, Is.Empty.Or.Null);
    }

    // -----------------------------------------------------------------------
    // Repeated placeholders
    // -----------------------------------------------------------------------

    [Test]
    public void BuildWelcomeMessage_RepeatedUsernamePlaceholder_BothMentionsPresent()
    {
        const string template = "{username} is here. Say hi to {username}!";

        var result = StartCommand.BuildWelcomeMessage(template, UserWithUsername, "Greet Chat", 60);

        var mentionCount = result.Entities?.Count(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserWithUsername.Id) ?? 0;

        Assert.That(mentionCount, Is.EqualTo(2));
    }
}
