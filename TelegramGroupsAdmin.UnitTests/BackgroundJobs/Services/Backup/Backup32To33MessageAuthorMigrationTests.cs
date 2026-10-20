using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Mirrors the cases of AddMessagesUserForeignKeyMigrationTests: the same repair rules applied to a 3.2
/// backup's JSON rows. Rows carry the snake_case keys the export writes (DTO property names).
/// </summary>
[TestFixture]
public class Backup32To33MessageAuthorMigrationTests
{
    private const long ChatId = -1001;
    private const long OtherChatId = -1002;
    private const long Orphan = 555;
    private const string ModeratorReason = "Marked as spam by moderator";

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // The restore path's options (BackupService.RestoreTableAsync).
    private static readonly JsonSerializerOptions RestoreOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private static object Message(int messageId, long userId, DateTimeOffset at, long chatId = ChatId) =>
        JsonSerializer.SerializeToElement(new { message_id = messageId, user_id = userId, chat_id = chatId, timestamp = at });

    // 3.2 exports carry is_spam (computed) alongside classification; the conversion must derive it from classification.
    private static object Decision(int messageId, int source, int classification, DateTimeOffset at,
        string reason = "x", long chatId = ChatId) =>
        JsonSerializer.SerializeToElement(new
        {
            id = 1, message_id = messageId, chat_id = chatId, detected_at = at, reason, source, classification,
            is_spam = false
        });

    private static object User(long id, string firstName = "Ann", bool banned = false) =>
        JsonSerializer.SerializeToElement(new
        {
            telegram_user_id = id, username = "ann", first_name = firstName, is_bot = false, is_trusted = false,
            is_active = true, is_banned = banned, kick_count = 0, bot_dm_enabled = true,
            first_seen_at = T0, last_seen_at = T0, created_at = T0, updated_at = T0
        });

    private static SystemBackup Build(object[] users, object[] messages, object[] decisions)
    {
        var data = new Dictionary<string, List<object>>
        {
            ["telegram_users"] = [.. users],
            ["messages"] = [.. messages],
            ["detection_results"] = [.. decisions]
        };
        return new SystemBackup
        {
            Metadata = new BackupMetadata { Version = "3.2", TableCount = 3, Tables = [.. data.Keys] },
            Data = data
        };
    }

    private static List<TelegramUserDto> Users(SystemBackup backup) =>
        [.. backup.Data!["telegram_users"].Select(r =>
            JsonSerializer.Deserialize<TelegramUserDto>(((JsonElement)r).GetRawText(), RestoreOptions)!)];

    private static TelegramUserDto Run(SystemBackup backup, long id)
    {
        Backup32To33MessageAuthorMigration.Apply(backup, NullLogger.Instance);
        return Users(backup).Single(u => u.TelegramUserId == id);
    }

    private static SystemBackup OrphanWith(int source, int classification, string reason = "x", long decisionChat = ChatId) =>
        Build([User(0)], [Message(1, Orphan, T0.AddHours(1))], [Decision(1, source, classification, T0.AddHours(2), reason, decisionChat)]);

