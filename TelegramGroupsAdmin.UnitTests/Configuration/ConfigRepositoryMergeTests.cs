using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Per-config merge unit tests for ConfigRepository's internal Merge* methods.
/// Contract under test: when a chat row exists, every non-nullable scalar field on the
/// chat row wins outright — chat-explicit `false`/`0` is an override, not "no opinion."
/// Strings fall back to global only when the chat field is empty (UI uses empty-string
/// to express "inherit"). Nullable refs fall back when null.
/// Internal methods are visible to this assembly via InternalsVisibleTo.
/// </summary>
[TestFixture]
public class ConfigRepositoryMergeTests
{
    // ============================================================================
    // Welcome
    // ============================================================================

    [Test]
    public void MergeWelcome_ChatNull_ReturnsGlobal()
    {
        var global = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "global hi" };
        Assert.That(ConfigRepository.MergeWelcome(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeWelcome_GlobalNull_ReturnsChat()
    {
        var chat = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "chat hi" };
        Assert.That(ConfigRepository.MergeWelcome(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeWelcome_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeWelcome(null, null), Is.Null);
    }

    [Test]
    public void MergeWelcome_ChatRowOverridesScalars_StringsInheritWhenEmpty()
    {
        var global = new WelcomeConfig
        {
            Enabled = true,
            MainWelcomeMessage = "global welcome",
            TimeoutSeconds = 60,
            MaxKicksBeforeBan = 3,
            AcceptButtonText = "Accept",
            DenyButtonText = "Decline"
        };
        var chat = new WelcomeConfig
        {
            // Scalars: chat row's value always wins, even at the type default
            Enabled = false,
            TimeoutSeconds = 0,
            MaxKicksBeforeBan = 0,
            // Strings: empty inherits global; non-empty overrides
            MainWelcomeMessage = "",
            AcceptButtonText = "",
            DenyButtonText = "Nope"
        };
        var merged = ConfigRepository.MergeWelcome(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Enabled, Is.False, "chat scalar wins even at type default");
            Assert.That(merged.TimeoutSeconds, Is.EqualTo(0));
            Assert.That(merged.MaxKicksBeforeBan, Is.EqualTo(0));
            Assert.That(merged.MainWelcomeMessage, Is.EqualTo("global welcome"), "empty string inherits global");
            Assert.That(merged.AcceptButtonText, Is.EqualTo("Accept"));
            Assert.That(merged.DenyButtonText, Is.EqualTo("Nope"), "non-empty chat string overrides");
        });
    }

    [Test]
    public void MergeWelcome_NestedConfigs_ChatStructureWins()
    {
        var globalExam = new ExamConfig { OpenEndedQuestion = "global question" };
        var chatExam = new ExamConfig { OpenEndedQuestion = "chat question" };
        var global = new WelcomeConfig { ExamConfig = globalExam };
        var chat = new WelcomeConfig { ExamConfig = chatExam };
        var merged = ConfigRepository.MergeWelcome(global, chat)!;
        Assert.That(merged.ExamConfig, Is.SameAs(chatExam), "chat ExamConfig wins atomically");
    }

