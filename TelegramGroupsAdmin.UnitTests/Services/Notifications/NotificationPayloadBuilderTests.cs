using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class NotificationPayloadBuilderTests
{
    [Test]
    public void Create_SetsSubject()
    {
        var payload = NotificationPayloadBuilder.Create("Test Subject").Build();

        Assert.That(payload.Subject, Is.EqualTo("Test Subject"));
    }

    [Test]
    public void Build_WithNoBlocks_ReturnsEmptyBlockList()
    {
        var payload = NotificationPayloadBuilder.Create("Empty").Build();

        Assert.That(payload.Blocks, Is.Empty);
    }

    [Test]
    public void WithText_AddsTextBlock()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithText("Hello world")
            .Build();

        Assert.That(payload.Blocks, Has.Count.EqualTo(1));
        Assert.That(payload.Blocks[0], Is.InstanceOf<TextBlock>());
        Assert.That(((TextBlock)payload.Blocks[0]).Text, Is.EqualTo("Hello world"));
    }

    [Test]
    public void WithField_AddsFieldListWithSingleField()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithField("Name", "Alice")
            .Build();

        Assert.That(payload.Blocks, Has.Count.EqualTo(1));
        Assert.That(payload.Blocks[0], Is.InstanceOf<FieldList>());

        var fieldList = (FieldList)payload.Blocks[0];
        Assert.That(fieldList.Fields, Has.Count.EqualTo(1));
        Assert.That(fieldList.Fields[0].Label, Is.EqualTo("Name"));
        Assert.That(fieldList.Fields[0].Value, Is.EqualTo("Alice"));
        Assert.That(fieldList.Fields[0].TelegramUserId, Is.Null);
    }

    [Test]
    public void WithField_TelegramUserId_SetsOnField()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithField("User", "SpammerBob", telegramUserId: 12345)
            .Build();

        var field = ((FieldList)payload.Blocks[0]).Fields[0];
        Assert.That(field.TelegramUserId, Is.EqualTo(12345));
    }

    [Test]
    public void WithFieldIf_ConditionTrue_AddsField()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithFieldIf(true, "Reason", "Spam detected")
            .Build();

        Assert.That(payload.Blocks, Has.Count.EqualTo(1));
        var field = ((FieldList)payload.Blocks[0]).Fields[0];
        Assert.That(field.Label, Is.EqualTo("Reason"));
        Assert.That(field.Value, Is.EqualTo("Spam detected"));
    }

    [Test]
    public void WithFieldIf_ConditionFalse_DoesNotAddBlock()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithFieldIf(false, "Reason", "Spam detected")
            .Build();

        Assert.That(payload.Blocks, Is.Empty);
    }

    [Test]
    public void WithFieldIf_ConditionTrue_NullValue_DoesNotAddBlock()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithFieldIf(true, "Reason", null)
            .Build();

        Assert.That(payload.Blocks, Is.Empty);
    }

    [Test]
    public void WithSection_AddsNestedContent()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithSection("Detection", s => s
                .WithField("Confidence", "95%")
                .WithText("Auto-detected by ML model"))
            .Build();

        Assert.That(payload.Blocks, Has.Count.EqualTo(1));
        Assert.That(payload.Blocks[0], Is.InstanceOf<SectionBlock>());

        var section = (SectionBlock)payload.Blocks[0];
        Assert.That(section.Header, Is.EqualTo("Detection"));
        Assert.That(section.Content, Has.Count.EqualTo(2));
        Assert.That(section.Content[0], Is.InstanceOf<FieldList>());
        Assert.That(section.Content[1], Is.InstanceOf<TextBlock>());
    }

    [Test]
    public void WithSection_WithFieldIf_ConditionalsWorkInsideSection()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithSection("Details", s => s
                .WithFieldIf(true, "Visible", "yes")
                .WithFieldIf(false, "Hidden", "no"))
            .Build();

        var section = (SectionBlock)payload.Blocks[0];
        Assert.That(section.Content, Has.Count.EqualTo(1));
        Assert.That(((FieldList)section.Content[0]).Fields[0].Label, Is.EqualTo("Visible"));
    }

    [Test]
    public void WithPhoto_SetsPhotoPath()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithPhoto("/data/media/photo.jpg")
            .Build();

        Assert.That(payload.PhotoPath, Is.EqualTo("/data/media/photo.jpg"));
    }

    [Test]
    public void WithVideo_SetsVideoPath()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithVideo("/data/media/video.mp4")
            .Build();

        Assert.That(payload.VideoPath, Is.EqualTo("/data/media/video.mp4"));
    }

    [Test]
    public void WithPhoto_NullPath_SetsNull()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithPhoto(null)
            .Build();

        Assert.That(payload.PhotoPath, Is.Null);
    }

    [Test]
    public void WithKeyboard_SetsKeyboardContext()
    {
        var keyboard = new ActionKeyboardContext(EntityId: 42, ChatId: 100, UserId: 200, KeyboardType: ReportType.ContentReport);

        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithKeyboard(keyboard)
            .Build();

        Assert.That(payload.Keyboard, Is.Not.Null);
        Assert.That(payload.Keyboard!.EntityId, Is.EqualTo(42));
        Assert.That(payload.Keyboard.KeyboardType, Is.EqualTo(ReportType.ContentReport));
    }

    [Test]
    public void Build_ComplexPayload_PreservesBlockOrder()
    {
        var payload = NotificationPayloadBuilder.Create("Spam Banned")
            .WithField("User", "Alice", telegramUserId: 111)
            .WithText("Banned from all managed chats")
            .WithSection("Detection", s => s
                .WithField("Confidence", "95%"))
            .WithField("Chats affected", "5")
            .WithPhoto("/data/media/spam.jpg")
            .Build();

        Assert.That(payload.Blocks, Has.Count.EqualTo(4));
        Assert.That(payload.Blocks[0], Is.InstanceOf<FieldList>());
        Assert.That(payload.Blocks[1], Is.InstanceOf<TextBlock>());
        Assert.That(payload.Blocks[2], Is.InstanceOf<SectionBlock>());
        Assert.That(payload.Blocks[3], Is.InstanceOf<FieldList>());
        Assert.That(payload.PhotoPath, Is.EqualTo("/data/media/spam.jpg"));
    }

    [Test]
    public void Build_ProducesImmutablePayload()
    {
        var payload = NotificationPayloadBuilder.Create("Subject")
            .WithText("Block 1")
            .Build();

        // Blocks is IReadOnlyList — cannot be modified after build
        Assert.That(payload.Blocks, Is.InstanceOf<IReadOnlyList<ContentBlock>>());
    }
}
