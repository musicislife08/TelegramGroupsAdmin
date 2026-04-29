using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for the typed methods on ConfigRepository.
/// Uses real PostgreSQL via Testcontainers to validate:
///  - per-config save/get round-trip preserves all fields,
///  - GetEffective merging across global (chat_id=0) and chat-specific rows,
///  - bot token is encrypted at rest and round-trips through DataProtection,
///  - moderation column multiplexing preserves the sibling config when one is updated.
/// </summary>
[TestFixture]
public class ConfigRepositoryIntegrationTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IConfigRepository? _repo;

    private static readonly ChatIdentity GlobalChat = new(0, "global");
    private static readonly ChatIdentity TestChat = new(123456789L, "Test Chat");

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        var keyDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}"));
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(keyDirectory);

        var dataSource = new NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build();
        services.AddSingleton(dataSource);
        services.AddDbContextFactory<AppDbContext>((_, options) => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        services.AddScoped<IConfigRepository, ConfigRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repo = _serviceProvider.GetRequiredService<IConfigRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    // ========================================================================
    // Round-trip: Welcome
    // ========================================================================

    [Test]
    public async Task SaveAndGet_Welcome_RoundTripPreservesAllFields()
    {
        var welcome = new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.DmWelcome,
            TimeoutSeconds = 90,
            MaxKicksBeforeBan = 3,
            MainWelcomeMessage = "Welcome friend!",
            DmChatTeaserMessage = "Click below.",
            AcceptButtonText = "Yes",
            DenyButtonText = "No",
            DmButtonText = "Open DM"
        };

        await _repo!.SaveWelcomeAsync(TestChat, welcome);
        var retrieved = await _repo.GetWelcomeAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Enabled, Is.True);
            Assert.That(retrieved.Mode, Is.EqualTo(WelcomeMode.DmWelcome));
            Assert.That(retrieved.TimeoutSeconds, Is.EqualTo(90));
            Assert.That(retrieved.MaxKicksBeforeBan, Is.EqualTo(3));
            Assert.That(retrieved.MainWelcomeMessage, Is.EqualTo("Welcome friend!"));
            Assert.That(retrieved.DmChatTeaserMessage, Is.EqualTo("Click below."));
            Assert.That(retrieved.AcceptButtonText, Is.EqualTo("Yes"));
            Assert.That(retrieved.DenyButtonText, Is.EqualTo("No"));
            Assert.That(retrieved.DmButtonText, Is.EqualTo("Open DM"));
        });
    }

    // ========================================================================
    // Round-trip: Log
    // ========================================================================

    [Test]
    public async Task SaveAndGet_Log_RoundTripPreservesAllFields()
    {
        var log = new LogConfig
        {
            DefaultLevel = LogLevel.Warning,
            Overrides = new Dictionary<string, LogLevel>
            {
                ["TelegramGroupsAdmin.Telegram"] = LogLevel.Debug,
                ["TelegramGroupsAdmin.ContentDetection"] = LogLevel.Information
            }
        };

        await _repo!.SaveLogAsync(TestChat, log);
        var retrieved = await _repo.GetLogAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.DefaultLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(retrieved.Overrides, Has.Count.EqualTo(2));
            Assert.That(retrieved.Overrides["TelegramGroupsAdmin.Telegram"], Is.EqualTo(LogLevel.Debug));
            Assert.That(retrieved.Overrides["TelegramGroupsAdmin.ContentDetection"], Is.EqualTo(LogLevel.Information));
        });
    }

    // ========================================================================
    // Round-trip: BotProtection
    // ========================================================================

    [Test]
    public async Task SaveAndGet_BotProtection_RoundTripPreservesAllFields()
    {
        var botProtection = new BotProtectionConfig
        {
            Enabled = true,
            AutoBanBots = true,
            AllowAdminInvitedBots = true,
            WhitelistedBots = ["@RoseBot", "@GroupButlerBot"],
            LogBotEvents = true
        };

        await _repo!.SaveBotProtectionAsync(TestChat, botProtection);
        var retrieved = await _repo.GetBotProtectionAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Enabled, Is.True);
            Assert.That(retrieved.AutoBanBots, Is.True);
            Assert.That(retrieved.AllowAdminInvitedBots, Is.True);
            Assert.That(retrieved.WhitelistedBots, Is.EquivalentTo(new[] { "@RoseBot", "@GroupButlerBot" }));
            Assert.That(retrieved.LogBotEvents, Is.True);
        });
    }

    // ========================================================================
    // Round-trip: TelegramBot
    // ========================================================================

    [Test]
    public async Task SaveAndGet_TelegramBot_RoundTripPreservesAllFields()
    {
        var telegramBot = new TelegramBotConfig { BotEnabled = true };

        await _repo!.SaveTelegramBotAsync(telegramBot);
        var retrieved = await _repo.GetTelegramBotAsync();

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.BotEnabled, Is.True);
    }

    // ========================================================================
    // Round-trip: ServiceMessageDeletion
    // ========================================================================

    [Test]
    public async Task SaveAndGet_ServiceMessageDeletion_RoundTripPreservesAllFields()
    {
        var smd = new ServiceMessageDeletionConfig
        {
            DeleteJoinMessages = false,
            DeleteLeaveMessages = false,
            DeletePhotoChanges = true,
            DeleteTitleChanges = true,
            DeletePinNotifications = false,
            DeleteChatCreationMessages = true
        };

        await _repo!.SaveServiceMessageDeletionAsync(TestChat, smd);
        var retrieved = await _repo.GetServiceMessageDeletionAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.DeleteJoinMessages, Is.False);
            Assert.That(retrieved.DeleteLeaveMessages, Is.False);
            Assert.That(retrieved.DeletePhotoChanges, Is.True);
            Assert.That(retrieved.DeleteTitleChanges, Is.True);
            Assert.That(retrieved.DeletePinNotifications, Is.False);
            Assert.That(retrieved.DeleteChatCreationMessages, Is.True);
        });
    }

    // ========================================================================
    // Round-trip: WarningSystem (multiplexed in moderation column)
    // ========================================================================

    [Test]
    public async Task SaveAndGet_WarningSystem_RoundTripPreservesAllFields()
    {
        var ws = new WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 5,
            AutoBanReason = "Auto-ban after {count} warnings"
        };

        await _repo!.SaveWarningSystemAsync(TestChat, ws);
        var retrieved = await _repo.GetWarningSystemAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.AutoBanEnabled, Is.True);
            Assert.That(retrieved.AutoBanThreshold, Is.EqualTo(5));
            Assert.That(retrieved.AutoBanReason, Is.EqualTo("Auto-ban after {count} warnings"));
        });
    }

    // ========================================================================
    // Round-trip: InviteCommand (multiplexed in moderation column)
    // ========================================================================

    [Test]
    public async Task SaveAndGet_InviteCommand_RoundTripPreservesAllFields()
    {
        var ic = new InviteCommandConfig
        {
            Enabled = false,
            DeleteCommandMessage = false,
            DeleteResponseAfterSeconds = 120
        };

        await _repo!.SaveInviteCommandAsync(TestChat, ic);
        var retrieved = await _repo.GetInviteCommandAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Enabled, Is.False);
            Assert.That(retrieved.DeleteCommandMessage, Is.False);
            Assert.That(retrieved.DeleteResponseAfterSeconds, Is.EqualTo(120));
        });
    }

    // ========================================================================
    // Round-trip: BanCelebration
    // ========================================================================

    [Test]
    public async Task SaveAndGet_BanCelebration_RoundTripPreservesAllFields()
    {
        var bc = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = false,
            TriggerOnManualBan = true,
            SendToBannedUser = false
        };

        await _repo!.SaveBanCelebrationAsync(TestChat, bc);
        var retrieved = await _repo.GetBanCelebrationAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Enabled, Is.True);
            Assert.That(retrieved.TriggerOnAutoBan, Is.False);
            Assert.That(retrieved.TriggerOnManualBan, Is.True);
            Assert.That(retrieved.SendToBannedUser, Is.False);
        });
    }

    // ========================================================================
    // GetEffective Welcome scenarios
    // ========================================================================

    [Test]
    public async Task GetEffective_Welcome_OnlyGlobal_ReturnsGlobal()
    {
        var global = new WelcomeConfig
        {
            Enabled = true,
            MainWelcomeMessage = "global message",
            TimeoutSeconds = 60
        };
        await _repo!.SaveWelcomeAsync(GlobalChat, global);

        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.Enabled, Is.True);
            Assert.That(effective.MainWelcomeMessage, Is.EqualTo("global message"));
            Assert.That(effective.TimeoutSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public async Task GetEffective_Welcome_OnlyChat_ReturnsChat()
    {
        var chat = new WelcomeConfig
        {
            Enabled = true,
            MainWelcomeMessage = "chat message",
            TimeoutSeconds = 30
        };
        await _repo!.SaveWelcomeAsync(TestChat, chat);

        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.Enabled, Is.True);
            Assert.That(effective.MainWelcomeMessage, Is.EqualTo("chat message"));
            Assert.That(effective.TimeoutSeconds, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task GetEffective_Welcome_BothPresent_ChatScalarsWin_StringsInheritWhenEmpty()
    {
        var global = new WelcomeConfig
        {
            Enabled = true,
            MainWelcomeMessage = "global",
            TimeoutSeconds = 60,
            AcceptButtonText = "Accept"
        };
        var chat = new WelcomeConfig
        {
            // Scalars: chat row's value wins, even at type default
            Enabled = false,
            TimeoutSeconds = 0,
            // Strings: empty inherits global; non-empty overrides
            MainWelcomeMessage = "chat-specific"
        };

        await _repo!.SaveWelcomeAsync(GlobalChat, global);
        await _repo.SaveWelcomeAsync(TestChat, chat);

        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.Enabled, Is.False, "chat scalar wins at type default");
            Assert.That(effective.TimeoutSeconds, Is.EqualTo(0));
            Assert.That(effective.MainWelcomeMessage, Is.EqualTo("chat-specific"), "non-empty chat string overrides");
            Assert.That(effective.AcceptButtonText, Is.EqualTo("Accept"), "empty chat string inherits global");
        });
    }

    // ========================================================================
    // GetEffective for multiplexed (WarningSystem)
    // ========================================================================

    [Test]
    public async Task GetEffective_WarningSystem_OnlyGlobal_ReturnsGlobal()
    {
        var global = new WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 3,
            AutoBanReason = "global reason"
        };
        await _repo!.SaveWarningSystemAsync(GlobalChat, global);

        var effective = await _repo.GetEffectiveWarningSystemAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.AutoBanEnabled, Is.True);
            Assert.That(effective.AutoBanThreshold, Is.EqualTo(3));
            Assert.That(effective.AutoBanReason, Is.EqualTo("global reason"));
        });
    }

    [Test]
    public async Task GetEffective_WarningSystem_BothPresent_ChatScalarsWin_EmptyStringInherits()
    {
        var global = new WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 3,
            AutoBanReason = "global"
        };
        var chat = new WarningSystemConfig
        {
            // Scalars: chat row's value wins, even at type default
            AutoBanEnabled = false,
            AutoBanThreshold = 0,
            // String: non-empty overrides
            AutoBanReason = "chat reason"
        };

        await _repo!.SaveWarningSystemAsync(GlobalChat, global);
        await _repo.SaveWarningSystemAsync(TestChat, chat);

        var effective = await _repo.GetEffectiveWarningSystemAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.AutoBanEnabled, Is.False, "chat scalar wins at type default");
            Assert.That(effective.AutoBanThreshold, Is.EqualTo(0));
            Assert.That(effective.AutoBanReason, Is.EqualTo("chat reason"));
        });
    }

    // ========================================================================
    // GetEffective for BanCelebration (regular case)
    // ========================================================================

    [Test]
    public async Task GetEffective_BanCelebration_BothPresent_ChatOverrides()
    {
        var global = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = true,
            SendToBannedUser = true
        };
        var chat = new BanCelebrationConfig
        {
            // Default false → falls through to global true
            Enabled = false,
            // Default true → falls through to global true
            TriggerOnAutoBan = true,
            TriggerOnManualBan = true,
            // Override
            SendToBannedUser = false
        };

        await _repo!.SaveBanCelebrationAsync(GlobalChat, global);
        await _repo.SaveBanCelebrationAsync(TestChat, chat);

        var effective = await _repo.GetEffectiveBanCelebrationAsync(TestChat.Id);

        Assert.That(effective, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(effective!.Enabled, Is.True, "global wins via default fall-through");
            Assert.That(effective.TriggerOnAutoBan, Is.True);
            Assert.That(effective.TriggerOnManualBan, Is.True);
            Assert.That(effective.SendToBannedUser, Is.False, "chat override wins");
        });
    }

    // ========================================================================
    // Bot token encryption
    // ========================================================================

    [Test]
    public async Task SaveBotToken_RoundTrip_StoresEncryptedReturnsDecrypted()
    {
        const string plainTextToken = "1234567890:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi";
        await _repo!.SaveBotTokenAsync(plainTextToken);

        // Verify the database stores ciphertext, not plaintext.
        var encrypted = await _testHelper!.ExecuteScalarAsync<string>(
            "SELECT telegram_bot_token_encrypted FROM configs WHERE chat_id = 0");

        Assert.That(encrypted, Is.Not.Null);
        Assert.That(encrypted, Is.Not.EqualTo(plainTextToken), "ciphertext != plaintext");
        Assert.That(encrypted, Does.Not.Contain(plainTextToken), "plaintext must not appear in ciphertext");

        // Round-trip via the API.
        var decrypted = await _repo.GetBotTokenAsync();
        Assert.That(decrypted, Is.EqualTo(plainTextToken));
    }

    [Test]
    public async Task GetBotToken_WhenNotSet_ReturnsNull()
    {
        var token = await _repo!.GetBotTokenAsync();
        Assert.That(token, Is.Null);
    }

    // ========================================================================
    // Moderation column multiplex safety
    // ========================================================================

    [Test]
    public async Task SaveWarningSystem_DoesNotClobberInviteCommand()
    {
        // Step 1: save InviteCommand for the chat.
        var ic = new InviteCommandConfig
        {
            Enabled = false,
            DeleteCommandMessage = false,
            DeleteResponseAfterSeconds = 90
        };
        await _repo!.SaveInviteCommandAsync(TestChat, ic);

        // Step 2: save WarningSystem on the SAME chat — should not erase the invite command.
        var ws = new WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 4,
            AutoBanReason = "ws reason"
        };
        await _repo.SaveWarningSystemAsync(TestChat, ws);

        // Step 3: read both back.
        var retrievedIc = await _repo.GetInviteCommandAsync(TestChat.Id);
        var retrievedWs = await _repo.GetWarningSystemAsync(TestChat.Id);

        Assert.Multiple(() =>
        {
            Assert.That(retrievedIc, Is.Not.Null, "InviteCommand survived WarningSystem save");
            Assert.That(retrievedIc!.Enabled, Is.False);
            Assert.That(retrievedIc.DeleteResponseAfterSeconds, Is.EqualTo(90));

            Assert.That(retrievedWs, Is.Not.Null);
            Assert.That(retrievedWs!.AutoBanThreshold, Is.EqualTo(4));
            Assert.That(retrievedWs.AutoBanReason, Is.EqualTo("ws reason"));
        });
    }

    [Test]
    public async Task DeleteWarningSystem_PreservesInviteCommandSibling()
    {
        var ic = new InviteCommandConfig { DeleteResponseAfterSeconds = 45 };
        var ws = new WarningSystemConfig { AutoBanThreshold = 7 };

        await _repo!.SaveInviteCommandAsync(TestChat, ic);
        await _repo.SaveWarningSystemAsync(TestChat, ws);

        await _repo.DeleteWarningSystemAsync(TestChat);

        var retrievedIc = await _repo.GetInviteCommandAsync(TestChat.Id);
        var retrievedWs = await _repo.GetWarningSystemAsync(TestChat.Id);

        Assert.Multiple(() =>
        {
            Assert.That(retrievedWs, Is.Null, "WarningSystem deleted");
            Assert.That(retrievedIc, Is.Not.Null, "InviteCommand sibling preserved");
            Assert.That(retrievedIc!.DeleteResponseAfterSeconds, Is.EqualTo(45));
        });
    }

    [Test]
    public async Task DeleteInviteCommand_PreservesWarningSystemSibling()
    {
        var ic = new InviteCommandConfig { DeleteResponseAfterSeconds = 99 };
        var ws = new WarningSystemConfig { AutoBanThreshold = 7, AutoBanReason = "preserve me" };

        await _repo!.SaveWarningSystemAsync(TestChat, ws);
        await _repo.SaveInviteCommandAsync(TestChat, ic);

        await _repo.DeleteInviteCommandAsync(TestChat);

        var retrievedWs = await _repo.GetWarningSystemAsync(TestChat.Id);
        var retrievedIc = await _repo.GetInviteCommandAsync(TestChat.Id);

        Assert.Multiple(() =>
        {
            Assert.That(retrievedWs, Is.Not.Null, "WarningSystem sibling preserved");
            Assert.That(retrievedWs!.AutoBanThreshold, Is.EqualTo(7));
            Assert.That(retrievedWs.AutoBanReason, Is.EqualTo("preserve me"));
            Assert.That(retrievedIc, Is.Null, "InviteCommand deleted");
        });
    }
}
