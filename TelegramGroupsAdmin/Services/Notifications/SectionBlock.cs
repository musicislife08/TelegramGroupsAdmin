namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Named section containing nested content blocks.
/// Renders as a bold header followed by indented content.
/// </summary>
internal sealed record SectionBlock(string Header, IReadOnlyList<ContentBlock> Content) : ContentBlock;
