using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Services.Hashing;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

/// <summary>
/// Integration tests for <see cref="PhotoHashRehashService"/> against real PostgreSQL
/// (Testcontainers). Covers the four <c>photo_hash</c> byte-array/base64 stores the v1-to-v2
/// hash migration cleared: telegram_users, linked_channels, ban_celebration_gifs and
/// image_training_samples (via its message join). Video keyframe re-extraction is explicitly
/// out of scope (tracked as a follow-up issue).
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

    private async Task<long> SeedImageTrainingSampleAsync(string mediaLocalPath, byte[]? photoHash)
    {
        var messageId = Interlocked.Increment(ref _nextMessageId);
        const long chatId = -100_012_345_678_901L;
        var now = DateTimeOffset.UtcNow;

        await using var context = NewContext();
        context.Messages.Add(new MessageRecordDto
        {
            MessageId = messageId,
            ChatId = chatId,
            UserId = 1,
            Timestamp = now,
            MediaLocalPath = mediaLocalPath,
        });
        await context.SaveChangesAsync();

        var sample = new ImageTrainingSampleDto
        {
            MessageId = messageId,
            ChatId = chatId,
            // [Required] on the model, but production code never assigns it either — the
            // real source path comes from the joined message's MediaLocalPath.
            PhotoPath = string.Empty,
            PhotoHash = photoHash,
            FileSizeBytes = 1,
            Width = 1,
            Height = 1,
            IsSpam = false,
            MarkedBySystemIdentifier = "integration-test",
            MarkedAt = now,
        };
        context.ImageTrainingSamples.Add(sample);
        await context.SaveChangesAsync();

        return sample.Id;
    }

    private async Task<ImageTrainingSampleDto> ReloadSampleAsync(long id)
    {
        await using var context = NewContext();
        return await context.ImageTrainingSamples.AsNoTracking().SingleAsync(s => s.Id == id);
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

    [Test]
    public async Task RehashAsync_TrainingSampleWithLiveMessageMedia_RecomputesHash()
    {
        var sampleId = await SeedImageTrainingSampleAsync("full/1/photo.jpg", photoHash: null);
        WriteTestImage(Path.Combine(_dataPath, "media", "full", "1", "photo.jpg"));

        await _service!.RehashAsync();

        var reloaded = await ReloadSampleAsync(sampleId);
        Assert.That(reloaded.PhotoHash, Is.Not.Null);
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
}
