using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Golden dataset extracted from production database with PII redacted.
/// Contains real message content and spam patterns to ensure realistic testing.
/// </summary>
public static class GoldenDataset
{
    // Tables with DTOs that BackupService can export (excludes: __EFMigrationsHistory, file_scan_quota, file_scan_results, ticker.*)
    public const int TotalTableCount = 38; // Updated 2025-12-13: +training_labels (ML.NET SDCA classifier training labels)

    /// <summary>
    /// Web application users (ASP.NET Identity)
    /// </summary>
    public static class Users
    {
        public const string User1_Id = "b388ee38-0ed3-4c09-9def-5715f9f07f56";
        public const string User1_Email = "owner@example.com";
        public const int User1_PermissionLevel = 2; // Owner
        public const int User1_Status = 1; // Active
        public const bool User1_EmailVerified = true;
        public const bool User1_TotpEnabled = true;

        public const string User2_Id = "921637d5-0f65-4c66-b143-6f057dd06a1c";
        public const string User2_Email = "admin@example.com";
        public const int User2_PermissionLevel = 0; // Admin
        public const int User2_Status = 1; // Active
        public const string User2_InvitedBy = User1_Id; // FK to User1
        public const bool User2_EmailVerified = true;
        public const bool User2_TotpEnabled = true;

        public const string User3_Id = "a8dc8371-afc5-4b61-9d71-d177f2dd9ddd";
        public const string User3_Email = "deleted@example.com";
        public const int User3_PermissionLevel = 0; // Admin
        public const int User3_Status = 3; // Deleted
        public const string User3_InvitedBy = User1_Id;
        public const bool User3_EmailVerified = false;
        public const bool User3_TotpEnabled = false;

        public const string User4_Id = "ba9ba542-3df6-4473-a820-578562780c57";
        public const string User4_Email = "globaladmin@example.com";
        public const int User4_PermissionLevel = 1; // GlobalAdmin
        public const int User4_Status = 3; // Deleted
        public const string User4_InvitedBy = User2_Id; // FK to User2
        public const bool User4_EmailVerified = false;
        public const bool User4_TotpEnabled = false;
    }

    /// <summary>
    /// Telegram users (from groups)
    /// </summary>
    public static class TelegramUsers
    {
        // System user (special ID 0)
        public const long System_TelegramUserId = 0;
        public const string System_Username = "system";
        public const bool System_IsTrusted = false;
        public const bool System_BotDmEnabled = false;

        // Real users (IDs randomized)
        public const long User1_TelegramUserId = 100001;
        public const string User1_Username = "alice_user";
        public const string User1_FirstName = "Alice";
        public const string User1_LastName = "Anderson";
        public const bool User1_IsTrusted = false;
        public const bool User1_BotDmEnabled = true;

        public const long User2_TelegramUserId = 100002;
        public const string User2_Username = "bob_chat";
        public const string User2_FirstName = "Bob";
        public const string User2_LastName = "Brown";
        public const bool User2_IsTrusted = false;
        public const bool User2_BotDmEnabled = false;

        public const long User3_TelegramUserId = 100003;
        public const string User3_Username = "charlie_msg";
        public const string User3_FirstName = "Charlie";
        public const string User3_LastName = "Clark";
        public const bool User3_IsTrusted = true;
        public const bool User3_BotDmEnabled = false;

        public const long User4_TelegramUserId = 100004;
        public const string User4_Username = "diana_test";
        public const string User4_FirstName = "Diana";
        public const string User4_LastName = "Davis";
        public const bool User4_IsTrusted = false;
        public const bool User4_BotDmEnabled = true;

        public const long User5_TelegramUserId = 100005;
        public const string User5_Username = "eve_trusted";
        public const string? User5_FirstName = null;
        public const bool User5_IsTrusted = true;
        public const bool User5_BotDmEnabled = false;

        public const long User6_TelegramUserId = 100006;
        public const string? User6_Username = null;
        public const string User6_FirstName = "Frank";
        public const bool User6_IsTrusted = true;
        public const bool User6_BotDmEnabled = false;

        public const long User7_TelegramUserId = 100007;
        public const string User7_Username = "grace_j";
        public const string? User7_FirstName = null;
        public const bool User7_IsTrusted = true;
        public const bool User7_BotDmEnabled = false;
    }

