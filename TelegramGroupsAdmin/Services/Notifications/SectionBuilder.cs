namespace TelegramGroupsAdmin.Services.Notifications;

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
