namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Canonical string identifiers for every system-issued <see cref="Actor"/>.
/// Single source of truth — referenced from Actor.cs, audit filters, SQL seeds, and tests.
/// </summary>
public static class SystemActorIds
{
    public const string AutoDetection = "auto_detection";
    public const string BotProtection = "bot_protection";
    public const string FileScanner = "file_scanner";
    public const string AutoTrust = "auto_trust";
    public const string Impersonation = "impersonation";
    public const string AutoBan = "auto_ban";
    public const string Cas = "cas";
    public const string LanguageWarning = "language_warning";
    public const string SystemSeed = "system_seed";
    public const string InitialSeed = "initial_seed";
    public const string WebAdmin = "web_admin";
    public const string ExamFlow = "exam_flow";
    public const string WelcomeFlow = "welcome_flow";
    public const string TempbanExpiry = "tempban_expiry";
    public const string Unknown = "unknown";
    public const string ProfileScan = "profile_scan";
    public const string UsernameBlacklist = "username_blacklist";
    public const string Bootstrap = "bootstrap";
    public const string ProfileDiffDetection = "profile_diff_detection";
    public const string WelcomeBypass = "welcome_bypass";
    public const string BotChatService = "bot_chat_service";

    /// <summary>
    /// Every identifier above, for code that maps a display-name search back to identifiers
    /// (the Audit Log's Issued By filter). Keep in step with the constants; a unit test checks it.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        AutoDetection, BotProtection, FileScanner, AutoTrust, Impersonation, AutoBan, Cas, LanguageWarning,
        SystemSeed, InitialSeed, WebAdmin, ExamFlow, WelcomeFlow, TempbanExpiry, Unknown, ProfileScan,
        UsernameBlacklist, Bootstrap, ProfileDiffDetection, WelcomeBypass, BotChatService,
    ];
}
