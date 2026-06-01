using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

[TestFixture]
public class TelegramEntityRendererTests
{
    [Test]
    public void Plain_text_with_no_entities_is_html_encoded()
    {
        var msg = TelegramMessage.Plain("a < b & c");

        Assert.That(TelegramEntityRenderer.ToHtml(msg), Is.EqualTo("a &lt; b &amp; c"));
    }

    [Test]
    public void Bold_entity_wraps_its_span_in_b_tag()
    {
        var msg = new TelegramMessageBuilder().Text("a ").Bold("banned").Build();

        Assert.That(TelegramEntityRenderer.ToHtml(msg), Is.EqualTo("a <b>banned</b>"));
    }

    [Test]
    public void Italic_and_code_and_link_render_to_expected_tags()
    {
        var msg = new TelegramMessageBuilder()
            .Italic("note")
            .Text(" ")
            .Code("x=1")
            .Text(" ")
            .Link("docs", "https://example.com/a?b=1&c=2")
            .Build();

        Assert.That(
            TelegramEntityRenderer.ToHtml(msg),
            Is.EqualTo("<i>note</i> <code>x=1</code> <a href=\"https://example.com/a?b=1&amp;c=2\">docs</a>"));
    }

    [Test]
    public void Text_mention_renders_as_styled_span_with_display_name()
    {
        var user = new UserIdentity(12345, "Sofi", "R", "rodriguez_sofi");
        var msg = new TelegramMessageBuilder().Text("Reported: ").Mention(user).Build();

        Assert.That(
            TelegramEntityRenderer.ToHtml(msg),
            Is.EqualTo("Reported: <span class=\"tg-mention\">Sofi R</span>"));
    }

    [Test]
    public void Inner_entity_text_is_html_encoded_not_injected()
    {
        // A user whose name contains markup must not be able to inject HTML into the preview.
        var user = new UserIdentity(1, "<script>", null, null);
        var msg = new TelegramMessageBuilder().Mention(user).Build();

        Assert.That(
            TelegramEntityRenderer.ToHtml(msg),
            Is.EqualTo("<span class=\"tg-mention\">&lt;script&gt;</span>"));
    }

    [Test]
    public void Emoji_offsets_render_correctly_because_utf16_aligns_with_dotnet_strings()
    {
        var msg = new TelegramMessageBuilder().Text("👍 ").Bold("ok").Build();

        Assert.That(TelegramEntityRenderer.ToHtml(msg), Is.EqualTo("👍 <b>ok</b>"));
    }

    [Test]
    public void Bad_token_survives_AppendTemplate_and_renders_as_normal_text()
    {
        // Round-trip: a mistyped placeholder passes through AppendTemplate as literal text,
        // then ToHtml renders it as ordinary (encoded) text — visible, not vanished.
        var user = new UserIdentity(1, "Sofi", null, "sofi");
        var msg = new TelegramMessageBuilder()
            .AppendTemplate("Hi {usernam}, welcome to {chat_name}", new Dictionary<string, Action<TelegramMessageBuilder>>
            {
                ["{username}"] = b => b.Mention(user),
                ["{chat_name}"] = b => b.Text("The Group"),
            })
            .Build();

        var html = TelegramEntityRenderer.ToHtml(msg);

        Assert.That(html, Is.EqualTo("Hi {usernam}, welcome to The Group"));
        Assert.That(html, Does.Contain("{usernam}"));
    }
}
