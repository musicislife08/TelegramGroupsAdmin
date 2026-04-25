using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class NotificationRendererTests
{
    #region ToTelegramMessage Tests

    [Test]
    public void ToTelegramMessage_SubjectIsBoldOnFirstLine()
    {
        var payload = NotificationPayloadBuilder.Create("Alert Title").Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        Assert.That(rendered.Text, Does.StartWith("Alert Title"));
        Assert.That(rendered.Entities, Has.Some.Matches<MessageEntity>(e =>
            e.Type == MessageEntityType.Bold &&
            e.Offset == 0 &&
            e.Length == "Alert Title".Length));
    }

    [Test]
    public void ToTelegramMessage_FieldWithoutUser_EmitsBoldLabelAndPlainValue()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Chat", "MyGroup")
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        // Label is bold
        var labelEntity = rendered.Entities.FirstOrDefault(e =>
            e.Type == MessageEntityType.Bold &&
            rendered.Text.Substring(e.Offset, e.Length) == "Chat:");
        Assert.That(labelEntity, Is.Not.Null);

        // No TextMention anywhere
        Assert.That(rendered.Entities, Has.None.Matches<MessageEntity>(e =>
            e.Type == MessageEntityType.TextMention));

        Assert.That(rendered.Text, Does.Contain("Chat: MyGroup"));
    }

    [Test]
    public void ToTelegramMessage_FieldWithUser_EmitsTextMentionWithEmbeddedUser()
    {
        var user = new UserIdentity(
            Id: 12345,
            FirstName: "Alice",
            LastName: "Smith",
            Username: "alice_s");
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", user)
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        var mention = rendered.Entities.FirstOrDefault(e =>
            e.Type == MessageEntityType.TextMention);
        Assert.That(mention, Is.Not.Null, "user field should produce TextMention");
        Assert.That(mention!.User, Is.Not.Null);
        Assert.That(mention.User!.Id, Is.EqualTo(12345));
        Assert.That(mention.User.FirstName, Is.EqualTo("Alice"));
        Assert.That(mention.User.LastName, Is.EqualTo("Smith"));
        Assert.That(mention.User.Username, Is.EqualTo("alice_s"));

        var span = rendered.Text.Substring(mention.Offset, mention.Length);
        Assert.That(span, Is.EqualTo(user.DisplayName));
    }

    [Test]
    public void ToTelegramMessage_SectionHeaderIsBold()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithSection("Analysis", s => s.WithField("Score", "1.0"))
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        var headerEntity = rendered.Entities.FirstOrDefault(e =>
            e.Type == MessageEntityType.Bold &&
            rendered.Text.Substring(e.Offset, e.Length) == "Analysis");
        Assert.That(headerEntity, Is.Not.Null);
    }

    [Test]
    public void ToTelegramMessage_EntityOffsetsMatchTextForNonBmpCharacters()
    {
        // UserIdentity display name will include the emoji; offset/length must be correct
        var user = new UserIdentity(Id: 1, FirstName: "\U0001F464User", LastName: null, Username: null);
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", user)
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        var mention = rendered.Entities.Single(e =>
            e.Type == MessageEntityType.TextMention);
        var span = rendered.Text.Substring(mention.Offset, mention.Length);
        Assert.That(span, Is.EqualTo(user.DisplayName));
    }

    [Test]
    public void ToTelegramMessage_ComplexPayload_OrderPreserved()
    {
        var alice = new UserIdentity(Id: 111, FirstName: "Alice", LastName: null, Username: null);
        var payload = NotificationPayloadBuilder.Create("Spam Banned")
            .WithField("User", alice)
            .WithField("Chat", "Test Group")
            .WithSection("Detection", s => s
                .WithField("Confidence", "95%")
                .WithField("Reason", "Known spam pattern"))
            .WithSection("Action", s => s
                .WithText("Banned from 3 chats"))
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        // Verify structural ordering
        var userIdx = rendered.Text.IndexOf(alice.DisplayName, StringComparison.Ordinal);
        var chatIdx = rendered.Text.IndexOf("Test Group", StringComparison.Ordinal);
        var detectionIdx = rendered.Text.IndexOf("Detection", StringComparison.Ordinal);
        var actionIdx = rendered.Text.IndexOf("Action", StringComparison.Ordinal);

        Assert.That(userIdx, Is.GreaterThanOrEqualTo(0));
        Assert.That(chatIdx, Is.GreaterThan(userIdx));
        Assert.That(detectionIdx, Is.GreaterThan(chatIdx));
        Assert.That(actionIdx, Is.GreaterThan(detectionIdx));
    }

    [Test]
    public void ToTelegramMessage_ReporterFromTelegramActor_RendersClickableMention()
    {
        // NotificationService.SendReportNotificationAsync builds a UserIdentity from an Actor
        // using Actor.DisplayName as the UserIdentity.FirstName so TelegramDisplayName.Format
        // returns the actor's display name via the full-name branch. The rendered TextMention
        // needs Actor.TelegramUserId for profile linking and the displayed text at the entity's
        // offset/length window must equal the actor's display name.
        var reporterActor = Actor.FromTelegramUser(54321L, "alice_a", "Alice", "Anderson");
        var reporterAsIdentity = new UserIdentity(
            reporterActor.TelegramUserId!.Value,
            FirstName: reporterActor.DisplayName,
            LastName: null,
            Username: null);

        var payload = NotificationPayloadBuilder.Create("Message Reported")
            .WithField("Reported by", reporterAsIdentity)
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        var mention = rendered.Entities.Single(e => e.Type == MessageEntityType.TextMention);
        Assert.That(mention.User, Is.Not.Null);
        Assert.That(mention.User!.Id, Is.EqualTo(54321L));

        var span = rendered.Text.Substring(mention.Offset, mention.Length);
        Assert.That(span, Is.EqualTo("Alice Anderson"), "mention text must equal actor display name");
    }

    [Test]
    public void ToTelegramMessage_ReporterFromSystemActor_RendersPlainTextNotClickable()
    {
        // Automated reporter (Auto-Detection, CAS, etc.) should NOT render as a clickable mention.
        // NotificationService calls builder.WithField("Reported by", actor.GetDisplayText()) for
        // non-Telegram actor types, which produces a plain label+value field.
        var payload = NotificationPayloadBuilder.Create("Message Reported")
            .WithField("Reported by", Actor.AutoDetection.GetDisplayText())
            .Build();

        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        Assert.That(rendered.Text, Does.Contain("Auto-Detection"));
        Assert.That(rendered.Entities, Has.None.Matches<MessageEntity>(e =>
            e.Type == MessageEntityType.TextMention));
    }

    #endregion

    #region ToEmailHtml Tests

    [Test]
    public void ToEmailHtml_ContainsHtmlStructure()
    {
        var payload = NotificationPayloadBuilder.Create("Test").Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<html>"));
        Assert.That(html, Does.Contain("</html>"));
        Assert.That(html, Does.Contain("class=\"container\""));
    }

    [Test]
    public void ToEmailHtml_SubjectRenderedAsH2()
    {
        var payload = NotificationPayloadBuilder.Create("Spam Alert").Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("<h2>Spam Alert</h2>"));
    }

    [Test]
    public void ToEmailHtml_TextBlock_RenderedAsParagraph()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithText("Something happened")
            .Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("<p>Something happened</p>"));
    }

    [Test]
    public void ToEmailHtml_Field_RenderedWithCssClasses()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Status", "Banned")
            .Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("class=\"field\""));
        Assert.That(html, Does.Contain("class=\"field-label\""));
        Assert.That(html, Does.Contain("Status:"));
        Assert.That(html, Does.Contain("Banned"));
    }

    [Test]
    public void ToEmailHtml_FieldWithUser_NoTgLink()
    {
        var user = new UserIdentity(Id: 12345, FirstName: "Alice", LastName: null, Username: null);
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", user)
            .Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        // tg://user links aren't actionable in email — should render as plain text
        Assert.That(html, Does.Not.Contain("tg://user"));
        Assert.That(html, Does.Contain("Alice"));
    }

    [Test]
    public void ToEmailHtml_Section_RenderedAsH3()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithSection("Detection Details", s => s
                .WithField("Method", "ML"))
            .Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("<h3>Detection Details</h3>"));
    }

    [Test]
    public void ToEmailHtml_ContainsFooter()
    {
        var payload = NotificationPayloadBuilder.Create("Alert").Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Contain("class=\"footer\""));
        Assert.That(html, Does.Contain("automated notification from TelegramGroupsAdmin"));
    }

    [Test]
    public void ToEmailHtml_EscapesHtmlInContent()
    {
        var payload = NotificationPayloadBuilder.Create("<script>alert('xss')</script>")
            .WithField("Reason", "Contains <b>bold</b> & stuff")
            .Build();

        var html = NotificationRenderer.ToEmailHtml(payload);

        Assert.That(html, Does.Not.Contain("<script>alert"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Contain("&lt;b&gt;bold&lt;/b&gt;"));
    }

    #endregion

    #region ToPlainText Tests

    [Test]
    public void ToPlainText_SubjectOnFirstLine()
    {
        var payload = NotificationPayloadBuilder.Create("Backup Failed").Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.StartWith("Backup Failed"));
    }

    [Test]
    public void ToPlainText_TextBlock_RenderedPlain()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithText("Something happened")
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Contain("Something happened"));
    }

    [Test]
    public void ToPlainText_Field_RenderedAsLabelColon()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Status", "Banned")
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Contain("Status: Banned"));
    }

    [Test]
    public void ToPlainText_FieldWithUser_NoLink()
    {
        var user = new UserIdentity(Id: 12345, FirstName: "Alice", LastName: null, Username: null);
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", user)
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Not.Contain("tg://user"));
        Assert.That(text, Does.Contain("User: "));
        Assert.That(text, Does.Contain("Alice"));
    }

    [Test]
    public void ToPlainText_Section_RenderedWithIndentation()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithSection("Details", s => s
                .WithField("Confidence", "95%")
                .WithText("Auto-detected"))
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Contain("Details"));
        Assert.That(text, Does.Contain("  Confidence: 95%"));
        Assert.That(text, Does.Contain("  Auto-detected"));
    }

    [Test]
    public void ToPlainText_NoHtmlFormatting()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Chat", "Test Group")
            .WithSection("Info", s => s.WithText("Some info"))
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Not.Contain("<b>"));
        Assert.That(text, Does.Not.Contain("</b>"));
        Assert.That(text, Does.Not.Contain("<h"));
        Assert.That(text, Does.Not.Contain("<p>"));
    }

    [Test]
    public void ToPlainText_SpecialChars_NotEscaped()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithText("Contains <angle brackets> & ampersand")
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        // Plain text should NOT escape — these are literal characters
        Assert.That(text, Does.Contain("Contains <angle brackets> & ampersand"));
        Assert.That(text, Does.Not.Contain("&lt;"));
        Assert.That(text, Does.Not.Contain("&amp;"));
    }

    #endregion
}
