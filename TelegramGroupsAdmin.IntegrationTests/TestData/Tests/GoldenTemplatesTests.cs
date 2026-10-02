using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// GoldenTemplates clone/drop against the session templates PostgresFixture built.
/// </summary>
[TestFixture]
public class GoldenTemplatesTests
{
    [TestCase(GoldenTemplate.Golden, true)]
    [TestCase(GoldenTemplate.Empty, false)]
    public async Task CloneAsync_CopiesTheTemplate(GoldenTemplate template, bool expectUsers)
    {
        var name = $"tmpl_test_{Guid.NewGuid():N}";
        await GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, template, name);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(GoldenTemplates.ConnectionStringFor(PostgresFixture.BaseConnectionString, name))
                .Options;
            await using var ctx = new AppDbContext(options);

            var hasOwner = await ctx.Users.AnyAsync(u => u.Id == GoldenDatasetConstants.WebUsers.OwnerId);

            Assert.That(hasOwner, Is.EqualTo(expectUsers));
        }
        finally
        {
            await GoldenTemplates.DropAsync(PostgresFixture.BaseConnectionString, name);
        }
    }

    [Test]
    public async Task DropAsync_RemovesTheDatabase()
    {
        var name = $"tmpl_test_{Guid.NewGuid():N}";
        await GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, GoldenTemplate.Empty, name);

        await GoldenTemplates.DropAsync(PostgresFixture.BaseConnectionString, name);

        await using var admin = new Npgsql.NpgsqlConnection(
            GoldenTemplates.ConnectionStringFor(PostgresFixture.BaseConnectionString, "postgres"));
        await admin.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = $1", admin);
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = name });
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.Zero);
    }
}
