using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SkiaSharp;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Hashing;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

/// <summary>
/// Integration tests for <see cref="PhotoHashRehashService"/> against real PostgreSQL
/// (Testcontainers). Covers the three <c>photo_hash</c> byte-array/base64 stores the v1-to-v2
/// hash migration cleared (telegram_users, linked_channels, ban_celebration_gifs) and the
/// messages.media_features backfill for curated media messages. Infrastructure fixture on the
/// empty template: it seeds its own rows.
/// </summary>
[TestFixture]
public class PhotoHashRehashServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IPhotoHashRehashService? _service;
    private string _dataPath = null!;

    private static long _nextUserId = 9_500_000_000_000L;
    private static int _nextMessageId = 500_000;
    private static long _nextManagedChatId = -100_900_000_000_000L;
    private static int _nextLinkedChannelId = 1;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), $"PhotoHashRehashTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataPath);

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.Configure<AppOptions>(opt => opt.DataPath = _dataPath);

        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IPhotoHashService, PhotoHashService>();
        services.AddCoreServices(); // SimHashService for MessageHistoryRepository
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddSingleton(Substitute.For<IVideoFrameExtractionService>());
        services.AddSingleton<IMediaFeatureExtractor, MediaFeatureExtractor>();
        services.AddScoped<IPhotoHashRehashService, PhotoHashRehashService>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _service = _scope.ServiceProvider.GetRequiredService<IPhotoHashRehashService>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();

        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    /// <summary>
    /// Writes a decodable JPEG fixture, mirroring the SkiaSharp fixture pattern used by
    /// ThumbnailServiceTests: a solid background with a filled circle, so the perceptual
    /// hash's box-average has real contrast to work with.
    /// </summary>
    private static void WriteTestImage(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
            canvas.DrawCircle(16f, 16f, 10f, paint);
        }

        using var fs = File.Create(path);
        bitmap.Encode(fs, SKEncodedImageFormat.Jpeg, 90);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_testHelper!.ConnectionString).Options);

    private async Task<long> SeedUserAsync(string photoPath, string? photoHash, DateTimeOffset? bannedAt)
    {
        var telegramUserId = Interlocked.Increment(ref _nextUserId);
        var now = DateTimeOffset.UtcNow;

        await using var context = NewContext();
        context.TelegramUsers.Add(new TelegramUserDto
        {
            TelegramUserId = telegramUserId,
            UserPhotoPath = photoPath,
            PhotoHash = photoHash,
            BannedAt = bannedAt,
            IsBanned = bannedAt is not null,
            FirstSeenAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();

        return telegramUserId;
    }

    private async Task<TelegramUserDto> ReloadUserAsync(long telegramUserId)
    {
        await using var context = NewContext();
        return await context.TelegramUsers.AsNoTracking()
            .SingleAsync(u => u.TelegramUserId == telegramUserId);
    }

    /// <summary>
    /// Seeds a message with an admin spam decision (curated verdict, no media features), as a
    /// never-scanned media message looks after an admin marks it spam.
    /// </summary>
    private async Task<(int MessageId, long ChatId)> SeedCuratedPhotoMessageAsync(string? photoLocalPath, string? mediaLocalPath = null)
    {
        var messageId = Interlocked.Increment(ref _nextMessageId);
        const long chatId = -100_900_000_000_001L;

        await using var ctx = NewContext();
        ctx.Messages.Add(new MessageRecordDto
        {
            MessageId = messageId, ChatId = chatId, UserId = 0, Timestamp = DateTimeOffset.UtcNow,
            PhotoFileId = "f", PhotoLocalPath = photoLocalPath, MediaLocalPath = mediaLocalPath
        });
        ctx.DetectionResults.Add(new DetectionResultRecordDto
        {
            MessageId = messageId, ChatId = chatId, DetectedAt = DateTimeOffset.UtcNow,
            Source = (int)VerdictSource.WebMarkSpam, Classification = (int)VerdictClassification.ExplicitSpam,
            DetectionMethod = "WebMarkSpam", Score = 5, Reason = "test", SystemIdentifier = "integration-test"
        });
        await ctx.SaveChangesAsync();
        return (messageId, chatId);
    }

    private async Task<MediaFeaturesDto?> ReloadMediaFeaturesAsync(int messageId, long chatId)
    {
        await using var ctx = NewContext();
        return (await ctx.Messages.AsNoTracking().SingleAsync(m => m.MessageId == messageId && m.ChatId == chatId)).MediaFeatures;
    }

    private async Task<int> SeedLinkedChannelAsync(string channelIconPath, byte[]? photoHash)
    {
        var managedChatId = Interlocked.Decrement(ref _nextManagedChatId);
        var now = DateTimeOffset.UtcNow;

        await using var context = NewContext();
        context.ManagedChats.Add(new ManagedChatRecordDto
        {
            ChatId = managedChatId,
            ChatName = "Test Chat",
            ChatType = ManagedChatType.Supergroup,
            BotStatus = BotChatStatus.Administrator,
            IsAdmin = true,
            AddedAt = now,
            IsActive = true,
            IsDeleted = false,
        });
        await context.SaveChangesAsync();

        var linkedChannel = new LinkedChannelRecordDto
        {
            ManagedChatId = managedChatId,
            ChannelId = Interlocked.Increment(ref _nextLinkedChannelId),
            ChannelName = "Test Channel",
            ChannelIconPath = channelIconPath,
            PhotoHash = photoHash,
            LastSynced = now,
        };
        context.LinkedChannels.Add(linkedChannel);
        await context.SaveChangesAsync();

        return linkedChannel.Id;
    }

    private async Task<LinkedChannelRecordDto> ReloadLinkedChannelAsync(int id)
    {
        await using var context = NewContext();
        return await context.LinkedChannels.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task<int> SeedBanCelebrationGifAsync(string filePath, byte[]? photoHash)
    {
        await using var context = NewContext();
        var gif = new BanCelebrationGifDto
        {
            FilePath = filePath,
            PhotoHash = photoHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.BanCelebrationGifs.Add(gif);
        await context.SaveChangesAsync();

        return gif.Id;
    }

    private async Task<BanCelebrationGifDto> ReloadBanCelebrationGifAsync(int id)
    {
        await using var context = NewContext();
        return await context.BanCelebrationGifs.AsNoTracking().SingleAsync(g => g.Id == id);
    }

    [Test]
    public async Task RehashAsync_UserPhotoOnDisk_RecomputesHash()
    {
        var userId = await SeedUserAsync("user_photos/1.jpg", photoHash: null, bannedAt: null);
        WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "1.jpg"));

        var result = await _service!.RehashAsync();

        var reloaded = await ReloadUserAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PhotoHash, Is.Not.Null);
            // telegram_users.photo_hash is a Base64 string column, not bytea — matching the
            // convention FetchUserPhotoJob already uses when it first populates this column.
            Assert.That(Convert.FromBase64String(reloaded.PhotoHash!), Has.Length.EqualTo(8));
            Assert.That(result.Recomputed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RehashAsync_UserPhotoMissingFromDisk_LeavesHashNull()
    {
        var userId = await SeedUserAsync("user_photos/2.jpg", photoHash: null, bannedAt: null);
        // Deliberately do not write the file.

        var result = await _service!.RehashAsync();

        var reloaded = await ReloadUserAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PhotoHash, Is.Null);
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RehashAsync_BannedUser_LeavesHashNullEvenWithPhotoOnDisk()
    {
        var userId = await SeedUserAsync("user_photos/3.jpg", photoHash: null, bannedAt: DateTimeOffset.UtcNow);
        WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "3.jpg"));

        var result = await _service!.RehashAsync();

        var reloaded = await ReloadUserAsync(userId);
        // The photo on disk was blurred in place by profile-scan censoring, so hashing
        // it would store a hash of the blur, not of the user's real photo.
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PhotoHash, Is.Null);
            Assert.That(result.Skipped, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RehashAsync_RunTwice_IsIdempotent()
    {
        await SeedUserAsync("user_photos/4.jpg", photoHash: null, bannedAt: null);
        WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "4.jpg"));

        var first = await _service!.RehashAsync();
        var second = await _service.RehashAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Recomputed, Is.EqualTo(1));
            // The row now has a hash, so the IS NULL predicate skips it entirely.
            Assert.That(second.Recomputed, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Review focus: an admin marks a never-scanned media message spam; the next startup rehash
    /// fills its media_features, so Layer 1 can match it.
    /// </summary>
    [Test]
    public async Task RehashAsync_CuratedPhotoMessageWithoutFeatures_GetsPhotoFeatures()
    {
        var relative = $"full/-100900000000001/{Interlocked.Increment(ref _nextMessageId)}.jpg";
        WriteTestImage(Path.Combine(_dataPath, "media", relative));
        var (messageId, chatId) = await SeedCuratedPhotoMessageAsync(relative);

        var result = await _service!.RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ReloadMediaFeaturesAsync(messageId, chatId),
                Is.TypeOf<PhotoFeaturesDto>().With.Property(nameof(PhotoFeaturesDto.Hash)).Length.EqualTo(8));
            Assert.That(result.Recomputed, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The TrainingHandler defensive download stores a photo under media_local_path (media/photo/),
    /// not photo_local_path; the backfill must still hash it.
    /// </summary>
    [Test]
    public async Task RehashAsync_CuratedPhotoDownloadedToMediaLocalPath_GetsPhotoFeatures()
    {
        var fileName = $"photo_{Interlocked.Increment(ref _nextMessageId)}_uid.jpg";
        WriteTestImage(Path.Combine(_dataPath, "media", "photo", fileName));
        var (messageId, chatId) = await SeedCuratedPhotoMessageAsync(photoLocalPath: null, mediaLocalPath: fileName);

        await _service!.RehashAsync();

        Assert.That(await ReloadMediaFeaturesAsync(messageId, chatId), Is.TypeOf<PhotoFeaturesDto>());
    }

    [Test]
    public async Task RehashAsync_CuratedPhotoMessageFileMissing_LeavesFeaturesNull()
    {
        // Deliberately do not write the file.
        var (messageId, chatId) = await SeedCuratedPhotoMessageAsync("full/-100900000000001/missing.jpg");

        var result = await _service!.RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ReloadMediaFeaturesAsync(messageId, chatId), Is.Null);
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A file that stays missing is retried every start and sorts ahead of older decisions. With a
    /// batch of one, the backfill must page past it rather than spend the batch on it.
    /// </summary>
    [Test]
    public async Task RehashAsync_MissingFileAheadOfValidCandidate_DoesNotBlockIt()
    {
        var relative = $"full/-100900000000001/{Interlocked.Increment(ref _nextMessageId)}.jpg";
        WriteTestImage(Path.Combine(_dataPath, "media", relative));
        var valid = await SeedCuratedPhotoMessageAsync(relative);
        // Seeded second, so its decision is newer and it comes first.
        var missing = await SeedCuratedPhotoMessageAsync("full/-100900000000001/gone.jpg");

        var result = await CreateService(MediaFeatureExtractor(), mediaBackfillLimit: 1).RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ReloadMediaFeaturesAsync(valid.MessageId, valid.ChatId), Is.TypeOf<PhotoFeaturesDto>());
            Assert.That(await ReloadMediaFeaturesAsync(missing.MessageId, missing.ChatId), Is.Null);
            Assert.That(result.Recomputed, Is.EqualTo(1));
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// An exception for one candidate (extractor or write) is counted and must not abort the backfill.
    /// </summary>
    [Test]
    public async Task RehashAsync_CandidateThrows_OtherCandidatesStillGetFeatures()
    {
        var goodRelative = $"full/-100900000000001/{Interlocked.Increment(ref _nextMessageId)}.jpg";
        var badRelative = $"full/-100900000000001/{Interlocked.Increment(ref _nextMessageId)}.jpg";
        WriteTestImage(Path.Combine(_dataPath, "media", goodRelative));
        WriteTestImage(Path.Combine(_dataPath, "media", badRelative));
        var good = await SeedCuratedPhotoMessageAsync(goodRelative);
        var bad = await SeedCuratedPhotoMessageAsync(badRelative); // newer: processed first

        var real = MediaFeatureExtractor();
        var extractor = Substitute.For<IMediaFeatureExtractor>();
        extractor.ExtractPhotoAsync(Arg.Any<string>())
            .Returns(call => call.Arg<string>().EndsWith(badRelative, StringComparison.Ordinal)
                ? throw new IOException("disk error")
                : real.ExtractPhotoAsync(call.Arg<string>()));

        var result = await CreateService(extractor).RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ReloadMediaFeaturesAsync(good.MessageId, good.ChatId), Is.TypeOf<PhotoFeaturesDto>());
            Assert.That(await ReloadMediaFeaturesAsync(bad.MessageId, bad.ChatId), Is.Null);
            Assert.That(result.Recomputed, Is.EqualTo(1));
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }

    private IMediaFeatureExtractor MediaFeatureExtractor() =>
        _scope!.ServiceProvider.GetRequiredService<IMediaFeatureExtractor>();

    private PhotoHashRehashService CreateService(IMediaFeatureExtractor extractor, int mediaBackfillLimit = 500)
    {
        var sp = _scope!.ServiceProvider;
        return new PhotoHashRehashService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            sp.GetRequiredService<IPhotoHashService>(),
            sp.GetRequiredService<IMessageHistoryRepository>(),
            extractor,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>(),
            sp.GetRequiredService<ILogger<PhotoHashRehashService>>())
        {
            MediaBackfillLimit = mediaBackfillLimit
        };
    }

    [Test]
    public async Task RehashAsync_LinkedChannelIconOnDisk_RecomputesHash()
    {
        var channelId = await SeedLinkedChannelAsync("channel_icons/1.jpg", photoHash: null);
        WriteTestImage(Path.Combine(_dataPath, "media", "channel_icons", "1.jpg"));

        var result = await _service!.RehashAsync();

        var reloaded = await ReloadLinkedChannelAsync(channelId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PhotoHash, Is.Not.Null);
            // linked_channels.photo_hash is bytea — an accidental Base64-string write here
            // (mixing up the users convention with this store) would fail this length check.
            Assert.That(reloaded.PhotoHash!, Has.Length.EqualTo(8));
            Assert.That(result.Recomputed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RehashAsync_BanCelebrationGifOnDisk_RecomputesHash()
    {
        var gifId = await SeedBanCelebrationGifAsync("ban-gifs/1.jpg", photoHash: null);
        WriteTestImage(Path.Combine(_dataPath, "media", "ban-gifs", "1.jpg"));

        var result = await _service!.RehashAsync();

        var reloaded = await ReloadBanCelebrationGifAsync(gifId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PhotoHash, Is.Not.Null);
            // ban_celebration_gifs.photo_hash is bytea, and a downstream query filters on
            // exactly 8 bytes (PhotoHashByteCount) — a wrong-length write is silently
            // excluded from spam matching rather than throwing, so this assertion is the
            // one that actually catches a copy-paste type mismatch.
            Assert.That(reloaded.PhotoHash!, Has.Length.EqualTo(8));
            Assert.That(result.Recomputed, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// One unreadable image must not cost the rest of the batch its hashes. Every prior test
    /// seeds a single candidate, so a decode failure that aborted the enclosing loop would
    /// pass all of them and still lose the whole corpus in production.
    /// </summary>
    [Test]
    public async Task RehashAsync_UndecodableRowMidBatch_StillRehashesTheRest()
    {
        var before = await SeedUserAsync("user_photos/mid_a.jpg", photoHash: null, bannedAt: null);
        var broken = await SeedUserAsync("user_photos/mid_b.jpg", photoHash: null, bannedAt: null);
        var after = await SeedUserAsync("user_photos/mid_c.jpg", photoHash: null, bannedAt: null);

        WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "mid_a.jpg"));
        WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "mid_c.jpg"));
        // Present on disk and non-empty, but not an image: this exercises the decode
        // failure path rather than the missing-file path.
        await File.WriteAllTextAsync(
            Path.Combine(_dataPath, "media", "user_photos", "mid_b.jpg"),
            "not an image");

        var result = await _service!.RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await ReloadUserAsync(before)).PhotoHash, Is.Not.Null, "row before the failure");
            Assert.That((await ReloadUserAsync(broken)).PhotoHash, Is.Null, "the undecodable row");
            Assert.That((await ReloadUserAsync(after)).PhotoHash, Is.Not.Null, "row after the failure");
            Assert.That(result.Recomputed, Is.EqualTo(2));
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A truncated photo decodes into a mostly-black bitmap rather than failing, so without
    /// an explicit completeness check the rehash would store a hash of the truncation — one
    /// that any other truncated photo would also match.
    /// </summary>
    [Test]
    public async Task RehashAsync_TruncatedPhoto_LeavesHashNull()
    {
        var userId = await SeedUserAsync("user_photos/truncated.jpg", photoHash: null, bannedAt: null);

        var path = Path.Combine(_dataPath, "media", "user_photos", "truncated.jpg");
        WriteTestImage(path);
        var full = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, full[..(full.Length / 2)]);

        var result = await _service!.RehashAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await ReloadUserAsync(userId)).PhotoHash, Is.Null);
            Assert.That(result.Unrecoverable, Is.EqualTo(1));
        });
    }
}
