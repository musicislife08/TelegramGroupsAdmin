using TelegramGroupsAdmin.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class NotificationRendererTests
{
    #region ToTelegramHtml Tests

    [Test]
    public void ToTelegramHtml_SubjectOnly_RendersBoldSubject()
    {
        var payload = NotificationPayloadBuilder.Create("Test Alert").Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.StartWith("<b>Test Alert</b>"));
    }

    [Test]
    public void ToTelegramHtml_TextBlock_RendersEscapedText()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithText("User <script> injected & broke things")
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("User &lt;script&gt; injected &amp; broke things"));
    }

    [Test]
    public void ToTelegramHtml_Field_RendersBoldLabel()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Confidence", "95%")
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("<b>Confidence:</b> 95%"));
    }

    [Test]
    public void ToTelegramHtml_FieldWithTelegramUserId_RendersTgUserLink()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", "SpammerBob", telegramUserId: 12345)
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("<a href=\"tg://user?id=12345\">SpammerBob</a>"));
        Assert.That(html, Does.Contain("<b>User:</b>"));
    }

    [Test]
    public void ToTelegramHtml_FieldWithoutTelegramUserId_NoLink()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Chat", "Test Group")
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Not.Contain("tg://user"));
        Assert.That(html, Does.Contain("<b>Chat:</b> Test Group"));
    }

    [Test]
    public void ToTelegramHtml_Section_RendersBoldHeaderAndContent()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithSection("Detection", s => s
                .WithField("Method", "OpenAI")
                .WithText("High confidence detection"))
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("<b>Detection</b>"));
        Assert.That(html, Does.Contain("<b>Method:</b> OpenAI"));
        Assert.That(html, Does.Contain("High confidence detection"));
    }

    [Test]
    public void ToTelegramHtml_HtmlSpecialCharsInSubject_AreEscaped()
    {
        var payload = NotificationPayloadBuilder.Create("Alert: <User> & \"Details\"").Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("Alert: &lt;User&gt; &amp; &quot;Details&quot;"));
        Assert.That(html, Does.Not.Contain("<User>"));
    }

    [Test]
    public void ToTelegramHtml_FieldValueWithSpecialChars_EscapesCorrectly()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("Reason", "Contains <script>alert('xss')</script>")
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
    }

    [Test]
    public void ToTelegramHtml_TgUserLink_EscapesDisplayName()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", "<Evil> & Name", telegramUserId: 999)
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        Assert.That(html, Does.Contain("<a href=\"tg://user?id=999\">&lt;Evil&gt; &amp; Name</a>"));
    }

    [Test]
    public void ToTelegramHtml_ComplexPayload_OrderPreserved()
    {
        var payload = NotificationPayloadBuilder.Create("Spam Banned")
            .WithField("User", "Alice", telegramUserId: 111)
            .WithField("Chat", "Test Group")
            .WithSection("Detection", s => s
                .WithField("Confidence", "95%")
                .WithField("Reason", "Known spam pattern"))
            .WithSection("Action", s => s
                .WithText("Banned from 3 chats"))
            .Build();

        var html = NotificationRenderer.ToTelegramHtml(payload);

        // Verify structural ordering
        var userIdx = html.IndexOf("Alice", StringComparison.Ordinal);
        var chatIdx = html.IndexOf("Test Group", StringComparison.Ordinal);
        var detectionIdx = html.IndexOf("Detection", StringComparison.Ordinal);
        var actionIdx = html.IndexOf("Action", StringComparison.Ordinal);

        Assert.That(userIdx, Is.LessThan(chatIdx));
        Assert.That(chatIdx, Is.LessThan(detectionIdx));
        Assert.That(detectionIdx, Is.LessThan(actionIdx));
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
    public void ToEmailHtml_FieldWithTelegramUserId_NoTgLink()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", "Alice", telegramUserId: 12345)
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
    public void ToPlainText_FieldWithTelegramUserId_NoLink()
    {
        var payload = NotificationPayloadBuilder.Create("Alert")
            .WithField("User", "Alice", telegramUserId: 12345)
            .Build();

        var text = NotificationRenderer.ToPlainText(payload);

        Assert.That(text, Does.Not.Contain("tg://user"));
        Assert.That(text, Does.Contain("User: Alice"));
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

    #region EscapeHtml Tests

    [Test]
    public void EscapeHtml_Null_ReturnsEmpty()
    {
        Assert.That(NotificationRenderer.EscapeHtml(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void EscapeHtml_EmptyString_ReturnsEmpty()
    {
        Assert.That(NotificationRenderer.EscapeHtml(""), Is.EqualTo(string.Empty));
    }

    [Test]
    public void EscapeHtml_NoSpecialChars_ReturnsUnchanged()
    {
        Assert.That(NotificationRenderer.EscapeHtml("Hello World"), Is.EqualTo("Hello World"));
    }

    [Test]
    public void EscapeHtml_Ampersand_EscapedFirst()
    {
        // & must be escaped before < and > to avoid double-escaping
        Assert.That(NotificationRenderer.EscapeHtml("A & B"), Is.EqualTo("A &amp; B"));
    }

    [Test]
    public void EscapeHtml_AngleBrackets_Escaped()
    {
        Assert.That(NotificationRenderer.EscapeHtml("<script>"), Is.EqualTo("&lt;script&gt;"));
    }

    [Test]
    public void EscapeHtml_AllSpecialChars_EscapedCorrectly()
    {
        Assert.That(
            NotificationRenderer.EscapeHtml("A & B < C > D \"E\""),
            Is.EqualTo("A &amp; B &lt; C &gt; D &quot;E&quot;"));
    }

    [Test]
    public void EscapeHtml_NoDoubleEscaping()
    {
        // If input already contains &amp;, it should be escaped to &amp;amp; (not left as-is)
        Assert.That(
            NotificationRenderer.EscapeHtml("&amp;"),
            Is.EqualTo("&amp;amp;"));
    }

    #endregion
}
