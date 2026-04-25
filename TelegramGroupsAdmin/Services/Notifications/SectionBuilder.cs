using TelegramGroupsAdmin.Core.Models;

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

    public SectionBuilder WithField(string label, string value)
    {
        _blocks.Add(new FieldList([new(label, value)]));
        return this;
    }

    public SectionBuilder WithField(string label, UserIdentity user)
    {
        _blocks.Add(new FieldList([new(label, user.DisplayName, user)]));
        return this;
    }

    internal IReadOnlyList<ContentBlock> Build() => _blocks.ToArray();
}
