namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Fixed text shown in bot-written messages instead of a flagged name.
/// </summary>
public static class NameRedaction
{
    public const string Explicit = "[name removed: explicit]";
    public const string Spam = "[name removed: spam]";
}