    [Test]
    public void Apply_OrphanWithSpamCommandDecision_GetsBannedRow()
    {
        var backup = Build([User(0), User(7)], [Message(1, Orphan, T0.AddHours(1)), Message(2, Orphan, T0.AddHours(3))],
            [Decision(2, 13, 0, T0.AddHours(4)), Decision(1, 13, 0, T0.AddHours(2))]);

        var user = Run(backup, Orphan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.IsBanned, Is.True);
            Assert.That(user.BannedAt, Is.EqualTo(T0.AddHours(2)), "earliest qualifying decision");
            Assert.That(user.BanExpiresAt, Is.Null);
            Assert.That(user.IsTrusted, Is.False);
            Assert.That(user.IsActive, Is.False);
            Assert.That(user.IsBot, Is.False);
            Assert.That(user.BotDmEnabled, Is.False);
            Assert.That(user.FirstName, Is.Null);
            Assert.That(user.NamesObservedAt, Is.Null);
            Assert.That(user.FirstSeenAt, Is.EqualTo(T0.AddHours(1)));
            Assert.That(user.LastSeenAt, Is.EqualTo(T0.AddHours(3)));
        }
    }

    [Test]
    public void Apply_OrphanWithMessagesInTwoChats_SpansBothAndIsBannedFromTheSecond()
    {
        var backup = Build([User(0)],
            [Message(1, Orphan, T0.AddHours(1)), Message(1, Orphan, T0.AddHours(5), OtherChatId)],
            [Decision(1, 10, 0, T0.AddHours(6), chatId: OtherChatId)]);

        var user = Run(backup, Orphan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.FirstSeenAt, Is.EqualTo(T0.AddHours(1)));
            Assert.That(user.LastSeenAt, Is.EqualTo(T0.AddHours(5)));
            Assert.That(user.IsBanned, Is.True);
            Assert.That(user.BannedAt, Is.EqualTo(T0.AddHours(6)));
        }
    }

    [Test]
    public void Apply_DecisionOnSameMessageIdInAnotherChat_DoesNotBanTheOrphan()
    {
        // The orphan's message is in ChatId; the qualifying decision is for message 1 in OtherChatId, which belongs to another user.
        var backup = Build([User(0), User(7)],
            [Message(1, Orphan, T0.AddHours(1)), Message(1, 7, T0.AddHours(1), OtherChatId)],
            [Decision(1, 13, 0, T0.AddHours(2), chatId: OtherChatId)]);

        Assert.That(Run(backup, Orphan).IsBanned, Is.False);
    }

    [TestCase(10)]
    [TestCase(11)]
    [TestCase(13)]
    [TestCase(14)]
    public void Apply_OrphanWithEachBanningSource_IsBanned(int source) =>
        Assert.That(Run(OrphanWith(source, 0), Orphan).IsBanned, Is.True);

    [Test]
    public void Apply_OrphanWithLegacyModeratorReason_IsBanned() =>
        Assert.That(Run(OrphanWith(99, 0, ModeratorReason), Orphan).IsBanned, Is.True);

    [Test]
    public void Apply_OrphanWithOtherLegacyReason_IsNotBanned() =>
        Assert.That(Run(OrphanWith(99, 0, "Marked as spam by moderator!"), Orphan).IsBanned, Is.False);

    [Test]
    public void Apply_OrphanWithOnlyAServiceMessage_IsNotBanned()
    {
        // A service message has no decision at all.
        var backup = Build([User(0)], [Message(1, Orphan, T0.AddHours(1))], []);

        var user = Run(backup, Orphan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.IsBanned, Is.False);
            Assert.That(user.BannedAt, Is.Null);
        }
    }

    [Test]
    public void Apply_OrphanWithHamDecisionCarryingModeratorReason_IsNotBanned() =>
        Assert.That(Run(OrphanWith(99, 1, ModeratorReason), Orphan).IsBanned, Is.False);

    [Test]
    public void Apply_OrphanWithOnlyAContentScanSpamVerdict_IsNotBanned() =>
        Assert.That(Run(OrphanWith(0, 0), Orphan).IsBanned, Is.False);

    [TestCase(10)]
    [TestCase(13)]
    public void Apply_OrphanWithChatZeroSpamRow_IsNotBanned(int source)
    {
        var backup = Build([User(0)], [Message(1, Orphan, T0.AddHours(1), 0)], [Decision(1, source, 0, T0.AddHours(2), chatId: 0)]);

        Assert.That(Run(backup, Orphan).IsBanned, Is.False);
    }

    [TestCase(0, true)]
    [TestCase(2, true)]
    [TestCase(4, true)]
    [TestCase(1, false)]
    [TestCase(3, false)]
    public void Apply_SpamIsDerivedFromClassification_NotFromTheExportedIsSpamFlag(int classification, bool banned) =>
        Assert.That(Run(OrphanWith(13, classification), Orphan).IsBanned, Is.EqualTo(banned));

    [Test]
    public void Apply_OrphanWithSystemAccountId_IsTrusted()
    {
        var backup = Build([User(0)], [Message(1, 777000, T0.AddHours(1))], []);

        var user = Run(backup, 777000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.IsTrusted, Is.True);
            Assert.That(user.IsBanned, Is.False);
        }
    }

    [Test]
    public void Apply_ExistingUser_IsUntouched()
    {
        var existing = User(7, "Keep");
        var backup = Build([User(0), existing], [Message(1, 7, T0.AddHours(1))], [Decision(1, 13, 0, T0.AddHours(2))]);

        Backup32To33MessageAuthorMigration.Apply(backup, NullLogger.Instance);

        var users = Users(backup);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(users, Has.Count.EqualTo(2));
            Assert.That(users.Single(u => u.TelegramUserId == 7).FirstName, Is.EqualTo("Keep"));
            Assert.That(users.Single(u => u.TelegramUserId == 7).IsBanned, Is.False);
            Assert.That(backup.Data!["telegram_users"], Does.Contain(existing));
        }
    }

    [Test]
    public void Apply_NoUserZero_CreatesIt()
    {
        var backup = Build([], [Message(1, 0, T0.AddHours(1), 0)], []);

        var user = Run(backup, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.IsBanned, Is.False);
            Assert.That(user.IsTrusted, Is.False);
            Assert.That(user.IsActive, Is.False);
            Assert.That(user.FirstName, Is.Null);
            Assert.That(Users(backup), Has.Count.EqualTo(1), "user 0's own messages do not make it an orphan twice");
        }
    }

    [Test]
    public void Apply_NoUserZeroAndNoMessages_CreatesIt()
    {
        var backup = Build([], [], []);

        Assert.That(Run(backup, 0).IsBanned, Is.False);
    }

    [Test]
    public void Apply_NoUserZero_WithQualifyingDecision_IsNotBanned()
    {
        var backup = Build([], [Message(1, 0, T0.AddHours(1))], [Decision(1, 13, 0, T0.AddHours(2))]);

        var user = Run(backup, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.IsBanned, Is.False);
            Assert.That(user.BannedAt, Is.Null);
        }
    }

    [Test]
    public void Apply_ExistingUserZero_IsUntouched()
    {
        var backup = Build([User(0, "Zero")], [], []);

        var user = Run(backup, 0);

        Assert.That(user.FirstName, Is.EqualTo("Zero"));
        Assert.That(Users(backup), Has.Count.EqualTo(1));
    }

    [Test]
    public void Apply_BackupWithoutMessagesTable_AddsOnlyUserZero()
    {
        var backup = new SystemBackup
        {
            Metadata = new BackupMetadata { Version = "3.2", TableCount = 1, Tables = ["telegram_users"] },
            Data = new Dictionary<string, List<object>> { ["telegram_users"] = [User(7)] }
        };

        Backup32To33MessageAuthorMigration.Apply(backup, NullLogger.Instance);

        Assert.That(Users(backup).Select(u => u.TelegramUserId), Is.EquivalentTo(new long[] { 7, 0 }));
    }
}
