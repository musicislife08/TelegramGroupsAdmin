using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for BanCelebrationService.
/// Tests config handling, GIF/caption retrieval, placeholder replacement, and file caching.
/// Uses real BanCelebrationCache for shuffle-bag algorithm testing,
/// IBotMessageService and IBotDmService for message/DM operations.
/// </summary>
[TestFixture]
public class BanCelebrationServiceTests
{
    private IConfigService _mockConfigService = null!;
    private BanCelebrationCache _celebrationCache = null!; // Real cache for shuffle-bag testing
    private IBanCelebrationGifRepository _mockGifRepository = null!;
    private IBanCelebrationCaptionRepository _mockCaptionRepository = null!;
    private IBotMessageService _mockMessageService = null!;
    private IBotDmService _mockDmService = null!;
    private IUserActionsRepository _mockUserActionsRepository = null!;
    private IOptions<MessageHistoryOptions> _mockHistoryOptions = null!;
    private ILogger<BanCelebrationService> _mockLogger = null!;
    private BanCelebrationService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfigService = Substitute.For<IConfigService>();
        _celebrationCache = new BanCelebrationCache(); // Real cache - tests shuffle-bag algorithm
        _mockGifRepository = Substitute.For<IBanCelebrationGifRepository>();
        _mockCaptionRepository = Substitute.For<IBanCelebrationCaptionRepository>();
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockUserActionsRepository = Substitute.For<IUserActionsRepository>();
        _mockLogger = Substitute.For<ILogger<BanCelebrationService>>();

        // Setup MessageHistoryOptions
        var historyOptions = new MessageHistoryOptions { ImageStoragePath = "/data" };
        _mockHistoryOptions = Options.Create(historyOptions);

