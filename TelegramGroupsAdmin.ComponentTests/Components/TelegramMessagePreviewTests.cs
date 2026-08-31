using Bunit;
using NUnit.Framework;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TelegramMessagePreview.razor — verifies it renders a TelegramMessage
/// (text + entities) into the bot bubble via TelegramEntityRenderer, so the preview reflects
/// exactly what Telegram is sent.
/// </summary>
public class TelegramMessagePreviewTests : MudBlazorTestContext
{
    [Test]
    public void Renders_plain_message_text_inside_bot_bubble()
    {
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(c => c.Message, TelegramMessage.Plain("Hello world")));

        var bubble = cut.Find(".telegram-bubble-bot .telegram-message-text");
        Assert.That(bubble.TextContent, Does.Contain("Hello world"));
    }

    [Test]
    public void Renders_bold_entity_as_bold_tag()
    {
        var message = new TelegramMessageBuilder().Text("a ").Bold("banned").Build();

        var cut = Render<TelegramMessagePreview>(p => p.Add(c => c.Message, message));

        var bubble = cut.Find(".telegram-bubble-bot .telegram-message-text");
        Assert.That(bubble.InnerHtml, Does.Contain("<b>banned</b>"));
    }

    [Test]
    public void Renders_text_mention_as_styled_span()
    {
        var message = new TelegramMessageBuilder()
            .Text("Hi ")
            .Mention(new UserIdentity(1, "Sofi", null, null))
            .Build();

        var cut = Render<TelegramMessagePreview>(p => p.Add(c => c.Message, message));

        var bubble = cut.Find(".telegram-bubble-bot .telegram-message-text");
        Assert.That(bubble.InnerHtml, Does.Contain("<span class=\"tg-mention\">Sofi</span>"));
    }

    [Test]
    public void Html_encodes_message_text_so_markup_cannot_be_injected()
    {
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(c => c.Message, TelegramMessage.Plain("a < b & c")));

        var bubble = cut.Find(".telegram-bubble-bot .telegram-message-text");
        Assert.That(bubble.InnerHtml, Does.Contain("a &lt; b &amp; c"));
    }

    [Test]
    public void Shows_user_command_bubble_when_enabled()
    {
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(c => c.Message, TelegramMessage.Plain("Bot response"))
            .Add(c => c.ShowUserCommand, true));

        var userBubble = cut.Find(".telegram-bubble-user");
        Assert.That(userBubble.TextContent, Does.Contain("/start"));
    }

    [Test]
    public void Hides_user_command_bubble_when_disabled()
    {
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(c => c.Message, TelegramMessage.Plain("Bot response"))
            .Add(c => c.ShowUserCommand, false));

        Assert.That(cut.FindAll(".telegram-bubble-user"), Is.Empty);
    }

    [Test]
    public void Renders_inline_keyboard_buttons()
    {
        var cut = Render<TelegramMessagePreview>(p => p
            .Add(c => c.Message, TelegramMessage.Plain("Pick one"))
            .Add(c => c.Buttons, new[] { new[] { "Accept", "Decline" } }));

        var buttons = cut.FindAll(".telegram-inline-button");
        Assert.That(buttons, Has.Count.EqualTo(2));
        Assert.That(buttons[0].TextContent, Does.Contain("Accept"));
        Assert.That(buttons[1].TextContent, Does.Contain("Decline"));
    }
}
