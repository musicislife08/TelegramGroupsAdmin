using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;

/// <summary>
/// Backup format 3.1 → 3.2 (verification token type stored as int): a 3.1 backup carries each
/// verification_tokens row's type as a string under <c>token_type_string</c>; 3.2 carries the TokenType
/// value under <c>token_type</c>. Converts the one into the other so a 3.1 backup restores into the 3.2 schema.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate implementation from the SQL migration <c>StoreVerificationTokenTypeAsInt</c>
/// (backup-only, time-boxed); its mapping mirrors that migration, and like it, an unknown value refuses
/// the backup instead of being guessed.
/// </para>
/// <para>
/// Remove this class, and its call in <c>BackupService.ApplyBackupMigrations</c>, one year after the
/// release that introduced backup format 3.2.
/// </para>
/// </remarks>
public static class Backup31To32TokenTypeMigration
{
    private const string VerificationTokens = "verification_tokens";
    private const string LegacyKey = "token_type_string";
    private const string Key = "token_type";

    // TokenType values (Telegram.Models.TokenType)
    private const int EmailVerification = 0;
    private const int PasswordReset = 1;
    private const int EmailChange = 2;

    public static void Apply(SystemBackup backup, ILogger logger)
    {
        if (backup.Data is null || !backup.Data.TryGetValue(VerificationTokens, out var rows))
            return;

        var converted = rows.Select(ToObject).ToList();
        foreach (var row in converted)
        {
            var legacy = row[LegacyKey] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            row[Key] = TokenTypeOf(legacy, row);
            row.Remove(LegacyKey);
        }

        backup.Data[VerificationTokens] = [.. converted.Select(o => (object)JsonSerializer.SerializeToElement(o))];

        logger.LogInformation("Backup 3.1→3.2: converted {Rows} verification token types to int", converted.Count);
    }

    private static int TokenTypeOf(string? legacy, JsonObject row) => legacy switch
    {
        "email_verify" => EmailVerification,
        "password_reset" => PasswordReset,
        "email_change" => EmailChange,
        // Every value the app ever wrote is listed above; refuse anything else (matches the SQL migration).
        _ => throw new InvalidOperationException(
            $"Backup verification_tokens row {row["id"]?.ToJsonString() ?? "(no id)"} has unknown token type '{legacy}'")
    };

    private static JsonObject ToObject(object row) => row switch
    {
        JsonElement element => JsonNode.Parse(element.GetRawText())!.AsObject(),
        _ => JsonSerializer.SerializeToNode(row)!.AsObject()
    };
}
