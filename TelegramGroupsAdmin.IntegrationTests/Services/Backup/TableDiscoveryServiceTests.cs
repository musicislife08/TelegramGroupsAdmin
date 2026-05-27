using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Backup;

[TestFixture]
public class TableDiscoveryServiceTests
{
    private static IReadOnlyList<Type> GetDtoTypes() =>
        typeof(AppDbContext).Assembly.GetTypes()
            .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
            .Where(t => t.Name.EndsWith("Dto") && (t.IsClass || t.IsValueType))
            .ToList();

    [Test]
    public void EveryTableBackedDtoHasTableAttribute()
    {
        // DTOs that are intentionally not backed by a regular table:
        //  - InviteWithCreatorDto: join projection
        //  - RawAlgorithmPerformanceStatsDto: keyless, configured for SqlQuery
        var expectedNonTableBacked = new HashSet<string>
        {
            "InviteWithCreatorDto",
            "RawAlgorithmPerformanceStatsDto",
        };

        var missingTableAttr = GetDtoTypes()
            .Where(t => !expectedNonTableBacked.Contains(t.Name))
            .Where(t => t.GetCustomAttribute<TableAttribute>() is null)
            .Select(t => t.Name)
            .ToList();

        Assert.That(missingTableAttr, Is.Empty,
            $"These DTOs need [Table(\"...\")] (or add to expectedNonTableBacked if intentional): " +
            string.Join(", ", missingTableAttr));
    }

    [Test]
    public void FindDtoForTable_ResolvesUsernameBlacklist_ToUsernameBlacklistEntryDto()
    {
        var result = TableDiscoveryService.FindDtoForTable("username_blacklist", GetDtoTypes());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("UsernameBlacklistEntryDto"));
    }

    [Test]
    public void FindDtoForTable_ReturnsNullForUnmatchedTable()
    {
        var result = TableDiscoveryService.FindDtoForTable("qrtz_locks", GetDtoTypes());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindDtoForTable_IsCaseInsensitive()
    {
        var result = TableDiscoveryService.FindDtoForTable("USERNAME_BLACKLIST", GetDtoTypes());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("UsernameBlacklistEntryDto"));
    }

    [Test]
    public async Task DiscoverTablesAsync_IncludesUsernameBlacklist()
    {
        using var testHelper = new MigrationTestHelper();
        await testHelper.CreateDatabaseFromGoldenTemplateAsync();

        await using var connection = new NpgsqlConnection(testHelper.ConnectionString);
        await connection.OpenAsync();

        var logger = Substitute.For<ILogger<TableDiscoveryService>>();
        var service = new TableDiscoveryService(logger);

        var mapping = await service.DiscoverTablesAsync(connection);

        Assert.That(mapping.ContainsKey("username_blacklist"), Is.True,
            $"Expected username_blacklist in mapping. Actual keys: {string.Join(", ", mapping.Keys.OrderBy(k => k))}");
        Assert.That(mapping["username_blacklist"].Name, Is.EqualTo("UsernameBlacklistEntryDto"));
    }
}