        // Default config setup - enabled celebration
        var defaultConfig = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = true,
            SendToBannedUser = false
        };
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .Returns(defaultConfig);

        // Default user actions repository returns 0 bans
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        _sut = new BanCelebrationService(
            _mockConfigService,
            _celebrationCache, // Real cache
            _mockGifRepository,
            _mockCaptionRepository,
            _mockMessageService,
            _mockDmService,
            _mockUserActionsRepository,
            _mockHistoryOptions,
            _mockLogger);
    }

    /// <summary>
    /// Helper to setup SendAndSaveAnimationAsync to return a Message with Animation.
    /// Uses direct object initialization (Telegram.Bot types have settable properties).
    /// </summary>
    private void SetupSuccessfulSendAnimation(string fileId = "cached_file_id")
    {
        var message = new Message
        {
            Animation = new Animation { FileId = fileId }
        };
        _mockMessageService.SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode>(),
            Arg.Any<CancellationToken>())
            .Returns(message);
    }

    /// <summary>
    /// Helper to setup GIF and caption repositories with test data.
    /// </summary>
    private void SetupRepositoriesWithGifsAndCaptions(int gifCount, int captionCount)
    {
        var gifIds = Enumerable.Range(1, gifCount).ToList();
        var captionIds = Enumerable.Range(1, captionCount).ToList();

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(captionIds);

        // Setup each GIF to return valid data
        foreach (var id in gifIds)
        {
            var gif = new BanCelebrationGif { Id = id, FilePath = $"ban-gifs/{id}.gif", FileId = $"file{id}" };
            _mockGifRepository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(gif);
        }

        // Setup each caption to return valid data
        foreach (var id in captionIds)
        {
            var caption = new BanCelebrationCaption { Id = id, Text = $"Caption {id}", DmText = $"DM {id}" };
            _mockCaptionRepository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(caption);
        }
    }

    #region Shuffle-Bag Algorithm Tests (using Real Cache)

    [Test]
    public async Task SendBanCelebration_WithThreeGifs_ShowsAllBeforeAnyRepeat()
    {
        // Arrange - 3 GIFs in repository, real cache will shuffle them
        SetupRepositoriesWithGifsAndCaptions(gifCount: 3, captionCount: 1);
        SetupSuccessfulSendAnimation();

        // Track which GIF IDs are used
        var usedGifIds = new List<int>();
        _mockGifRepository.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = callInfo.Arg<int>();
                usedGifIds.Add(id);
                return new BanCelebrationGif { Id = id, FilePath = $"ban-gifs/{id}.gif", FileId = $"file{id}" };
            });

        // Act - Call 3 times (one full bag cycle)
        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);
            Assert.That(result, Is.True, $"Celebration {i + 1} should succeed");
        }

        // Assert - Each GIF should be used exactly once (shuffle-bag guarantee)
        Assert.That(usedGifIds, Has.Count.EqualTo(3), "Should have used 3 GIFs");
        Assert.That(usedGifIds.Distinct().Count(), Is.EqualTo(3), "All 3 GIFs should be unique (no repeats)");
        Assert.That(usedGifIds, Is.EquivalentTo(new[] { 1, 2, 3 }), "Should contain all GIF IDs 1, 2, 3");
    }

    [Test]
    public async Task SendBanCelebration_AfterBagExhaustion_ReshufflesFromDatabase()
    {
        // Arrange - 2 GIFs in repository
        SetupRepositoriesWithGifsAndCaptions(gifCount: 2, captionCount: 1);
        SetupSuccessfulSendAnimation();

        // Act - Call 3 times (exhausts bag of 2 + starts new bag)
        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);
            Assert.That(result, Is.True, $"Celebration {i + 1} should succeed");
        }

        // Assert - GetAllIdsAsync should be called twice (initial load + reshuffle after exhaustion)
        await _mockGifRepository.Received(2).GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenCacheReturnsGifId_FetchesGifFromRepository()
    {
        // Arrange
        SetupRepositoriesWithGifsAndCaptions(gifCount: 1, captionCount: 1);
        SetupSuccessfulSendAnimation();

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Test Chat"), new UserIdentity(456, "BadUser", null, null), true);

        // Assert
        Assert.That(result, Is.True);
        await _mockGifRepository.Received(1).GetByIdAsync(1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenCacheBagEmpty_RepopulatesFromRepository()
    {
        // Arrange - Real cache starts empty, will trigger repopulation
        SetupRepositoriesWithGifsAndCaptions(gifCount: 3, captionCount: 1);
        SetupSuccessfulSendAnimation();

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.True);
        // Real cache starts empty, so GetAllIdsAsync must be called to populate it
        await _mockGifRepository.Received(1).GetAllIdsAsync(Arg.Any<CancellationToken>());
        await _mockCaptionRepository.Received(1).GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenGifDeletedFromBag_SkipsToNextGif()
    {
        // Arrange - 3 GIF IDs in repo, but GIF #2 returns null (deleted from DB after bag was populated)
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3 });
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });

        // GIF 2 was deleted from DB (returns null), GIFs 1 and 3 exist
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" });
        _mockGifRepository.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((BanCelebrationGif?)null);
        _mockGifRepository.GetByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "file3" });

        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" });

        SetupSuccessfulSendAnimation();

        // Act - Call twice, one of the calls will hit the deleted GIF
        var result1 = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);
        var result2 = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Both should succeed (deleted GIF skipped gracefully)
        Assert.That(result1, Is.True, "First celebration should succeed");
        Assert.That(result2, Is.True, "Second celebration should succeed (deleted GIF skipped)");
    }

    [Test]
    public async Task SendBanCelebration_WhenNoGifsExist_ReturnsFalse()
    {
        // Arrange - Repository returns no GIF IDs
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int>());

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenNoCaptionsExist_ReturnsFalse()
    {
        // Arrange - GIFs exist but no captions
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" });
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int>());

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenCaptionDeletedFromBag_SkipsToNextCaption()
    {
        // Arrange - 3 caption IDs in repo, but caption #2 returns null (deleted)
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" });

        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "Caption 1", DmText = "DM 1" });
        _mockCaptionRepository.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((BanCelebrationCaption?)null);
        _mockCaptionRepository.GetByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 3, Text = "Caption 3", DmText = "DM 3" });

        SetupSuccessfulSendAnimation();

        // Act - Call twice, one will hit the deleted caption
        var result1 = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);
        var result2 = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Both should succeed (deleted caption skipped)
        Assert.That(result1, Is.True);
        Assert.That(result2, Is.True);
    }

    [Test]
    public async Task SendBanCelebration_WhenAllGifsDeletedMidBag_ReshufflesToEmptyAndReturnsFalse()
    {
        // Arrange - GIF IDs in repo on first call, but GIFs were deleted between
        // when IDs were loaded and when they're fetched. On reshuffle, DB returns empty.
        var callCount = 0;
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                // First call: IDs exist in DB
                // Second call (reshuffle after all deleted): DB now returns empty
                return callCount == 1 ? new List<int> { 1, 2 } : new List<int>();
            });

        // All GIFs return null when fetched (deleted between GetAllIds and GetById)
        _mockGifRepository.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((BanCelebrationGif?)null);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Should return false (no valid GIFs found after exhausting bag and reshuffling)
        Assert.That(result, Is.False);
    }

    #endregion

    #region Configuration Tests

    [Test]
    public async Task SendBanCelebration_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var disabledConfig = new BanCelebrationConfig { Enabled = false };
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .Returns(disabledConfig);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);

        // Verify repository was never consulted (short-circuited at config check)
        await _mockGifRepository.DidNotReceive().GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenAutoBanDisabled_AndIsAutoBan_ReturnsFalse()
    {
        // Arrange
        var config = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = false,
            TriggerOnManualBan = true
        };
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .Returns(config);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenManualBanDisabled_AndIsManualBan_ReturnsFalse()
    {
        // Arrange
        var config = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = false
        };
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .Returns(config);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenConfigIsNull_UsesDefaultConfigWhichIsDisabled()
    {
        // Arrange - Return null config (service falls back to BanCelebrationConfig.Default)
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .Returns((BanCelebrationConfig?)null);

        // Act - Default config has Enabled=false (feature is opt-in)
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: true);

        // Assert - Should return false because default config has Enabled=false
        Assert.That(result, Is.False);

        // Verify repository was never consulted (short-circuited at config check)
        await _mockGifRepository.DidNotReceive().GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Placeholder Replacement Tests

    [Test]
    public async Task SendBanCelebration_ReplacesPlaceholdersInCaption()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "{username} banned from {chatname}! Ban #{bancount}",
            DmText = "You were banned"
        };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>()).Returns(42);

        SetupSuccessfulSendAnimation();

        // Act
        await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Test Group"), new UserIdentity(456, "SpammerBob", null, null), true);

        // Assert - Verify the caption sent to Telegram has placeholders replaced
        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            123,
            Arg.Any<InputFile>(),
            "SpammerBob banned from Test Group! Ban #42",
            ParseMode.Markdown,
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ban Count Tests

    [Test]
    public async Task SendBanCelebration_WhenBanCountQueryFails_UsesZeroAsFallback()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "Ban #{bancount}!",
            DmText = "Banned"
        };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Ban count query throws
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        SetupSuccessfulSendAnimation();

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Should still succeed, using 0 as fallback ban count
        Assert.That(result, Is.True);

        // Verify caption used 0 as the ban count fallback
        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            123,
            Arg.Any<InputFile>(),
            "Ban #0!",
            ParseMode.Markdown,
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region File Caching Tests

    [Test]
    public async Task SendBanCelebration_WhenGifSentSuccessfully_UpdatesFileIdCache()
    {
        // Arrange - GIF has no cached FileId (empty string triggers local upload path)
        // Create a real temp file since the service calls System.IO.File.Exists()
        var tempDir = Path.Combine(Path.GetTempPath(), "ban-celebration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "1.gif");
        await File.WriteAllBytesAsync(tempFile, [0x47, 0x49, 0x46]); // GIF magic bytes

        try
        {
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
            _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
            _mockGifRepository.GetFullPath(gif.FilePath).Returns(tempFile);
            _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
            _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

            // Setup SendAndSaveAnimationAsync to return message with new FileId from Telegram
            var sentMessage = new Message
            {
                Animation = new Animation { FileId = "new_telegram_file_id" }
            };
            _mockMessageService.SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode>(),
                Arg.Any<CancellationToken>())
                .Returns(sentMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

            // Assert
            Assert.That(result, Is.True);

            // Verify the file_id was cached back to the repository
            await _mockGifRepository.Received(1).UpdateFileIdAsync(
                gif.Id,
                "new_telegram_file_id",
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SendBanCelebration_WhenGifAlreadyCached_DoesNotUpdateFileId()
    {
        // Arrange - GIF already has a cached FileId
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "existing_file_id" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Setup SendAndSaveAnimationAsync — returns message with same FileId (cached send)
        SetupSuccessfulSendAnimation("existing_file_id");

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.True);

        // Verify UpdateFileIdAsync was NOT called (FileId was already cached)
        await _mockGifRepository.DidNotReceive().UpdateFileIdAsync(
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenCachedFileIdIsStale_ClearsAndFallsBackToLocalUpload()
    {
        // Arrange - GIF has a stale cached FileId
        var tempDir = Path.Combine(Path.GetTempPath(), "ban-celebration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "1.gif");
        await File.WriteAllBytesAsync(tempFile, [0x47, 0x49, 0x46]); // GIF magic bytes

        try
        {
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "stale_file_id" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
            _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
            _mockGifRepository.GetFullPath(gif.FilePath).Returns(tempFile);
            _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
            _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

            // First call with cached file_id throws "wrong file identifier" (stale)
            // Second call with local file stream succeeds
            var successMessage = new Message
            {
                Animation = new Animation { FileId = "new_file_id_from_upload" }
            };
            _mockMessageService.SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    _ => throw new Exception("Bad Request: wrong file identifier/HTTP URL specified"),
                    _ => successMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

            // Assert
            Assert.That(result, Is.True);

            // Verify stale file_id was cleared
            await _mockGifRepository.Received(1).ClearFileIdAsync(1, Arg.Any<CancellationToken>());

            // Verify SendAndSaveAnimationAsync was called twice (stale attempt + local upload)
            await _mockMessageService.Received(2).SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public async Task SendBanCelebration_WhenExceptionOccurs_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        _mockConfigService.GetEffectiveAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration, Arg.Any<long>())
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False, "Should return false when exception occurs");

        // Verify warning was logged
        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to send ban celebration")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task SendBanCelebration_WhenMessageServiceThrows_ReturnsFalse()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Message service throws (Telegram API error)
        _mockMessageService.SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Telegram API rate limited"));

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Service handles exception gracefully and returns false
        Assert.That(result, Is.False);
    }

    #endregion
}