    [Test]
    public void MergeWelcome_NestedTrustedBypass_ChatWholesaleReplacesGlobal()
    {
        // Documents the wholesale-replacement semantics: nested configs (JoinSecurity, TrustedBypass)
        // are NOT field-by-field merged; the chat-level value wins entirely.
        // Matches legacy ConfigService.MergeConfigs<T> reflection-based behavior.
        var global = new WelcomeConfig
        {
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessageAdmin = "global admin msg"
            }
        };
        var chat = new WelcomeConfig
        {
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = false  // chat explicitly wants disabled, even though all other fields default
            }
        };

        var merged = ConfigRepository.MergeWelcome(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.TrustedBypass.Enabled, Is.False, "chat wins entirely (no field merge)");
            Assert.That(merged.TrustedBypass.AnnouncementMessageAdmin,
                Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementMessageAdmin),
                "global's announcement message is silently dropped — wholesale replacement resets to default");
        });
    }

    // ============================================================================
    // Log
    // ============================================================================

    [Test]
    public void MergeLog_ChatNull_ReturnsGlobal()
    {
        var global = new LogConfig { DefaultLevel = LogLevel.Warning };
        Assert.That(ConfigRepository.MergeLog(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeLog_GlobalNull_ReturnsChat()
    {
        var chat = new LogConfig { DefaultLevel = LogLevel.Debug };
        Assert.That(ConfigRepository.MergeLog(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeLog_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeLog(null, null), Is.Null);
    }

    [Test]
    public void MergeLog_ChatRowOverridesDefaultLevel_DictionariesUnion()
    {
        var global = new LogConfig
        {
            DefaultLevel = LogLevel.Warning,
            Overrides = new Dictionary<string, LogLevel> { ["TGA.Foo"] = LogLevel.Information }
        };
        var chat = new LogConfig
        {
            // Even though Information is the type default, an explicit chat row's value wins.
            DefaultLevel = LogLevel.Information,
            Overrides = new Dictionary<string, LogLevel> { ["TGA.Bar"] = LogLevel.Debug }
        };
        var merged = ConfigRepository.MergeLog(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.DefaultLevel, Is.EqualTo(LogLevel.Information), "chat scalar wins at type default");
            Assert.That(merged.Overrides, Has.Count.EqualTo(2), "override dictionaries are unioned");
            Assert.That(merged.Overrides["TGA.Foo"], Is.EqualTo(LogLevel.Information));
            Assert.That(merged.Overrides["TGA.Bar"], Is.EqualTo(LogLevel.Debug));
        });
    }

    [Test]
    public void MergeLog_OverlappingOverrides_ChatWins()
    {
        var global = new LogConfig
        {
            Overrides = new Dictionary<string, LogLevel> { ["TGA.Foo"] = LogLevel.Information }
        };
        var chat = new LogConfig
        {
            Overrides = new Dictionary<string, LogLevel> { ["TGA.Foo"] = LogLevel.Debug }
        };
        var merged = ConfigRepository.MergeLog(global, chat)!;
        Assert.That(merged.Overrides["TGA.Foo"], Is.EqualTo(LogLevel.Debug));
    }

    // ============================================================================
    // BotProtection
    // ============================================================================

    [Test]
    public void MergeBotProtection_ChatNull_ReturnsGlobal()
    {
        var global = new BotProtectionConfig { Enabled = true, AutoBanBots = true };
        Assert.That(ConfigRepository.MergeBotProtection(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeBotProtection_GlobalNull_ReturnsChat()
    {
        var chat = new BotProtectionConfig { Enabled = true };
        Assert.That(ConfigRepository.MergeBotProtection(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeBotProtection_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeBotProtection(null, null), Is.Null);
    }

    [Test]
    public void MergeBotProtection_ChatRowOverridesScalars_EmptyListInheritsGlobal()
    {
        var global = new BotProtectionConfig
        {
            Enabled = true,
            AutoBanBots = true,
            AllowAdminInvitedBots = true,
            WhitelistedBots = ["@RoseBot", "@GroupButlerBot"],
            LogBotEvents = true
        };
        var chat = new BotProtectionConfig
        {
            // Scalars: chat row wins outright, even at type default
            Enabled = false,
            AutoBanBots = false,
            AllowAdminInvitedBots = false,
            // List: empty chat list inherits the global list
            WhitelistedBots = [],
            LogBotEvents = true
        };
        var merged = ConfigRepository.MergeBotProtection(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Enabled, Is.False, "chat scalar wins at type default");
            Assert.That(merged.AutoBanBots, Is.False);
            Assert.That(merged.AllowAdminInvitedBots, Is.False);
            Assert.That(merged.WhitelistedBots, Has.Count.EqualTo(2), "empty chat list inherits global");
            Assert.That(merged.LogBotEvents, Is.True);
        });
    }

    [Test]
    public void MergeBotProtection_ChatWhitelistOverrides()
    {
        var global = new BotProtectionConfig { WhitelistedBots = ["@RoseBot"] };
        var chat = new BotProtectionConfig { WhitelistedBots = ["@LocalBot"] };
        var merged = ConfigRepository.MergeBotProtection(global, chat)!;
        Assert.That(merged.WhitelistedBots, Is.EquivalentTo(new[] { "@LocalBot" }));
    }

    // ============================================================================
    // TelegramBot
    // ============================================================================

    [Test]
    public void MergeTelegramBot_ChatNull_ReturnsGlobal()
    {
        var global = new TelegramBotConfig { BotEnabled = true };
        Assert.That(ConfigRepository.MergeTelegramBot(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeTelegramBot_GlobalNull_ReturnsChat()
    {
        var chat = new TelegramBotConfig { BotEnabled = true };
        Assert.That(ConfigRepository.MergeTelegramBot(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeTelegramBot_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeTelegramBot(null, null), Is.Null);
    }

    [Test]
    public void MergeTelegramBot_ChatRowOverridesScalar_EvenAtTypeDefault()
    {
        var global = new TelegramBotConfig { BotEnabled = true };
        var chat = new TelegramBotConfig { BotEnabled = false }; // explicitly disable
        var merged = ConfigRepository.MergeTelegramBot(global, chat)!;
        Assert.That(merged.BotEnabled, Is.False, "chat scalar wins even at type default");
    }

    // ============================================================================
    // ServiceMessageDeletion
    // ============================================================================

    [Test]
    public void MergeServiceMessageDeletion_ChatNull_ReturnsGlobal()
    {
        var global = new ServiceMessageDeletionConfig { DeleteJoinMessages = false };
        Assert.That(ConfigRepository.MergeServiceMessageDeletion(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeServiceMessageDeletion_GlobalNull_ReturnsChat()
    {
        var chat = new ServiceMessageDeletionConfig { DeleteJoinMessages = false };
        Assert.That(ConfigRepository.MergeServiceMessageDeletion(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeServiceMessageDeletion_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeServiceMessageDeletion(null, null), Is.Null);
    }

    [Test]
    public void MergeServiceMessageDeletion_ChatRowOverridesAllScalars()
    {
        var global = new ServiceMessageDeletionConfig
        {
            DeleteJoinMessages = false,
            DeleteLeaveMessages = false,
            DeletePhotoChanges = true,
            DeleteTitleChanges = true,
            DeletePinNotifications = true,
            DeleteChatCreationMessages = true
        };
        var chat = new ServiceMessageDeletionConfig
        {
            // Each chat scalar wins regardless of whether it equals the type default.
            DeleteJoinMessages = true,
            DeleteLeaveMessages = true,
            DeletePhotoChanges = false,
            DeleteTitleChanges = true,
            DeletePinNotifications = true,
            DeleteChatCreationMessages = true
        };
        var merged = ConfigRepository.MergeServiceMessageDeletion(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.DeleteJoinMessages, Is.True, "chat scalar wins");
            Assert.That(merged.DeleteLeaveMessages, Is.True);
            Assert.That(merged.DeletePhotoChanges, Is.False);
            Assert.That(merged.DeleteTitleChanges, Is.True);
            Assert.That(merged.DeletePinNotifications, Is.True);
            Assert.That(merged.DeleteChatCreationMessages, Is.True);
        });
    }

    // ============================================================================
    // WarningSystem
    // ============================================================================

    [Test]
    public void MergeWarningSystem_ChatNull_ReturnsGlobal()
    {
        var global = new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 };
        Assert.That(ConfigRepository.MergeWarningSystem(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeWarningSystem_GlobalNull_ReturnsChat()
    {
        var chat = new WarningSystemConfig { AutoBanEnabled = true };
        Assert.That(ConfigRepository.MergeWarningSystem(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeWarningSystem_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeWarningSystem(null, null), Is.Null);
    }

    [Test]
    public void MergeWarningSystem_ChatRowOverridesScalars_EmptyReasonInherits()
    {
        var global = new WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 3,
            AutoBanReason = "global reason"
        };
        var chat = new WarningSystemConfig
        {
            // Scalars: chat wins even at type default
            AutoBanEnabled = false,
            AutoBanThreshold = 0,
            // String: empty inherits global
            AutoBanReason = ""
        };
        var merged = ConfigRepository.MergeWarningSystem(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.AutoBanEnabled, Is.False, "chat scalar wins at type default");
            Assert.That(merged.AutoBanThreshold, Is.EqualTo(0));
            Assert.That(merged.AutoBanReason, Is.EqualTo("global reason"), "empty string inherits global");
        });
    }

    [Test]
    public void MergeWarningSystem_ChatThresholdOverrides()
    {
        var global = new WarningSystemConfig { AutoBanThreshold = 3 };
        var chat = new WarningSystemConfig { AutoBanThreshold = 5 };
        var merged = ConfigRepository.MergeWarningSystem(global, chat)!;
        Assert.That(merged.AutoBanThreshold, Is.EqualTo(5));
    }

    // ============================================================================
    // InviteCommand
    // ============================================================================

    [Test]
    public void MergeInviteCommand_ChatNull_ReturnsGlobal()
    {
        var global = new InviteCommandConfig { Enabled = false, DeleteResponseAfterSeconds = 60 };
        Assert.That(ConfigRepository.MergeInviteCommand(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeInviteCommand_GlobalNull_ReturnsChat()
    {
        var chat = new InviteCommandConfig { Enabled = false };
        Assert.That(ConfigRepository.MergeInviteCommand(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeInviteCommand_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeInviteCommand(null, null), Is.Null);
    }

    [Test]
    public void MergeInviteCommand_ChatRowOverridesAllScalars()
    {
        var global = new InviteCommandConfig
        {
            Enabled = false,
            DeleteCommandMessage = false,
            DeleteResponseAfterSeconds = 60
        };
        var chat = new InviteCommandConfig
        {
            // Even at type defaults, chat row wins outright.
            Enabled = true,
            DeleteCommandMessage = true,
            DeleteResponseAfterSeconds = 30
        };
        var merged = ConfigRepository.MergeInviteCommand(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Enabled, Is.True, "chat scalar wins at type default");
            Assert.That(merged.DeleteCommandMessage, Is.True);
            Assert.That(merged.DeleteResponseAfterSeconds, Is.EqualTo(30));
        });
    }

    // ============================================================================
    // BanCelebration
    // ============================================================================

    [Test]
    public void MergeBanCelebration_ChatNull_ReturnsGlobal()
    {
        var global = new BanCelebrationConfig { Enabled = true };
        Assert.That(ConfigRepository.MergeBanCelebration(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeBanCelebration_GlobalNull_ReturnsChat()
    {
        var chat = new BanCelebrationConfig { Enabled = true };
        Assert.That(ConfigRepository.MergeBanCelebration(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeBanCelebration_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeBanCelebration(null, null), Is.Null);
    }

    [Test]
    public void MergeBanCelebration_ChatRowOverridesAllScalars()
    {
        var global = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = false,
            TriggerOnManualBan = false,
            SendToBannedUser = false
        };
        var chat = new BanCelebrationConfig
        {
            // Each chat scalar wins outright — including type defaults.
            Enabled = false,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = true,
            SendToBannedUser = false
        };
        var merged = ConfigRepository.MergeBanCelebration(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Enabled, Is.False, "chat scalar wins at type default");
            Assert.That(merged.TriggerOnAutoBan, Is.True);
            Assert.That(merged.TriggerOnManualBan, Is.True);
            Assert.That(merged.SendToBannedUser, Is.False);
        });
    }
}