    /// <summary>
    /// Managed chats (groups monitored by bot)
    /// </summary>
    public static class ManagedChats
    {
        public const long Chat1_Id = -1001766988150;
        public const string Chat1_Name = "Test Group Alpha";
        public const int Chat1_Type = 2; // Group
        public const int Chat1_BotStatus = 1;
        public const bool Chat1_IsAdmin = true;
        public const bool Chat1_IsActive = true;

        public const long Chat2_Id = -1003193605358;
        public const string Chat2_Name = "Bot Testing Beta";
        public const int Chat2_Type = 2; // Group
        public const int Chat2_BotStatus = 1;
        public const bool Chat2_IsAdmin = true;
        public const bool Chat2_IsActive = true;

        public const long Chat3_Id = -1001241664237;
        public const string Chat3_Name = "Community Gamma";
        public const int Chat3_Type = 2; // Group
        public const int Chat3_BotStatus = 1;
        public const bool Chat3_IsAdmin = true;
        public const bool Chat3_IsActive = true;

        // Chat for most test messages
        public const long MainChat_Id = -1001322973935;
        public const string MainChat_Name = "Main Test Group";
        public const int MainChat_Type = 2;
        public const int MainChat_BotStatus = 1;
        public const bool MainChat_IsAdmin = true;
        public const bool MainChat_IsActive = true;
    }

    /// <summary>
    /// Real message content from production (PII redacted)
    /// </summary>
    public static class Messages
    {
        // Message IDs and relationships preserved
        public const long Msg1_Id = 82619;
        public const long Msg1_UserId = TelegramUsers.User2_TelegramUserId;
        public const long Msg1_ChatId = ManagedChats.MainChat_Id;
        public const string Msg1_Text = "Fair enough";
        public const int Msg1_ContentCheckSkipReason = 2;

        public const long Msg2_Id = 82618;
        public const long Msg2_UserId = 1232994248; // Additional user
        public const long Msg2_ChatId = ManagedChats.MainChat_Id;
        public const string Msg2_Text = "He was old and crusty 20yrs ago 😂.  I'm guessing he's had enough.";
        public const int Msg2_ContentCheckSkipReason = 2;

        public const long Msg3_Id = 82617;
        public const long Msg3_UserId = TelegramUsers.User2_TelegramUserId;
        public const long Msg3_ChatId = ManagedChats.MainChat_Id;
        public const string Msg3_Text = "Get while the getting's good?";
        public const int Msg3_ContentCheckSkipReason = 2;

        public const long Msg4_Id = 82616;
        public const long Msg4_UserId = TelegramUsers.User2_TelegramUserId;
        public const long Msg4_ChatId = ManagedChats.MainChat_Id;
        public const string Msg4_Text = "Sounds like he may wanna do it again at some point. I know looking at the state of things I might want to be in it while watching whatever is gonna go down in the economy";
        public const int Msg4_ContentCheckSkipReason = 2;

        public const long Msg5_Id = 82615;
        public const long Msg5_UserId = 1232994248;
        public const long Msg5_ChatId = ManagedChats.MainChat_Id;
        public const string Msg5_Text = "Sad to see him ride out, he definitely knows how to run an org.";
        public const int Msg5_ContentCheckSkipReason = 2;

        // Message with media
        public const long Msg6_Id = 82612;
        public const long Msg6_UserId = 1232994248;
        public const long Msg6_ChatId = ManagedChats.MainChat_Id;
        public const string? Msg6_Text = null; // Media only
        public const int Msg6_MediaType = 1; // Photo
        public const int Msg6_ContentCheckSkipReason = 2;

        // Longer message
        public const long Msg7_Id = 82603;
        public const long Msg7_UserId = TelegramUsers.User2_TelegramUserId;
        public const long Msg7_ChatId = ManagedChats.MainChat_Id;
        public const string Msg7_Text = "OK, I feel as though I can now say this from a position of competency, trial and error success.\n\nJust pay the feckin $20.\n\nEven with a used car value worth of GPUs, there is not a single sumbitchin local LLM to approach the effectiveness of even the $20 Claude models with Code CLI.\n\nI feel like I have a very expensive chat bot that feeds me a chain of errors to correct in my shed.";
        public const int Msg7_ContentCheckSkipReason = 2;

        // Professional conversation
        public const long Msg8_Id = 82606;
        public const long Msg8_UserId = 934156131;
        public const long Msg8_ChatId = ManagedChats.MainChat_Id;
        public const string Msg8_Text = "I've been active with the American Institute of Architects large firm Roundtable for years. We have an email list. I emailed every single General Counsel at the top 20 architectural firms, and that's how I got this job and why I'm talking to the other firm too.";
        public const int Msg8_ContentCheckSkipReason = 1;

