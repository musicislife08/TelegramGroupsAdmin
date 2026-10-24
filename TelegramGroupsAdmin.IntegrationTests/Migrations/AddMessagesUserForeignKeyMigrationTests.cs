using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// AddMessagesUserForeignKey up and down: user 0 and every message author without a telegram_users
/// row get an id-only row (banned when the app's own records show it banned them), then
/// messages.user_id gains its key. Migration fixture on an empty schema, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class AddMessagesUserForeignKeyMigrationTests
{
    private MigrationTestHelper _helper = null!;

    private static string MigrationId<T>() where T : Migration =>
        typeof(T).GetCustomAttribute<MigrationAttribute>()!.Id;

    private static readonly string Previous = MigrationId<StoreVerificationTokenTypeAsInt>();
    private static readonly string Target = MigrationId<AddMessagesUserForeignKey>();

    private const long ChatId = -1001;
    private const long OtherChatId = -1002;
    private const long OrphanId = 5001;
    private const long ExistingId = 5002;
    private const string ModeratorReason = "Marked as spam by moderator";

    private static readonly DateTimeOffset T1 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 1, 3, 10, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(Previous);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private static string Sql(DateTimeOffset at) => $"'{at:O}'::timestamptz";

    private Task InsertMessageAsync(int messageId, long userId, DateTimeOffset at, long chatId = ChatId) =>
        _helper.ExecuteSqlAsync($"""
            INSERT INTO messages (message_id, user_id, chat_id, "timestamp")
            VALUES ({messageId}, {userId}, {chatId}, {Sql(at)});
            """);

    private Task InsertDecisionAsync(int messageId, int source, int classification, DateTimeOffset at,
        string reason = "x", long chatId = ChatId) =>
        _helper.ExecuteSqlAsync($"""
            INSERT INTO detection_results (message_id, chat_id, detected_at, detection_method, score, reason,
                system_identifier, edit_version, source, classification)
            VALUES ({messageId}, {chatId}, {Sql(at)}, 'x', 1, '{reason}', 'integration-test', 0, {source}, {classification});
            """);

    private Task InsertUserAsync(long id, string firstName, string username, DateTimeOffset at) =>
        _helper.ExecuteSqlAsync($"""
            INSERT INTO telegram_users (telegram_user_id, username, first_name, last_name, names_observed_at,
                is_bot, is_trusted, is_banned, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at, is_active)
            VALUES ({id}, '{username}', '{firstName}', NULL, {Sql(at)},
                false, false, false, true, {Sql(at)}, {Sql(at)}, {Sql(at)}, {Sql(at)}, true);
            """);

    private Task<bool?> IsBannedAsync(long id) =>
        _helper.ExecuteScalarAsync<bool?>($"SELECT is_banned FROM telegram_users WHERE telegram_user_id = {id}");

    private Task<long> UserCountAsync(long id) =>
        _helper.ExecuteScalarAsync<long>($"SELECT count(*) FROM telegram_users WHERE telegram_user_id = {id}");

    private Task<string?> UserRowAsync(long id) =>
        _helper.ExecuteScalarAsync<string>($"SELECT row_to_json(t)::text FROM telegram_users t WHERE telegram_user_id = {id}");

    [Test]
    public async Task Up_OrphanWithSpamCommandDecision_GetsBannedRow()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertMessageAsync(2, OrphanId, T3);
        await InsertDecisionAsync(2, source: 13, classification: 0, T3);
        await InsertDecisionAsync(1, source: 13, classification: 0, T2);

        await _helper.ApplyNextMigrationAsync(Target);

        var where = $"FROM telegram_users WHERE telegram_user_id = {OrphanId}";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await IsBannedAsync(OrphanId), Is.True);
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT banned_at {where}"), Is.EqualTo(T2),
                "banned_at is the earliest qualifying decision");
            Assert.That(await _helper.ExecuteScalarAsync<long>($"SELECT count(*) {where} AND ban_expires_at IS NULL"), Is.EqualTo(1));
            Assert.That(await _helper.ExecuteScalarAsync<long>(
                $"SELECT count(*) {where} AND username IS NULL AND first_name IS NULL AND last_name IS NULL AND names_observed_at IS NULL"),
                Is.EqualTo(1));
            Assert.That(await _helper.ExecuteScalarAsync<bool>($"SELECT is_trusted {where}"), Is.False);
            Assert.That(await _helper.ExecuteScalarAsync<bool>($"SELECT is_active {where}"), Is.False);
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT first_seen_at {where}"), Is.EqualTo(T1));
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT last_seen_at {where}"), Is.EqualTo(T3));
        }
    }

    [Test]
    public async Task Up_OrphanWithMessagesInTwoChats_SpansBothAndIsBannedFromTheSecond()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertMessageAsync(1, OrphanId, T3, chatId: OtherChatId);
        await InsertDecisionAsync(1, source: 13, classification: 0, T3, chatId: OtherChatId);

        await _helper.ApplyNextMigrationAsync(Target);

        var where = $"FROM telegram_users WHERE telegram_user_id = {OrphanId}";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT first_seen_at {where}"), Is.EqualTo(T1));
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT last_seen_at {where}"), Is.EqualTo(T3));
            Assert.That(await IsBannedAsync(OrphanId), Is.True);
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>($"SELECT banned_at {where}"), Is.EqualTo(T3));
        }
    }

    // Message ids are unique only per chat: a decision on another author's message with the same id in
    // another chat must not ban the orphan (the join needs chat_id as well as message_id).
    [Test]
    public async Task Up_DecisionOnSameMessageIdInAnotherChat_DoesNotBanTheOrphan()
    {
        await InsertUserAsync(ExistingId, "Existing", "existing_user", T1);
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertMessageAsync(1, ExistingId, T2, chatId: OtherChatId);
        await InsertDecisionAsync(1, source: 13, classification: 0, T3, chatId: OtherChatId);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    [TestCase(10)]
    [TestCase(11)]
    [TestCase(14)]
    public async Task Up_OrphanWithEachBanningSource_IsBanned(int source)
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source, classification: 0, T2);

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await IsBannedAsync(OrphanId), Is.True);
    }

    [Test]
    public async Task Up_OrphanWithLegacyModeratorReason_IsBanned()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source: 99, classification: 0, T2, reason: ModeratorReason);

        await _helper.ApplyNextMigrationAsync(Target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await IsBannedAsync(OrphanId), Is.True);
            Assert.That(await _helper.ExecuteScalarAsync<DateTimeOffset>(
                $"SELECT banned_at FROM telegram_users WHERE telegram_user_id = {OrphanId}"), Is.EqualTo(T2));
        }
    }

    [Test]
    public async Task Up_OrphanWithOtherLegacyReason_IsNotBanned()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source: 99, classification: 0, T2, reason: "Migrated training label");

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    [Test]
    public async Task Up_OrphanWithOnlyAServiceMessage_IsNotBanned()
    {
        await InsertMessageAsync(1, OrphanId, T1);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    // Source 13 rows are always spam (CK_detection_results_source_classification), so the ham case for
    // the is_spam gate is a LegacyManual row carrying the moderator reason with a ham classification.
    [Test]
    public async Task Up_OrphanWithHamDecisionCarryingModeratorReason_IsNotBanned()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source: 99, classification: 1, T2, reason: ModeratorReason);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    // A content scan's spam verdict (ImplicitSpam) is not a record of a ban.
    [Test]
    public async Task Up_OrphanWithOnlyAContentScanSpamVerdict_IsNotBanned()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source: 0, classification: 2, T2);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    // Chat 0 holds training samples (TrainingDataPage, Import, web-entered spam); a spam label there bans nobody.
    [TestCase(16)]
    [TestCase(18)]
    [TestCase(11)]
    public async Task Up_OrphanWithChatZeroSpamRow_IsNotBanned(int source)
    {
        await InsertMessageAsync(1, OrphanId, T1, chatId: 0);
        await InsertDecisionAsync(1, source, classification: 0, T2, chatId: 0);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(OrphanId);
    }

    [Test]
    public async Task Up_OrphanWithSystemAccountId_IsTrusted()
    {
        const long serviceAccountId = 777000;
        await InsertMessageAsync(1, serviceAccountId, T1);

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await _helper.ExecuteScalarAsync<bool>(
            $"SELECT is_trusted FROM telegram_users WHERE telegram_user_id = {serviceAccountId}"), Is.True);
    }

    [Test]
    public async Task Up_ExistingUser_IsUntouched()
    {
        await InsertUserAsync(ExistingId, "Existing", "existing_user", T1);
        await InsertMessageAsync(1, ExistingId, T2);
        await InsertDecisionAsync(1, source: 13, classification: 0, T3);
        var before = await UserRowAsync(ExistingId);

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await UserRowAsync(ExistingId), Is.EqualTo(before));
    }

    [Test]
    public async Task Up_NoUserZero_CreatesIt()
    {
        await InsertMessageAsync(1, 0, T1);

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM telegram_users WHERE telegram_user_id = 0 " +
            "AND NOT is_trusted AND NOT is_active AND NOT is_banned AND NOT bot_dm_enabled AND banned_at IS NULL " +
            "AND username IS NULL AND first_name IS NULL AND last_name IS NULL AND names_observed_at IS NULL"),
            Is.EqualTo(1), "user 0: untrusted, inactive, unbanned, no names");
    }

    [Test]
    public async Task Up_NoUserZeroAndNoMessages_CreatesIt()
    {
        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await UserCountAsync(0), Is.EqualTo(1));
    }

    // User 0 is never an orphan (it is created first), so a spam decision on a channel post bans nobody.
    [Test]
    public async Task Up_NoUserZero_WithQualifyingDecision_IsNotBanned()
    {
        await InsertMessageAsync(1, 0, T1);
        await InsertDecisionAsync(1, source: 13, classification: 0, T2);

        await _helper.ApplyNextMigrationAsync(Target);

        await AssertCreatedNotBannedAsync(0);
    }

    [Test]
    public async Task Up_ExistingUserZero_IsUntouched()
    {
        await InsertUserAsync(0, "Channel", "channel_posts", T1);
        await InsertMessageAsync(1, 0, T2);
        var before = await UserRowAsync(0);

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await UserRowAsync(0), Is.EqualTo(before));
    }

    [Test]
    public async Task Up_ThenInsertMessageForUnknownUser_FailsForeignKey()
    {
        await _helper.ApplyNextMigrationAsync(Target);

        var ex = Assert.ThrowsAsync<PostgresException>(() => InsertMessageAsync(1, OrphanId, T1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.SqlState, Is.EqualTo(PostgresErrorCodes.ForeignKeyViolation));
            Assert.That(ex.ConstraintName, Is.EqualTo("FK_messages_telegram_users_user_id"));
        }
    }

    [Test]
    public async Task Up_DeletingAUser_DeletesTheirMessages()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await _helper.ApplyNextMigrationAsync(Target);

        await _helper.ExecuteSqlAsync($"DELETE FROM telegram_users WHERE telegram_user_id = {OrphanId}");

        Assert.That(await _helper.ExecuteScalarAsync<long>($"SELECT count(*) FROM messages WHERE user_id = {OrphanId}"),
            Is.Zero);
    }

    [Test]
    public async Task Down_DropsTheKey_KeepsRepairedRows()
    {
        await InsertMessageAsync(1, OrphanId, T1);
        await InsertDecisionAsync(1, source: 13, classification: 0, T2);
        await _helper.ApplyNextMigrationAsync(Target);

        await _helper.ApplyNextMigrationAsync(Previous);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM information_schema.table_constraints " +
                "WHERE table_schema = current_schema() AND constraint_name = 'FK_messages_telegram_users_user_id'"),
                Is.Zero);
            Assert.That(await IsBannedAsync(OrphanId), Is.True);
            Assert.That(await UserCountAsync(0), Is.EqualTo(1));
        }

        await InsertMessageAsync(2, 9999, T3);
        Assert.That(await _helper.ExecuteScalarAsync<long>("SELECT count(*) FROM messages WHERE user_id = 9999"),
            Is.EqualTo(1), "without the key an authorless message is accepted again");
    }

    private async Task AssertCreatedNotBannedAsync(long id)
    {
        const string cols = "is_banned, banned_at, ban_expires_at";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await UserCountAsync(id), Is.EqualTo(1), "the orphan gets a row");
            Assert.That(await _helper.ExecuteScalarAsync<long>(
                $"SELECT count(*) FROM (SELECT {cols} FROM telegram_users WHERE telegram_user_id = {id}) t " +
                "WHERE NOT is_banned AND banned_at IS NULL AND ban_expires_at IS NULL"),
                Is.EqualTo(1), "and is not banned");
        }
    }
}
