using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Tests the rotation claim algorithm's locking contract directly, through the internal entry point
/// the repositories use. The per-repository fixtures cover what the rotation dispenses; this covers
/// the guarantee that makes fetching the claimed row safe.
/// </summary>
[TestFixture]
public class RotationCycleClaimTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    /// <summary>
    /// The claim fetches its row inside the claiming transaction rather than after it commits, and
    /// this is what makes that safe: the claiming UPDATE holds a row-level exclusive lock until
    /// commit, so a concurrent delete of that row blocks instead of racing.
    ///
    /// Were the fetch moved back outside the transaction, a delete landing in the gap would hand
    /// the caller a null indistinguishable from "nothing claimable" while the row stayed stamped —
    /// burned for the rest of the cycle without ever being used.
    /// </summary>
    [Test]
    public async Task ClaimNextAsync_WhileTheClaimedRowIsBeingFetched_AConcurrentDeleteIsBlocked()
    {
        await SeedGifAsync("ban-gifs/locked.gif");

        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        PostgresException? deleteAttempt = null;

        var claimed = await RotationCycleClaim.ClaimNextAsync(
            context,
            RotationBag.BanCelebrationGifs,
            async (id, token) =>
            {
                // Runs inside the claim's transaction, standing in for the repository's fetch.
                deleteAttempt = await TryDeleteFromAnotherConnectionAsync(id, token);
                return await context.BanCelebrationGifs.FindAsync([id], token);
            },
            CancellationToken.None);

        Assert.That(deleteAttempt, Is.Not.Null,
            "the concurrent DELETE must not succeed while the claim holds the row");
        Assert.That(deleteAttempt!.SqlState, Is.EqualTo(PostgresErrorCodes.LockNotAvailable),
            "the DELETE must block on the claim's row lock rather than race it");
        Assert.That(claimed, Is.Not.Null, "the claim still returns the row it stamped");
    }

    /// <summary>
    /// Attempts to delete <paramref name="id"/> on a separate connection with a short lock timeout.
    /// Returns the timeout exception when the row is locked, or null if the delete went through.
    /// </summary>
    private async Task<PostgresException?> TryDeleteFromAnotherConnectionAsync(int id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_testHelper!.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var timeout = new NpgsqlCommand("SET lock_timeout = '750ms'", connection))
        {
            await timeout.ExecuteNonQueryAsync(ct);
        }

        await using var delete = new NpgsqlCommand(
            "DELETE FROM ban_celebration_gifs WHERE id = @id", connection);
        delete.Parameters.AddWithValue("id", id);

        try
        {
            await delete.ExecuteNonQueryAsync(ct);
            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }

    private async Task SeedGifAsync(string filePath)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        context.BanCelebrationGifs.Add(new BanCelebrationGifDto
        {
            FilePath = filePath,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }
}