        // Healthcare tech discussion
        public const long Msg9_Id = 82596;
        public const long Msg9_UserId = 468009795;
        public const long Msg9_ChatId = ManagedChats.MainChat_Id;
        public const string Msg9_Text = "I have close to 8 years of experience in Healthcare tech. So I have that momentum.";
        public const int Msg9_ContentCheckSkipReason = 2;

        // Software engineering context
        public const long Msg10_Id = 82594;
        public const long Msg10_UserId = 468009795;
        public const long Msg10_ChatId = ManagedChats.MainChat_Id;
        public const string Msg10_Text = "I'm a software engineering manager.  Like I'm the boss of people who write the code.";
        public const int Msg10_ContentCheckSkipReason = 2;

        // Message for Result2
        public const long Msg11_Id = 82581;
        public const long Msg11_UserId = TelegramUsers.User1_TelegramUserId;
        public const long Msg11_ChatId = ManagedChats.MainChat_Id;
        public const string Msg11_Text = "I hit 30 last summer.";
        public const int Msg11_ContentCheckSkipReason = 0; // Actually checked
    }

    /// <summary>
    /// Detection results (spam/ham classifications)
    /// </summary>
    public static class DetectionResults
    {
        public const long Result1_MessageId = Messages.Msg1_Id;
        public const string Result1_DetectionMethod = "InvisibleChars, StopWords, CAS, Similarity, Bayes, Spacing";
        public const int Result1_Confidence = 0;
        public const bool Result1_IsSpam = false;
        public const string Result1_Reason = "No spam detected";
        public const int Result1_NetConfidence = 0;

        public const long Result2_MessageId = 82581; // "I hit 30 last summer"
        public const string Result2_DetectionMethod = "InvisibleChars, StopWords, CAS, Similarity, Bayes, Spacing";
        public const int Result2_Confidence = 37;
        public const bool Result2_IsSpam = false;
        public const string Result2_Reason = "Spam probability: 0.752 (key words: hit) (certainty: 0.504)";
        public const int Result2_NetConfidence = 37;
    }

    /// <summary>
    /// Training labels (explicit ML training data for spam classifier)
    /// </summary>
    public static class TrainingLabels
    {
        // Spam label 1: Msg1 marked as spam by admin
        public const long Label1_MessageId = Messages.Msg1_Id;
        public const short Label1_Label = 0; // Spam
        public const long Label1_LabeledByUserId = TelegramUsers.User1_TelegramUserId;
        public const string Label1_Reason = "Manual spam marking via /report command";

        // Spam label 2: Msg2 marked as spam (no user attribution)
        public const long Label2_MessageId = Messages.Msg2_Id;
        public const short Label2_Label = 0; // Spam
        public static readonly long? Label2_LabeledByUserId = null;
        public const string Label2_Reason = "Confirmed spam pattern";

        // Ham label 1: Msg3 corrected to ham
        public const long Label3_MessageId = Messages.Msg3_Id;
        public const short Label3_Label = 1; // Ham
        public const long Label3_LabeledByUserId = TelegramUsers.User2_TelegramUserId;
        public const string Label3_Reason = "Admin correction - false positive";

        // Ham label 2: Msg4 marked as ham (no reason)
        public const long Label4_MessageId = Messages.Msg4_Id;
        public const short Label4_Label = 1; // Ham
        public static readonly long? Label4_LabeledByUserId = null;
        public static readonly string? Label4_Reason = null;

        // Spam label 3: Msg5 marked as spam (no reason)
        public const long Label5_MessageId = Messages.Msg5_Id;
        public const short Label5_Label = 0; // Spam
        public static readonly long? Label5_LabeledByUserId = null;
        public static readonly string? Label5_Reason = null;
    }

    /// <summary>
    /// Linked channels (channels linked to managed chat groups for impersonation detection)
    /// </summary>
    public static class LinkedChannels
    {
        // Channel linked to MainChat (1:1 relationship per Telegram API)
        public const long Channel1_ManagedChatId = ManagedChats.MainChat_Id;
        public const long Channel1_ChannelId = -1001555777999;
        public const string Channel1_Name = "Main Test Channel";
        public const string? Channel1_IconPath = null; // No icon downloaded
        // Photo hash: 8 bytes representing a pHash for impersonation detection
        public static readonly byte[] Channel1_PhotoHash = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0];

