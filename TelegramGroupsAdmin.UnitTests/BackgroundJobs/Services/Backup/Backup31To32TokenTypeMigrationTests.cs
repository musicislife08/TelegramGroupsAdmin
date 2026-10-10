using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// verification_tokens rows are shaped the way the 3.1 export wrote them: keys are the snake_case of the
/// DTO property names, so the string type sits under token_type_string.
/// </summary>
[TestFixture]
public class Backup31To32TokenTypeMigrationTests
{
    private const string Table = "verification_tokens";

    // The restore path's options (BackupService.RestoreTableAsync).
    private static readonly JsonSerializerOptions RestoreOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private static object Row(long id, string tokenType) => JsonSerializer.SerializeToElement(new
    {
        id, user_id = "u1", token_type_string = tokenType, token = $"t{id}", value = (string?)null,
        expires_at = "2026-01-02T00:00:00+00:00", created_at = "2026-01-01T00:00:00+00:00", used_at = (string?)null
    });

    private static SystemBackup Build(params object[] rows) => new()
    {
        Metadata = new BackupMetadata { Version = "3.1", TableCount = 1, Tables = [Table] },
        Data = new Dictionary<string, List<object>> { [Table] = [.. rows] }
    };

    private static VerificationTokenDto Restore(object row) =>
        JsonSerializer.Deserialize<VerificationTokenDto>(((JsonElement)row).GetRawText(), RestoreOptions)!;

    [Test]
    public void Apply_ConvertsEachStringToItsTokenTypeValue()
    {
        var backup = Build(Row(1, "email_verify"), Row(2, "password_reset"), Row(3, "email_change"));

        Backup31To32TokenTypeMigration.Apply(backup, NullLogger.Instance);

        var restored = backup.Data![Table].Select(Restore).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Select(r => r.TokenType), Is.EqualTo(new[]
            {
                (int)TokenType.EmailVerification, (int)TokenType.PasswordReset, (int)TokenType.EmailChange
            }));
            Assert.That(restored.Select(r => r.Token), Is.EqualTo(new[] { "t1", "t2", "t3" }));
            Assert.That(((JsonElement)backup.Data[Table][0]).TryGetProperty("token_type_string", out _), Is.False);
        }
    }

    [Test]
    public void Apply_UnknownTokenType_RefusesTheBackup()
    {
        var backup = Build(Row(1, "email_verify"), Row(7, "magic_link"));

        var ex = Assert.Throws<InvalidOperationException>(() => Backup31To32TokenTypeMigration.Apply(backup, NullLogger.Instance));

        Assert.That(ex!.Message, Does.Contain("magic_link").And.Contain("7"));
    }

    [Test]
    public void Apply_NoVerificationTokens_LeavesTheBackupAlone()
    {
        var backup = Build();
        backup.Data!.Remove(Table);

        Assert.DoesNotThrow(() => Backup31To32TokenTypeMigration.Apply(backup, NullLogger.Instance));
        Assert.That(backup.Data, Is.Empty);
    }

    [Test]
    public void Apply_NoData_DoesNothing()
    {
        var backup = Build();
        backup.Data = null;

        Assert.DoesNotThrow(() => Backup31To32TokenTypeMigration.Apply(backup, NullLogger.Instance));
    }
}
