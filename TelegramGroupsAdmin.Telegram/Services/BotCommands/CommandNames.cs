namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Static constants for bot command names used in keyed service registration
/// </summary>
public static class CommandNames
{
    public const string Start = "start";
    public const string Help = "help";
    public const string Link = "link";
    public const string Spam = "spam";
    public const string Ban = "ban";
    public const string Trust = "trust";
    public const string Unban = "unban";
    public const string Warn = "warn";
    public const string TempBan = "tempban";
    public const string Mute = "mute";
    public const string Report = "report";
    public const string Invite = "invite";
    public const string Delete = "delete";
    public const string MyStatus = "mystatus";

    /// <summary>
    /// All command names for validation and iteration
    /// </summary>
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Start, Help, Link, Spam, Ban, Trust, Unban,
        Warn, TempBan, Mute, Report, Invite, Delete, MyStatus
    };
}
