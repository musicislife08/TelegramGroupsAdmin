using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Identity of a Telegram user as one resolved value: the real name for logs and the UI, and the
/// name verdict that decides what bot-written text shows. Obtain it from IUserIdentityService.
/// Telegram names are per account, so one identity is reused across chats.
/// </summary>
public sealed record UserIdentity(long Id, string? FirstName, string? LastName, string? Username)
{
    public string DisplayName { get; } = TelegramDisplayName.Format(FirstName, LastName, Username, Id);

    /// <summary>
    /// Verdict from the latest profile scan. Defaults to Unscanned, which also covers Quartz
    /// payloads serialized before this member existed.
    /// </summary>
    public NameVerdict Verdict { get; init; } = NameVerdict.Unscanned;

    /// <summary>
    /// Name for text the bot writes to Telegram, given the effective masking setting where the
    /// text is written.
    /// </summary>
    public string BotDisplayName(NameMasking masking) =>
        masking == NameMasking.Off ? DisplayName : Verdict switch
        {
            NameVerdict.Explicit => NameRedaction.Explicit,
            NameVerdict.Promotional => NameRedaction.Spam,
            _ => DisplayName
        };

    /// <summary>
    /// Creates an ID-only identity. Internal fallback used by FromAsync when user isn't in DB.
    /// </summary>
    public static UserIdentity FromId(long id) => new(id, null, null, null);

    /// <summary>Sample identity for settings-page previews. Never sent to Telegram for a real user.</summary>
    public static UserIdentity ForPreview(long id, string? firstName, string? lastName, string? username) =>
        new(id, firstName, lastName, username);

    /// <summary>Identity for tests.</summary>
    public static UserIdentity ForTest(
        long id, string? firstName = null, string? lastName = null, string? username = null,
        NameVerdict verdict = NameVerdict.Unscanned) =>
        new(id, firstName, lastName, username) { Verdict = verdict };
}
