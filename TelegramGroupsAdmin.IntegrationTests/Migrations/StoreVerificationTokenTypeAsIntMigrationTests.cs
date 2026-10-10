using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// StoreVerificationTokenTypeAsInt up and down: verification_tokens.token_type changes from the strings
/// email_verify / password_reset / email_change to the TokenType ints 0 / 1 / 2 and back. Migration
/// fixture on an empty schema, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class StoreVerificationTokenTypeAsIntMigrationTests
{
    private MigrationTestHelper _helper = null!;

    private static string MigrationId<T>() where T : Migration =>
        typeof(T).GetCustomAttribute<MigrationAttribute>()!.Id;

    private static readonly string Previous = MigrationId<AddProfileScanAttemptedAt>();
    private static readonly string Target = MigrationId<StoreVerificationTokenTypeAsInt>();

    private const string UserId = "token-type-migration-user";

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(Previous);
        await _helper.ExecuteSqlAsync($"""
            INSERT INTO users (id, email, normalized_email, password_hash, security_stamp, permission_level, status, totp_enabled, email_verified, created_at)
            VALUES ('{UserId}', 'token@test.example', 'TOKEN@TEST.EXAMPLE', 'hash', 'stamp', 0, 1, false, false, now());
            """);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertTokenAsync(string token, string tokenType) => _helper.ExecuteSqlAsync($"""
        INSERT INTO verification_tokens (user_id, token_type, token, expires_at, created_at)
        VALUES ('{UserId}', '{tokenType}', '{token}', now() + interval '1 day', now());
        """);

    private async Task InsertEachKnownTypeAsync()
    {
        await InsertTokenAsync("t-verify", "email_verify");
        await InsertTokenAsync("t-reset", "password_reset");
        await InsertTokenAsync("t-change", "email_change");
    }

    [Test]
    public async Task Up_ConvertsEachStringToItsTokenTypeValue()
    {
        await InsertEachKnownTypeAsync();

        await _helper.ApplyNextMigrationAsync(Target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<string>(
                "SELECT data_type FROM information_schema.columns WHERE table_name = 'verification_tokens' AND column_name = 'token_type'"),
                Is.EqualTo("integer"));
            Assert.That(await _helper.ExecuteScalarAsync<int>("SELECT token_type FROM verification_tokens WHERE token = 't-verify'"), Is.Zero);
            Assert.That(await _helper.ExecuteScalarAsync<int>("SELECT token_type FROM verification_tokens WHERE token = 't-reset'"), Is.EqualTo(1));
            Assert.That(await _helper.ExecuteScalarAsync<int>("SELECT token_type FROM verification_tokens WHERE token = 't-change'"), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Up_UnknownString_FailsInsteadOfGuessing()
    {
        await InsertTokenAsync("t-unknown", "magic_link");

        var ex = Assert.ThrowsAsync<PostgresException>(() => _helper.ApplyNextMigrationAsync(Target));

        Assert.That(ex!.SqlState, Is.EqualTo(PostgresErrorCodes.NotNullViolation));
    }

    [Test]
    public async Task Down_RestoresTheStrings()
    {
        await InsertEachKnownTypeAsync();
        await _helper.ApplyNextMigrationAsync(Target);

        await _helper.ApplyNextMigrationAsync(Previous);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<string>("SELECT token_type FROM verification_tokens WHERE token = 't-verify'"), Is.EqualTo("email_verify"));
            Assert.That(await _helper.ExecuteScalarAsync<string>("SELECT token_type FROM verification_tokens WHERE token = 't-reset'"), Is.EqualTo("password_reset"));
            Assert.That(await _helper.ExecuteScalarAsync<string>("SELECT token_type FROM verification_tokens WHERE token = 't-change'"), Is.EqualTo("email_change"));
        }
    }
}
