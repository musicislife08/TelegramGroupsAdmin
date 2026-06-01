using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

[TestFixture]
public class TelegramMessageBuilderTests
{
    [Test]
    public void Text_only_produces_no_entities()
    {
        var msg = new TelegramMessageBuilder().Text("hello world").Build();
        Assert.That(msg.Text, Is.EqualTo("hello world"));
        Assert.That(msg.Entities, Is.Empty);
    }

    [Test]
    public void Bold_records_offset_and_length_over_appended_text()
    {
        var msg = new TelegramMessageBuilder().Text("a ").Bold("banned").Build();
        Assert.That(msg.Text, Is.EqualTo("a banned"));
        Assert.That(msg.Entities, Has.Count.EqualTo(1));
        var e = msg.Entities[0];
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.Bold));
        Assert.That(e.Offset, Is.EqualTo(2));
        Assert.That(e.Length, Is.EqualTo(6));
    }

    [Test]
    public void Mention_emits_text_mention_with_embedded_user_and_display_name()
    {
        var user = new UserIdentity(12345, "Sofi", "R", "rodriguez_sofi");
        var msg = new TelegramMessageBuilder().Text("Reported user: ").Mention(user).Build();
        Assert.That(msg.Text, Is.EqualTo("Reported user: Sofi R"));
        Assert.That(msg.Entities, Has.Count.EqualTo(1));
        var e = msg.Entities[0];
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(e.Offset, Is.EqualTo("Reported user: ".Length));
        Assert.That(e.Length, Is.EqualTo("Sofi R".Length));
        Assert.That(e.User!.Id, Is.EqualTo(12345));
    }

    [Test]
    public void Mention_without_username_still_clickable_via_display_name()
    {
        var user = new UserIdentity(999, "NoUser", null, null);
        var msg = new TelegramMessageBuilder().Mention(user).Build();
        Assert.That(msg.Text, Is.EqualTo("NoUser"));
        Assert.That(msg.Entities[0].Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(msg.Entities[0].User!.Id, Is.EqualTo(999));
    }

    [Test]
    public void Offsets_count_utf16_code_units_not_runes()
    {
        var msg = new TelegramMessageBuilder().Text("👍 ").Bold("x").Build();
        Assert.That(msg.Text, Is.EqualTo("👍 x"));
        Assert.That(msg.Entities[0].Offset, Is.EqualTo(3));
        Assert.That(msg.Entities[0].Length, Is.EqualTo(1));
    }

    [Test]
    public void LineBreak_appends_newline()
    {
        var msg = new TelegramMessageBuilder().Text("a").LineBreak().Text("b").Build();
        Assert.That(msg.Text, Is.EqualTo("a\nb"));
    }

    [Test]
    public void AppendTemplate_substitutes_known_tokens_via_actions()
    {
        var user = new UserIdentity(7, "Sofi", null, "sofi");
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("Welcome {username} to {chat_name}!", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{username}"] = b => b.Mention(user),
                ["{chat_name}"] = b => b.Text("The Group"),
            })
            .Build();

        Assert.That(msg.Text, Is.EqualTo("Welcome Sofi to The Group!"));
        Assert.That(msg.Entities, Has.Count.EqualTo(1));
        var e = msg.Entities[0];
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(e.Offset, Is.EqualTo("Welcome ".Length));
        Assert.That(e.Length, Is.EqualTo("Sofi".Length));
        Assert.That(e.User!.Id, Is.EqualTo(7));
    }

    [Test]
    public void AppendTemplate_passes_unknown_token_through_as_literal_text()
    {
        // A mistyped placeholder ({usernam}) is not in the map — it must survive verbatim,
        // never be dropped, so it renders visibly for the admin to spot.
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("Hi {usernam}, welcome", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{username}"] = b => b.Text("Sofi"),
            })
            .Build();

        Assert.That(msg.Text, Is.EqualTo("Hi {usernam}, welcome"));
        Assert.That(msg.Entities, Is.Empty);
    }

    [Test]
    public void AppendTemplate_matches_tokens_left_to_right_regardless_of_map_order()
    {
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("{b}{a}", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{a}"] = x => x.Text("A"),
                ["{b}"] = x => x.Text("B"),
            })
            .Build();

        Assert.That(msg.Text, Is.EqualTo("BA"));
    }

    [Test]
    public void AppendTemplate_prefers_longer_key_when_tokens_share_an_offset()
    {
        // {username} and {username_extra} both match at offset 0. The greedy tie-break must pick
        // the LONGER key, so {username_extra}'s substitution wins and {username}'s never runs.
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("{username_extra} hi", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{username}"] = b => b.Text("SHORT"),
                ["{username_extra}"] = b => b.Text("LONG"),
            })
            .Build();

        Assert.That(msg.Text, Is.EqualTo("LONG hi"));
        Assert.That(msg.Text, Does.Contain("LONG"));
        Assert.That(msg.Text, Does.Not.Contain("SHORT"));
    }

    [Test]
    public void AppendTemplate_with_no_tokens_emits_template_verbatim()
    {
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("plain text, no tokens", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{username}"] = b => b.Text("Sofi"),
            })
            .Build();

        Assert.That(msg.Text, Is.EqualTo("plain text, no tokens"));
        Assert.That(msg.Entities, Is.Empty);
    }
}
