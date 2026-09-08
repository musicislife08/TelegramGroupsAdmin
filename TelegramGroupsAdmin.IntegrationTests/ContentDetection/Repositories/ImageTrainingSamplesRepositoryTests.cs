using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Integration tests for <see cref="ImageTrainingSamplesRepository.GetRecentSamplesAsync"/> against
/// real PostgreSQL (Testcontainers).
/// </summary>
/// <remarks>
/// This fixture exists specifically to prove the NULL/length hash filter translates to SQL and
/// executes server-side, rather than throwing an EF translation error or silently falling back to
/// client-side evaluation (which would materialize the whole table). It also covers the exact
/// regression a reviewer flagged for the v1-hash-clearing migration: a NULL hash (source image
/// gone) and a zero-length hash (what the migration's <c>Down</c> backfills a NULL column with
/// before restoring NOT NULL) must both be excluded, because
/// <see cref="IPhotoHashService.CompareHashes"/> throws on anything but an exactly
/// <see cref="TelegramGroupsAdmin.Core.HashingConstants.PhotoHashByteCount"/>-byte array.
/// </remarks>
[TestFixture]
public class ImageTrainingSamplesRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IImageTrainingSamplesRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton(Substitute.For<IPhotoHashService>());
        services.AddScoped<IImageTrainingSamplesRepository, ImageTrainingSamplesRepository>();

        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IImageTrainingSamplesRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    /// <summary>
    /// Inserts a message row (satisfies the FK on image_training_samples) and a training sample
    /// row with the given hash, bypassing the DTO's [Required]/non-null C# shape via raw SQL so a
    /// NULL or zero-length hash can be persisted even though the CLR model now forbids
    /// constructing one directly.
    /// </summary>
    private async Task SeedSampleAsync(int messageId, long chatId, byte[]? photoHash, bool isSpam, DateTimeOffset markedAt)
    {
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_testHelper!.ConnectionString).Options);

        context.Messages.Add(new MessageRecordDto
        {
            MessageId = messageId,
            ChatId = chatId,
            UserId = 1,
            Timestamp = markedAt
        });
        await context.SaveChangesAsync();

        // Raw SQL: the EF model's PhotoHash setter is byte[]? but nothing stops a valid-length
        // hash from being the only value ever assigned through the DbSet in production. To seed a
        // NULL or a zero-length array deterministically (mirroring what the migration and its
        // rollback can leave behind), write directly.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO image_training_samples
                (message_id, chat_id, photo_path, photo_hash, file_size_bytes, width, height,
                 is_spam, marked_by_system_identifier, marked_at)
            VALUES
                ({messageId}, {chatId}, {"test.jpg"}, {photoHash}, 1, 1, 1,
                 {isSpam}, {"integration-test"}, {markedAt})
            """);
    }

    [Test]
    public async Task GetRecentSamplesAsync_ExcludesNullAndWrongLengthHashes_ServerSide()
    {
        // Arrange: one NULL hash (source image gone), one zero-length hash (what the
        // ClearV1PhotoHashes migration's Down leaves a previously-NULL row with), and one
        // valid 8-byte hash.
        var markedAt = DateTimeOffset.UtcNow;

        await SeedSampleAsync(1001, -1001L, photoHash: null, isSpam: true, markedAt);
        await SeedSampleAsync(1002, -1001L, photoHash: [], isSpam: true, markedAt.AddSeconds(1));
        var validHash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await SeedSampleAsync(1003, -1001L, photoHash: validHash, isSpam: false, markedAt.AddSeconds(2));

        // Act: this must execute as a server-side WHERE, not throw a LINQ translation error and
        // not fall back to client evaluation of the whole table.
        var result = await _repository!.GetRecentSamplesAsync(limit: 1000, CancellationToken.None);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].PhotoHash, Is.EqualTo(validHash));
        Assert.That(result[0].IsSpam, Is.False);

        // Prove the filter is a server-side WHERE, not a client-evaluated fallback that would
        // materialize the whole table before filtering: confirmed via `dotnet test` against real
        // Postgres, EF renders `WHERE i.photo_hash IS NOT NULL AND length(i.photo_hash) = 8`
        // (Npgsql translates byte[].Length to the bytea length() function).
        await using var probeContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_testHelper!.ConnectionString).Options);
        var sql = probeContext.ImageTrainingSamples
            .Where(its => its.PhotoHash != null && its.PhotoHash.Length == TelegramGroupsAdmin.Core.HashingConstants.PhotoHashByteCount)
            .ToQueryString();
        Assert.That(sql, Does.Contain("photo_hash IS NOT NULL"));
        Assert.That(sql, Does.Contain("length(i.photo_hash) = 8"));
    }
}
