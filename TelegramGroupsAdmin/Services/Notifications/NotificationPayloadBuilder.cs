using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Fluent builder for constructing immutable NotificationPayload records.
/// Each With* method adds content; conditional logic lives at the call site.
/// </summary>
internal sealed class NotificationPayloadBuilder
{
    private string _subject = "";
    private readonly List<ContentBlock> _blocks = [];
    private string? _photoPath;
    private string? _videoPath;
    private ActionKeyboardContext? _keyboard;

    public static NotificationPayloadBuilder Create(string subject) => new() { _subject = subject };

    public NotificationPayloadBuilder WithText(string text)
    {
        _blocks.Add(new TextBlock(text));
        return this;
    }

    public NotificationPayloadBuilder WithField(string label, string value)
    {
        _blocks.Add(new FieldList([new(label, value)]));
        return this;
    }

    public NotificationPayloadBuilder WithField(string label, UserIdentity user)
    {
        _blocks.Add(new FieldList([new(label, user.DisplayName, user)]));
        return this;
    }

    public NotificationPayloadBuilder WithSection(string header, Action<SectionBuilder> configure)
    {
        var sb = new SectionBuilder();
        configure(sb);
        _blocks.Add(new SectionBlock(header, sb.Build()));
        return this;
    }

    public NotificationPayloadBuilder WithPhoto(string path)
    {
        _photoPath = path;
        return this;
    }

    public NotificationPayloadBuilder WithVideo(string path)
    {
        _videoPath = path;
        return this;
    }

    public NotificationPayloadBuilder WithKeyboard(ActionKeyboardContext ctx)
    {
        _keyboard = ctx;
        return this;
    }

    public NotificationPayload Build() => new()
    {
        Subject = _subject,
        Blocks = _blocks.ToArray(),
        PhotoPath = _photoPath,
        VideoPath = _videoPath,
        Keyboard = _keyboard
    };
}