        // Channel linked to Chat1 (no photo hash - tests null handling)
        public const long Channel2_ManagedChatId = ManagedChats.Chat1_Id;
        public const long Channel2_ChannelId = -1001444666888;
        public const string Channel2_Name = "Alpha Channel";
        public const string? Channel2_IconPath = "channels/alpha_icon.jpg";
        public static readonly byte[]? Channel2_PhotoHash = null;
    }

    /// <summary>
    /// Test API keys (plaintext, will be encrypted during seed)
    /// </summary>
    public static class ApiKeys
    {
        public const string VirusTotal_Test = "vt_test_key_1a2b3c4d5e6f";
    }

    /// <summary>
    /// Config data (JSONB and encrypted fields)
    /// </summary>
    public static class Configs
    {
        public const long Config1_Id = 1;
        public const long Config1_ChatId = 0; // Global config (SCHEMA-3: chat_id = 0 for global)

        // Spam detection config (real structure, simplified)
        public const string SpamDetectionConfigJson = """
        {
          "cas": {"apiUrl": "https://api.cas.chat", "enabled": true, "timeout": "00:00:05"},
          "bayes": {"enabled": true, "minSpamProbability": 50},
          "openAI": {"enabled": true, "vetoMode": true, "vetoThreshold": 95, "checkShortMessages": false},
          "spacing": {"enabled": true, "minWordsCount": 5, "spaceRatioThreshold": 0.3},
          "stopWords": {"enabled": true, "confidenceThreshold": 50},
          "similarity": {"enabled": true, "threshold": 0.5},
          "threatIntel": {"enabled": true, "timeout": "00:00:30", "useVirusTotal": true},
          "autoBanThreshold": 80,
          "firstMessageOnly": true,
          "minMessageLength": 20
        }
        """;

        // Backup encryption config (real structure)
        public const string BackupEncryptionConfigJson = """
        {
          "Enabled": true,
          "Algorithm": "AES-256-GCM",
          "CreatedAt": "2025-10-28T03:44:47.159372+00:00",
          "Iterations": 100000,
          "LastRotatedAt": null
        }
        """;
    }

    /// <summary>
    /// Seeds full dataset: base data + GoldenDataset training labels (3 spam + 2 ham) + MLTrainingData.sql (20 spam + 20 ham).
    /// Total: 23 spam + 22 ham training samples.
    /// Use for most tests that need complete training data.
    /// </summary>
    public static async Task SeedAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider = null)
    {
        await SeedBaseDataAsync(context, dataProtectionProvider);
        await SeedGoldenDatasetTrainingLabelsAsync(context);
        await SeedMLTrainingDataScriptAsync(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds only base data (messages, users, chats, configs) - NO training labels or ML data.
    /// Total: 0 spam + 0 ham training samples.
    /// Use for threshold tests that need to create minimal custom datasets.
    /// </summary>
    public static async Task SeedWithoutTrainingDataAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider = null)
    {
        await SeedBaseDataAsync(context, dataProtectionProvider);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds base data + GoldenDataset training labels only (3 spam + 2 ham) - skips MLTrainingData.sql.
    /// Total: 3 spam + 2 ham training samples (below 20 minimum threshold).
    /// Use for tests validating behavior with insufficient training data.
    /// </summary>
    public static async Task SeedWithMinimalTrainingDataAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider = null)
    {
        await SeedBaseDataAsync(context, dataProtectionProvider);
        await SeedGoldenDatasetTrainingLabelsAsync(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Legacy method for backwards compatibility. Use SeedAsync() instead.
    /// </summary>
    [Obsolete("Use SeedAsync() instead")]
    public static async Task SeedDatabaseAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider = null)
    {
        await SeedAsync(context, dataProtectionProvider);
    }

    /// <summary>
    /// Seeds base database structure (users, chats, messages, detection_results, configs).
    /// Does NOT seed training_labels or ML training data.
    /// </summary>
    private static async Task SeedBaseDataAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider)
    {
        // Create Data Protector for API keys if provider available
        var apiKeyProtector = dataProtectionProvider?.CreateProtector(DataProtectionPurposes.ApiKeys);

        // 1. Seed telegram_users (no FK dependencies)
        await context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO telegram_users (telegram_user_id, username, first_name, last_name, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
            VALUES
            ({TelegramUsers.System_TelegramUserId}, '{TelegramUsers.System_Username}', NULL, NULL, {TelegramUsers.System_IsTrusted}, {TelegramUsers.System_BotDmEnabled}, NOW() - INTERVAL '14 days', NOW(), NOW() - INTERVAL '14 days', NOW()),
            ({TelegramUsers.User1_TelegramUserId}, '{TelegramUsers.User1_Username}', '{TelegramUsers.User1_FirstName}', '{TelegramUsers.User1_LastName}', {TelegramUsers.User1_IsTrusted}, {TelegramUsers.User1_BotDmEnabled}, NOW() - INTERVAL '13 days', NOW(), NOW() - INTERVAL '13 days', NOW()),
            ({TelegramUsers.User2_TelegramUserId}, '{TelegramUsers.User2_Username}', '{TelegramUsers.User2_FirstName}', '{TelegramUsers.User2_LastName}', {TelegramUsers.User2_IsTrusted}, {TelegramUsers.User2_BotDmEnabled}, NOW() - INTERVAL '12 days', NOW(), NOW() - INTERVAL '12 days', NOW()),
            ({TelegramUsers.User3_TelegramUserId}, '{TelegramUsers.User3_Username}', '{TelegramUsers.User3_FirstName}', '{TelegramUsers.User3_LastName}', {TelegramUsers.User3_IsTrusted}, {TelegramUsers.User3_BotDmEnabled}, NOW() - INTERVAL '11 days', NOW(), NOW() - INTERVAL '11 days', NOW()),
            ({TelegramUsers.User4_TelegramUserId}, '{TelegramUsers.User4_Username}', '{TelegramUsers.User4_FirstName}', '{TelegramUsers.User4_LastName}', {TelegramUsers.User4_IsTrusted}, {TelegramUsers.User4_BotDmEnabled}, NOW() - INTERVAL '10 days', NOW(), NOW() - INTERVAL '10 days', NOW()),
            ({TelegramUsers.User5_TelegramUserId}, '{TelegramUsers.User5_Username}', NULL, NULL, {TelegramUsers.User5_IsTrusted}, {TelegramUsers.User5_BotDmEnabled}, NOW() - INTERVAL '9 days', NOW(), NOW() - INTERVAL '9 days', NOW()),
            ({TelegramUsers.User6_TelegramUserId}, NULL, '{TelegramUsers.User6_FirstName}', NULL, {TelegramUsers.User6_IsTrusted}, {TelegramUsers.User6_BotDmEnabled}, NOW() - INTERVAL '8 days', NOW(), NOW() - INTERVAL '8 days', NOW()),
            ({TelegramUsers.User7_TelegramUserId}, '{TelegramUsers.User7_Username}', NULL, NULL, {TelegramUsers.User7_IsTrusted}, {TelegramUsers.User7_BotDmEnabled}, NOW() - INTERVAL '7 days', NOW(), NOW() - INTERVAL '7 days', NOW())
            """
        );

        // 2. Seed users (with self-referencing FK: invited_by)
        const string testPasswordHash = "AQAAAAIAAYagAAAAEDummyHashForTestingOnly1234567890";
        const string testSecurityStamp = "TEST_SECURITY_STAMP";

#pragma warning disable EF1002 // SQL injection risk suppressed: all values are static test constants
        await context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO users (id, email, normalized_email, password_hash, security_stamp, permission_level, invited_by, is_active, totp_secret, totp_enabled, totp_setup_started_at, created_at, last_login_at, status, email_verified, "InvitedByUserId")
            VALUES
            ('{Users.User1_Id}', '{Users.User1_Email}', '{Users.User1_Email.ToUpperInvariant()}', '{testPasswordHash}', '{testSecurityStamp}', {Users.User1_PermissionLevel}, NULL, TRUE, NULL, {Users.User1_TotpEnabled}, NULL, NOW() - INTERVAL '14 days', NOW(), {Users.User1_Status}, {Users.User1_EmailVerified}, NULL),
            ('{Users.User2_Id}', '{Users.User2_Email}', '{Users.User2_Email.ToUpperInvariant()}', '{testPasswordHash}', '{testSecurityStamp}', {Users.User2_PermissionLevel}, '{Users.User1_Id}', TRUE, NULL, {Users.User2_TotpEnabled}, NULL, NOW() - INTERVAL '13 days', NOW(), {Users.User2_Status}, {Users.User2_EmailVerified}, '{Users.User1_Id}'),
            ('{Users.User3_Id}', '{Users.User3_Email}', '{Users.User3_Email.ToUpperInvariant()}', '{testPasswordHash}', '{testSecurityStamp}', {Users.User3_PermissionLevel}, '{Users.User1_Id}', FALSE, NULL, {Users.User3_TotpEnabled}, NULL, NOW() - INTERVAL '12 days', NULL, {Users.User3_Status}, {Users.User3_EmailVerified}, '{Users.User1_Id}'),
            ('{Users.User4_Id}', '{Users.User4_Email}', '{Users.User4_Email.ToUpperInvariant()}', '{testPasswordHash}', '{testSecurityStamp}', {Users.User4_PermissionLevel}, '{Users.User2_Id}', FALSE, NULL, {Users.User4_TotpEnabled}, NULL, NOW() - INTERVAL '11 days', NULL, {Users.User4_Status}, {Users.User4_EmailVerified}, '{Users.User2_Id}')
            """
        );
#pragma warning restore EF1002

        // 3. Seed managed_chats
        await context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO managed_chats (chat_id, chat_name, chat_type, bot_status, is_admin, added_at, is_active, last_seen_at)
            VALUES
            ({ManagedChats.Chat1_Id}, '{ManagedChats.Chat1_Name}', {ManagedChats.Chat1_Type}, {ManagedChats.Chat1_BotStatus}, {ManagedChats.Chat1_IsAdmin}, NOW() - INTERVAL '13 days', {ManagedChats.Chat1_IsActive}, NOW()),
            ({ManagedChats.Chat2_Id}, '{ManagedChats.Chat2_Name}', {ManagedChats.Chat2_Type}, {ManagedChats.Chat2_BotStatus}, {ManagedChats.Chat2_IsAdmin}, NOW() - INTERVAL '12 days', {ManagedChats.Chat2_IsActive}, NOW()),
            ({ManagedChats.Chat3_Id}, '{ManagedChats.Chat3_Name}', {ManagedChats.Chat3_Type}, {ManagedChats.Chat3_BotStatus}, {ManagedChats.Chat3_IsAdmin}, NOW() - INTERVAL '11 days', {ManagedChats.Chat3_IsActive}, NOW()),
            ({ManagedChats.MainChat_Id}, '{ManagedChats.MainChat_Name}', {ManagedChats.MainChat_Type}, {ManagedChats.MainChat_BotStatus}, {ManagedChats.MainChat_IsAdmin}, NOW() - INTERVAL '10 days', {ManagedChats.MainChat_IsActive}, NOW())
            """
        );

        // 4. Seed linked_channels (FK to managed_chats)
        await context.Database.ExecuteSqlRawAsync(
            $$"""
            INSERT INTO linked_channels (managed_chat_id, channel_id, channel_name, channel_icon_path, photo_hash, last_synced)
            VALUES
            ({{LinkedChannels.Channel1_ManagedChatId}}, {{LinkedChannels.Channel1_ChannelId}}, {0}, NULL, {1}, NOW() - INTERVAL '1 day'),
            ({{LinkedChannels.Channel2_ManagedChatId}}, {{LinkedChannels.Channel2_ChannelId}}, {2}, {3}, NULL, NOW() - INTERVAL '2 days')
            """,
            LinkedChannels.Channel1_Name,
            LinkedChannels.Channel1_PhotoHash,
            LinkedChannels.Channel2_Name,
            (object?)LinkedChannels.Channel2_IconPath ?? DBNull.Value
        );

        // 5. Seed messages (use parameters for text to handle special characters)
        await context.Database.ExecuteSqlRawAsync(
            $$"""
            INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, media_type, content_check_skip_reason)
            VALUES
            ({{Messages.Msg1_Id}}, {{Messages.Msg1_UserId}}, {{Messages.Msg1_ChatId}}, NOW() - INTERVAL '1 hour', {0}, NULL, {{Messages.Msg1_ContentCheckSkipReason}}),
            ({{Messages.Msg2_Id}}, {{Messages.Msg2_UserId}}, {{Messages.Msg2_ChatId}}, NOW() - INTERVAL '2 hours', {1}, NULL, {{Messages.Msg2_ContentCheckSkipReason}}),
            ({{Messages.Msg3_Id}}, {{Messages.Msg3_UserId}}, {{Messages.Msg3_ChatId}}, NOW() - INTERVAL '3 hours', {2}, NULL, {{Messages.Msg3_ContentCheckSkipReason}}),
            ({{Messages.Msg4_Id}}, {{Messages.Msg4_UserId}}, {{Messages.Msg4_ChatId}}, NOW() - INTERVAL '4 hours', {3}, NULL, {{Messages.Msg4_ContentCheckSkipReason}}),
            ({{Messages.Msg5_Id}}, {{Messages.Msg5_UserId}}, {{Messages.Msg5_ChatId}}, NOW() - INTERVAL '5 hours', {4}, NULL, {{Messages.Msg5_ContentCheckSkipReason}}),
            ({{Messages.Msg6_Id}}, {{Messages.Msg6_UserId}}, {{Messages.Msg6_ChatId}}, NOW() - INTERVAL '6 hours', NULL, {{Messages.Msg6_MediaType}}, {{Messages.Msg6_ContentCheckSkipReason}}),
            ({{Messages.Msg7_Id}}, {{Messages.Msg7_UserId}}, {{Messages.Msg7_ChatId}}, NOW() - INTERVAL '7 hours', {5}, NULL, {{Messages.Msg7_ContentCheckSkipReason}}),
            ({{Messages.Msg8_Id}}, {{Messages.Msg8_UserId}}, {{Messages.Msg8_ChatId}}, NOW() - INTERVAL '8 hours', {6}, NULL, {{Messages.Msg8_ContentCheckSkipReason}}),
            ({{Messages.Msg9_Id}}, {{Messages.Msg9_UserId}}, {{Messages.Msg9_ChatId}}, NOW() - INTERVAL '9 hours', {7}, NULL, {{Messages.Msg9_ContentCheckSkipReason}}),
            ({{Messages.Msg10_Id}}, {{Messages.Msg10_UserId}}, {{Messages.Msg10_ChatId}}, NOW() - INTERVAL '10 hours', {8}, NULL, {{Messages.Msg10_ContentCheckSkipReason}}),
            ({{Messages.Msg11_Id}}, {{Messages.Msg11_UserId}}, {{Messages.Msg11_ChatId}}, NOW() - INTERVAL '11 hours', {9}, NULL, {{Messages.Msg11_ContentCheckSkipReason}})
            """,
            Messages.Msg1_Text, Messages.Msg2_Text, Messages.Msg3_Text, Messages.Msg4_Text, Messages.Msg5_Text,
            Messages.Msg7_Text, Messages.Msg8_Text, Messages.Msg9_Text, Messages.Msg10_Text, Messages.Msg11_Text
        );

        // 6. Seed detection_results (FK to messages)
        await context.Database.ExecuteSqlRawAsync(
            $$"""
            INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, reason, system_identifier, used_for_training, net_confidence, edit_version)
            VALUES
            ({{DetectionResults.Result1_MessageId}}, NOW() - INTERVAL '1 hour', 'System', {0}, {{DetectionResults.Result1_Confidence}}, {1}, 'spam-detection-v1', FALSE, {{DetectionResults.Result1_NetConfidence}}, 0),
            (82581, NOW() - INTERVAL '2 hours', 'System', {2}, {{DetectionResults.Result2_Confidence}}, {3}, 'spam-detection-v1', FALSE, {{DetectionResults.Result2_NetConfidence}}, 0)
            """,
            DetectionResults.Result1_DetectionMethod, DetectionResults.Result1_Reason,
            DetectionResults.Result2_DetectionMethod, DetectionResults.Result2_Reason
        );

        // 7. Seed configs (with encrypted api_keys and JSONB)
        string? encryptedApiKeys = null;
        if (apiKeyProtector != null)
        {
            var apiKeysJson = $$"""
            {
              "VirusTotal": "{{ApiKeys.VirusTotal_Test}}"
            }
            """;
            encryptedApiKeys = apiKeyProtector.Protect(apiKeysJson);
        }

        await context.Database.ExecuteSqlRawAsync(
            $$"""
            INSERT INTO configs (id, chat_id, spam_detection_config, api_keys, backup_encryption_config, created_at)
            VALUES
            ({{Configs.Config1_Id}}, {0}, {1}::jsonb, {2}, {3}::jsonb, NOW() - INTERVAL '10 days')
            """,
            Configs.Config1_ChatId,
            Configs.SpamDetectionConfigJson!,
            encryptedApiKeys!,
            Configs.BackupEncryptionConfigJson!
        );
    }

    /// <summary>
    /// Seeds GoldenDataset training labels (3 spam + 2 ham from original test data).
    /// </summary>
    private static async Task SeedGoldenDatasetTrainingLabelsAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            $$"""
            INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at, reason, audit_log_id)
            VALUES
            ({{TrainingLabels.Label1_MessageId}}, {{TrainingLabels.Label1_Label}}, {{TrainingLabels.Label1_LabeledByUserId}}, NOW() - INTERVAL '1 day', {0}, NULL),
            ({{TrainingLabels.Label2_MessageId}}, {{TrainingLabels.Label2_Label}}, NULL, NOW() - INTERVAL '2 days', {1}, NULL),
            ({{TrainingLabels.Label3_MessageId}}, {{TrainingLabels.Label3_Label}}, {{TrainingLabels.Label3_LabeledByUserId}}, NOW() - INTERVAL '3 days', {2}, NULL),
            ({{TrainingLabels.Label4_MessageId}}, {{TrainingLabels.Label4_Label}}, NULL, NOW() - INTERVAL '4 days', NULL, NULL),
            ({{TrainingLabels.Label5_MessageId}}, {{TrainingLabels.Label5_Label}}, NULL, NOW() - INTERVAL '5 days', NULL, NULL)
            """,
            TrainingLabels.Label1_Reason, TrainingLabels.Label2_Reason, TrainingLabels.Label3_Reason
        );
    }

    /// <summary>
    /// Seeds ML training data from embedded SQL script (20 spam + 20 ham).
    /// </summary>
    private static async Task SeedMLTrainingDataScriptAsync(AppDbContext context)
    {
        await LoadSqlScriptAsync(context, "MLTrainingData.sql");
    }

    /// <summary>
    /// Seeds balanced ML training data (20 spam + 20 ham).
    /// Use for tests requiring balanced training datasets.
    /// </summary>
    public static async Task SeedBalancedTrainingDataAsync(AppDbContext context)
    {
        await LoadSqlScriptAsync(context, "SQL.11_training_full.sql");
    }

    /// <summary>
    /// Seeds high-spam imbalanced ML training data (100 spam + 20 ham, 83.3% spam, 5:1 ratio).
    /// Use for testing ML classifier behavior with high spam ratio.
    /// </summary>
    public static async Task SeedHighSpamTrainingDataAsync(AppDbContext context)
    {
        await LoadSqlScriptAsync(context, "SQL.20_unbalanced_100_20.sql");
    }

    /// <summary>
    /// Seeds high-ham imbalanced ML training data (20 spam + 100 ham, 16.7% spam, 1:5 ratio).
    /// Use for testing ML classifier behavior with high ham ratio.
    /// </summary>
    public static async Task SeedHighHamTrainingDataAsync(AppDbContext context)
    {
        await LoadSqlScriptAsync(context, "SQL.21_unbalanced_20_100.sql");
    }

    /// <summary>
    /// Seeds deduplication test data with intentional near-duplicates (22 messages with 7 distinct groups).
    /// Use for testing SimHash near-duplicate detection accuracy.
    /// Message IDs: 95001-95022
    /// Groups:
    /// - Group 1 (95001-95004): Crypto signal spam variants
    /// - Group 2 (95005-95007): Investment scam variants
    /// - Group 3 (95008-95010): Giveaway scam variants
    /// - Group 4 (95011-95013): Different spam topics (not near-duplicates)
    /// - Group 5 (95014-95016): Legitimate ham messages
    /// - Group 6 (95017-95019): Ham near-duplicates
    /// - Group 7 (95020-95022): More spam variants
    /// </summary>
    public static async Task SeedDeduplicationTestDataAsync(AppDbContext context)
    {
        await LoadSqlScriptAsync(context, "SQL.30_dedup_test_data.sql");
    }

    /// <summary>
    /// Loads and executes an embedded SQL script from TestData directory.
    /// </summary>
    /// <param name="context">Database context</param>
    /// <param name="scriptPath">Relative path within TestData (e.g., "SQL.11_training_full.sql")</param>
    private static async Task LoadSqlScriptAsync(AppDbContext context, string scriptPath)
    {
        var assembly = typeof(GoldenDataset).Assembly;
        var resourceName = $"TelegramGroupsAdmin.IntegrationTests.TestData.{scriptPath}";
        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}. Ensure the SQL file is marked as EmbeddedResource in the .csproj file.");
        }

        using var reader = new StreamReader(stream);
        var sqlScript = await reader.ReadToEndAsync();
        await context.Database.ExecuteSqlRawAsync(sqlScript);
    }
}
