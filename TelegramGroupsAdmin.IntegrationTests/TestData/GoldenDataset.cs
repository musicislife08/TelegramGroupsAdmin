using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

public static class GoldenDataset
{
    /// <summary>
    /// Loads the 35 canonical/*.sql fixtures FK-ordered into the target context, then
    /// runs the encrypted-column UPDATE post-step using the supplied DataProtection
    /// provider. Used by PostgresFixture.[OneTimeSetUp] to build golden_template, and
    /// by GoldenReducePlanTests to exercise Reduce against canonical without depending
    /// on Phase 2's template infrastructure.
    ///
    /// HASHES are pre-baked into the SQL files (see Pre-1b) — this method does NOT
    /// recompute content_hash / similarity_hash at load time. The only post-load step
    /// is the encrypted-column UPDATE below.
    /// </summary>
    public static async Task LoadCanonicalAsync(
        AppDbContext context,
        IDataProtectionProvider dataProtection,
        CancellationToken ct = default)
    {
        // FK-safe load order matching TestData/SQL/canonical/ exactly (35 files;
        // numeric on-disk order IS the FK-safe order — Pre-1b enforced this).
        // Resource names use '.' separators per .NET embedded-resource conventions:
        // path "TestData/SQL/canonical/01_users.sql" -> "SQL.canonical.01_users.sql".
        string[] fixtures =
        {
            // Layer 0 roots — users, telegram_users, managed_chats first
            "SQL.canonical.01_users.sql",
            "SQL.canonical.02_telegram_users.sql",
            "SQL.canonical.03_managed_chats.sql",
            // Independent reference / config tables
            "SQL.canonical.04_configs.sql",
            "SQL.canonical.05_content_detection_configs.sql",
            "SQL.canonical.06_ban_celebration_captions.sql",
            "SQL.canonical.07_ban_celebration_gifs.sql",
            "SQL.canonical.08_blocklist_subscriptions.sql",
            "SQL.canonical.09_prompt_versions.sql",
            "SQL.canonical.10_recovery_codes.sql",       // EMPTY (0 rows)
            "SQL.canonical.11_stop_words.sql",
            "SQL.canonical.12_tag_definitions.sql",
            "SQL.canonical.13_username_blacklist.sql",   // 2 rows (Exact only)
            "SQL.canonical.14_domain_filters.sql",       // EMPTY
            "SQL.canonical.15_image_training_samples.sql", // EMPTY
            "SQL.canonical.16_video_training_samples.sql", // EMPTY
            "SQL.canonical.17_web_notifications.sql",    // EMPTY
            "SQL.canonical.18_notification_preferences.sql",
            // Layer 1 — children of roots
            "SQL.canonical.19_messages.sql",             // 400 rows
            "SQL.canonical.20_chat_admins.sql",
            "SQL.canonical.21_linked_channels.sql",
            "SQL.canonical.22_telegram_user_mappings.sql",
            "SQL.canonical.23_profile_scan_results.sql",
            "SQL.canonical.24_username_history.sql",
            "SQL.canonical.25_admin_notes.sql",
            "SQL.canonical.26_audit_log.sql",
            "SQL.canonical.27_user_tags.sql",
            "SQL.canonical.28_welcome_responses.sql",    // includes synthetic 999001..999005
            "SQL.canonical.29_invites.sql",
            "SQL.canonical.30_reports.sql",
            // Layer 2 — children of messages
            "SQL.canonical.31_message_edits.sql",
            "SQL.canonical.32_detection_results.sql",
            "SQL.canonical.33_training_labels.sql",      // 200 rows
            "SQL.canonical.34_user_actions.sql",         // 993 rows
            // Layer 3 — child of messages AND message_edits
            "SQL.canonical.35_message_translations.sql",
        };

        foreach (var fixture in fixtures)
        {
            ct.ThrowIfCancellationRequested();
            await LoadCanonicalSqlScriptAsync(context, fixture);
        }

        // Encrypted-column post-step: 04_configs.sql seeds the configs rows with all
        // DataProtection-encrypted columns NULL. Encrypt canonical plaintext under the
        // shared provider and UPDATE. Each column has its OWN purpose string — production
        // code in TelegramGroupsAdmin.Configuration uses DataProtectionPurposes.ApiKeys
        // ("ApiKeys") for the api_keys column, so canonical MUST use the same constant —
        // mismatched purposes would write ciphertext production code can't decrypt.
        var apiKeysProtector = dataProtection.CreateProtector(DataProtectionPurposes.ApiKeys);
        var apiKeysCanonical = apiKeysProtector.Protect("""{"openai":"sk-canonical-test-key"}""");

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE configs SET api_keys = {0} WHERE chat_id = 0",
            apiKeysCanonical);
    }

    /// <summary>
    /// Entry point for the subtractive Reduce builder. Returns a stage-1 plan bound
    /// to the supplied context; no DB work runs until ApplyAsync is called. Each
    /// invocation returns a fresh plan — plans are single-shot.
    /// </summary>
    public static GoldenReducePlanBuilder Reduce(AppDbContext context)
        => new GoldenReducePlanBuilder(new GoldenReducePlanState(context));

    /// <summary>
    /// Entry point for the in-place Mutator. Returns a builder bound to the supplied
    /// context; no DB work runs until ApplyAsync is called. Reserve for cases where
    /// canonical structurally cannot provide the shape (the canonical example is
    /// analytics aggregations that need NOW()-relative timestamps).
    /// </summary>
    public static GoldenMutatePlanBuilder Mutate(AppDbContext context)
        => new GoldenMutatePlanBuilder(context);

    /// <summary>
    /// Loads and executes an embedded canonical SQL script via a raw <see cref="NpgsqlCommand"/>
    /// that bypasses EF Core's <c>{n}</c> parameter parser. Required because canonical fixtures
    /// (pg_dump --column-inserts output) carry single-brace JSONB literals like
    /// <c>'{{"ChatId": -123, ...}}'</c> that ExecuteSqlRawAsync mis-parses as parameter
    /// placeholders.
    /// </summary>
    private static async Task LoadCanonicalSqlScriptAsync(AppDbContext context, string scriptPath)
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

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sqlScript, connection);
        await cmd.ExecuteNonQueryAsync();
    }
}
