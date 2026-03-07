namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Fluent builder for constructing immutable NotificationPayload records.
/// Call sites use WithText/WithField/WithSection/WithFieldIf for clean, readable payload construction.
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

    public NotificationPayloadBuilder WithField(string label, string value, long? telegramUserId = null)
    {
        _blocks.Add(new FieldList([new(label, value, telegramUserId)]));
        return this;
    }

    public NotificationPayloadBuilder WithFieldIf(bool condition, string label, string? value, long? telegramUserId = null)
    {
        if (condition && value != null) WithField(label, value, telegramUserId);
        return this;
    }

    public NotificationPayloadBuilder WithSection(string header, Action<SectionBuilder> configure)
    {
        var sb = new SectionBuilder();
        configure(sb);
        _blocks.Add(new SectionBlock(header, sb.Build()));
        return this;
    }

    public NotificationPayloadBuilder WithPhoto(string? path)
    {
        _photoPath = path;
        return this;
    }

    public NotificationPayloadBuilder WithVideo(string? path)
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

/// <summary>
/// Builder for content blocks within a section.
/// </summary>
internal sealed class SectionBuilder
{
    private readonly List<ContentBlock> _blocks = [];

    public SectionBuilder WithText(string text)
    {
        _blocks.Add(new TextBlock(text));
        return this;
    }

    public SectionBuilder WithField(string label, string value, long? telegramUserId = null)
    {
        _blocks.Add(new FieldList([new(label, value, telegramUserId)]));
        return this;
    }

    public SectionBuilder WithFieldIf(bool condition, string label, string? value, long? telegramUserId = null)
    {
        if (condition && value != null) WithField(label, value, telegramUserId);
        return this;
    }

    internal IReadOnlyList<ContentBlock> Build() => _blocks.ToArray();
}
