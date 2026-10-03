using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>
/// Builds the session templates once and clones/drops per-test databases from them.
/// Every connection here uses Pooling=false: Postgres refuses CREATE DATABASE … TEMPLATE
/// while any other session is connected to the template.
/// </summary>
public static class GoldenTemplates
{
    public const string EmptyTemplateName = "empty_template";
    public const string GoldenTemplateName = "golden_template";

    /// <summary>
    /// Creates empty_template (migrated) and golden_template (empty + canonical, encrypted
    /// columns protected with <paramref name="dataProtection"/>), then flags both as templates.
    /// </summary>
    public static async Task BuildAsync(
        string baseConnectionString, IDataProtectionProvider dataProtection, CancellationToken ct = default)
    {
        await ExecuteAdminAsync(baseConnectionString, $"CREATE DATABASE \"{EmptyTemplateName}\"", ct);
        await using (var ctx = CreateContext(baseConnectionString, EmptyTemplateName))
        {
            await ctx.Database.MigrateAsync(ct);
        }
        await FlagAsTemplateAsync(baseConnectionString, EmptyTemplateName, ct);

        await ExecuteAdminAsync(
            baseConnectionString,
            $"CREATE DATABASE \"{GoldenTemplateName}\" TEMPLATE \"{EmptyTemplateName}\"", ct);
        await using (var ctx = CreateContext(baseConnectionString, GoldenTemplateName))
        {
            await GoldenDataset.LoadCanonicalAsync(ctx, dataProtection, ct);
        }
        await FlagAsTemplateAsync(baseConnectionString, GoldenTemplateName, ct);
    }

    /// <summary>Creates <paramref name="databaseName"/> as a copy of the chosen template (~50–150 ms).</summary>
    public static Task CloneAsync(
        string baseConnectionString, GoldenTemplate template, string databaseName, CancellationToken ct = default)
    {
        var source = template == GoldenTemplate.Golden ? GoldenTemplateName : EmptyTemplateName;
        return ExecuteAdminAsync(baseConnectionString, $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{source}\"", ct);
    }

    /// <summary>Terminates sessions on <paramref name="databaseName"/> and drops it if it exists.</summary>
    public static async Task DropAsync(string baseConnectionString, string databaseName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString(baseConnectionString));
        await conn.OpenAsync(ct);

        await using (var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1 AND pid <> pg_backend_pid()",
            conn))
        {
            terminate.Parameters.Add(new NpgsqlParameter { Value = databaseName });
            await terminate.ExecuteNonQueryAsync(ct);
        }

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", conn);
        await drop.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The base connection string retargeted at <paramref name="databaseName"/>.</summary>
    public static string ConnectionStringFor(string baseConnectionString, string databaseName)
        => new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString;

    private static AppDbContext CreateContext(string baseConnectionString, string databaseName)
    {
        var cs = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false }.ConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }

    private static string AdminConnectionString(string baseConnectionString)
        => new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;

    private static async Task ExecuteAdminAsync(string baseConnectionString, string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString(baseConnectionString));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Task FlagAsTemplateAsync(string baseConnectionString, string databaseName, CancellationToken ct)
        => ExecuteAdminAsync(baseConnectionString, $"UPDATE pg_database SET datistemplate = true WHERE datname = '{databaseName}'", ct);
}
