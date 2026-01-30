using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for BanCelebrationService.
/// Tests the shuffle-bag algorithm that ensures all GIFs/captions are shown before any repeats.
/// </summary>
[TestFixture]
public class BanCelebrationServiceTests
{
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramBotClientFactory _mockBotClientFactory = null!;
#pragma warning restore NUnit1032
    private IServiceScopeFactory _mockScopeFactory = null!;
    private IDmDeliveryService _mockDmDeliveryService = null!;
    private IOptions<MessageHistoryOptions> _mockHistoryOptions = null!;
    private ILogger<BanCelebrationService> _mockLogger = null!;
    private BanCelebrationService _sut = null!;

    // Mock scoped services
    private IConfigService _mockConfigService = null!;
    private IBanCelebrationGifRepository _mockGifRepository = null!;
    private IBanCelebrationCaptionRepository _mockCaptionRepository = null!;
    private IUserActionsRepository _mockUserActionsRepository = null!;
    private ITelegramOperations _mockTelegramOperations = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockDmDeliveryService = Substitute.For<IDmDeliveryService>();
        _mockLogger = Substitute.For<ILogger<BanCelebrationService>>();

        // Setup MessageHistoryOptions
        var historyOptions = new MessageHistoryOptions { ImageStoragePath = "/data" };
        _mockHistoryOptions = Options.Create(historyOptions);

        // Setup scoped services
        _mockConfigService = Substitute.For<IConfigService>();
        _mockGifRepository = Substitute.For<IBanCelebrationGifRepository>();
        _mockCaptionRepository = Substitute.For<IBanCelebrationCaptionRepository>();
        _mockUserActionsRepository = Substitute.For<IUserActionsRepository>();
        _mockTelegramOperations = Substitute.For<ITelegramOperations>();

        // Setup service scope factory to return mocked scoped services
        var mockScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceProvider.GetService(typeof(IConfigService)).Returns(_mockConfigService);
        mockServiceProvider.GetService(typeof(IBanCelebrationGifRepository)).Returns(_mockGifRepository);
        mockServiceProvider.GetService(typeof(IBanCelebrationCaptionRepository)).Returns(_mockCaptionRepository);
        mockServiceProvider.GetService(typeof(IUserActionsRepository)).Returns(_mockUserActionsRepository);

        mockScope.ServiceProvider.Returns(mockServiceProvider);

        _mockScopeFactory.CreateScope().Returns(mockScope);
        _mockScopeFactory.CreateAsyncScope().Returns(new AsyncServiceScope(mockScope));

        // Setup bot client factory
        _mockBotClientFactory.GetOperationsAsync()
            .Returns(_mockTelegramOperations);

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
            _mockBotClientFactory,
            _mockScopeFactory,
            _mockDmDeliveryService,
            _mockHistoryOptions,
            _mockLogger);
    }

    /// <summary>
    /// Helper to setup SendAnimationAsync to return a Message with Animation.
    /// Uses direct object initialization (Telegram.Bot types have settable properties).
    /// </summary>
    private void SetupSuccessfulSendAnimation(string fileId = "cached_file_id")
    {
        var message = new Message
        {
            Animation = new Animation { FileId = fileId }
        };
        _mockTelegramOperations.SendAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode>(),
            Arg.Any<CancellationToken>())
            .Returns(message);
    }

    #region Shuffle-Bag Algorithm Tests

    [Test]
    public async Task SendBanCelebration_WithThreeGifs_ShowsAllBeforeAnyRepeat()
    {
        // Arrange
        var gifIds = new List<int> { 1, 2, 3 };
        var gifs = gifIds.Select(id => new BanCelebrationGif
        {
            Id = id,
            FilePath = $"ban-gifs/{id}.gif",
            FileId = $"file_id_{id}"
        }).ToList();

        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "Banned {username}!",
            DmText = "You were banned from {chatname}"
        };

        // Setup repositories
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Setup GetByIdAsync to return corresponding GIF
        foreach (var gif in gifs)
        {
            _mockGifRepository.GetByIdAsync(gif.Id, Arg.Any<CancellationToken>()).Returns(gif);
        }

        // Setup Telegram operations to return a message
        SetupSuccessfulSendAnimation();

        // Act - Call 3 times (exhaust the bag)
        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(
                chatId: 123,
                chatName: "Test Chat",
                bannedUserId: 456,
                bannedUserName: "BadUser",
                isAutoBan: true);

            Assert.That(result, Is.True, $"Call {i + 1} should succeed");
        }

        // Assert - Verify GetByIdAsync was called for each GIF exactly once
        foreach (var gifId in gifIds)
        {
            await _mockGifRepository.Received(1).GetByIdAsync(gifId, Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task SendBanCelebration_AfterBagExhaustion_ReshufflesFromDatabase()
    {
        // Arrange
        var gifIds = new List<int> { 1, 2 };
        var gifs = gifIds.Select(id => new BanCelebrationGif
        {
            Id = id,
            FilePath = $"ban-gifs/{id}.gif",
            FileId = $"file_id_{id}"
        }).ToList();

        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "Banned!",
            DmText = "You were banned"
        };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        foreach (var gif in gifs)
        {
            _mockGifRepository.GetByIdAsync(gif.Id, Arg.Any<CancellationToken>()).Returns(gif);
        }

        SetupSuccessfulSendAnimation();

        // Act - Call 3 times (2 to exhaust bag, 1 to trigger reshuffle)
        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(
                chatId: 123,
                chatName: "Test Chat",
                bannedUserId: 456,
                bannedUserName: "BadUser",
                isAutoBan: true);

            Assert.That(result, Is.True, $"Call {i + 1} should succeed");
        }

        // Assert - GetAllIdsAsync should be called twice (initial + reshuffle)
        await _mockGifRepository.Received(2).GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenGifDeletedFromBag_SkipsToNextGif()
    {
        // Arrange
        var gifIds = new List<int> { 1, 2, 3 };

        // GIF 1 exists, GIF 2 was deleted (returns null), GIF 3 exists
        var gif1 = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var gif3 = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "file3" };

        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Setup GetByIdAsync: ID 1 returns gif, ID 2 returns null (deleted), ID 3 returns gif
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif1);
        _mockGifRepository.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((BanCelebrationGif?)null);
        _mockGifRepository.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(gif3);

        SetupSuccessfulSendAnimation();

        // Act - Call twice to get both valid GIFs (deleted one is skipped automatically)
        var result1 = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);
        var result2 = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(result2, Is.True);

        // Verify both valid GIFs were used (order doesn't matter due to random shuffle)
        await _mockGifRepository.Received().GetByIdAsync(1, Arg.Any<CancellationToken>());
        await _mockGifRepository.Received().GetByIdAsync(3, Arg.Any<CancellationToken>());

        // The shuffle-bag algorithm ensures deleted items are skipped gracefully
        // We don't assert on GetByIdAsync(2) being called because:
        // 1. The shuffle order is random (Fisher-Yates)
        // 2. ID 2 might be last in the shuffle and never reached in this test
        // 3. The important behavior is that valid GIFs (1, 3) were successfully retrieved
    }

    [Test]
    public async Task SendBanCelebration_WhenNoGifsExist_ReturnsFalse()
    {
        // Arrange
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<int>());

        // Act
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenNoCaptionsExist_ReturnsFalse()
    {
        // Arrange
        var gifIds = new List<int> { 1 };
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<int>());

        // Act
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WithThreeCaptions_ShowsAllBeforeAnyRepeat()
    {
        // Arrange
        var captionIds = new List<int> { 1, 2, 3 };
        var captions = captionIds.Select(id => new BanCelebrationCaption
        {
            Id = id,
            Text = $"Caption {id}",
            DmText = $"DM Caption {id}"
        }).ToList();

        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(captionIds);

        foreach (var caption in captions)
        {
            _mockCaptionRepository.GetByIdAsync(caption.Id, Arg.Any<CancellationToken>()).Returns(caption);
        }

        SetupSuccessfulSendAnimation();

        // Act - Call 3 times
        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);
            Assert.That(result, Is.True);
        }

        // Assert - Each caption should be used exactly once
        foreach (var captionId in captionIds)
        {
            await _mockCaptionRepository.Received(1).GetByIdAsync(captionId, Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task SendBanCelebration_WhenCaptionDeletedFromBag_SkipsToNextCaption()
    {
        // Arrange
        var captionIds = new List<int> { 1, 2, 3 };
        var caption1 = new BanCelebrationCaption { Id = 1, Text = "Caption 1", DmText = "DM 1" };
        var caption3 = new BanCelebrationCaption { Id = 3, Text = "Caption 3", DmText = "DM 3" };

        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(captionIds);

        // Caption 2 was deleted
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption1);
        _mockCaptionRepository.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((BanCelebrationCaption?)null);
        _mockCaptionRepository.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(caption3);

        SetupSuccessfulSendAnimation();

        // Act - Call twice, which will use the two valid captions
        var result1 = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);
        var result2 = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(result2, Is.True);

        // Verify both valid captions were used (order doesn't matter due to random shuffle)
        await _mockCaptionRepository.Received().GetByIdAsync(1, Arg.Any<CancellationToken>());
        await _mockCaptionRepository.Received().GetByIdAsync(3, Arg.Any<CancellationToken>());

        // The shuffle-bag algorithm ensures deleted items are skipped gracefully
        // We don't assert on GetByIdAsync(2) being called because:
        // 1. The shuffle order is random (Fisher-Yates)
        // 2. ID 2 might be last in the shuffle and never reached in this test
        // 3. The important behavior is that valid captions (1, 3) were successfully retrieved
    }

    [Test]
    public async Task SendBanCelebration_WhenAllGifsDeletedMidBag_ReshufflesToEmptyAndReturnsFalse()
    {
        // Arrange - All GIF IDs in the bag return null (all deleted since last shuffle)
        // On reshuffle, the DB is now empty too
        var initialIds = new List<int> { 1, 2 };

        // First call: returns IDs [1,2]. Second call (reshuffle): returns empty
        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>())
            .Returns(initialIds, new List<int>());

        // Both GIFs return null (deleted)
        _mockGifRepository.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((BanCelebrationGif?)null);

        // Act
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert - Should return false gracefully (no infinite loop)
        Assert.That(result, Is.False);

        // Verify it attempted reshuffle after exhausting the bag of deleted items
        await _mockGifRepository.Received(2).GetAllIdsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Multi-Call Bag State Tests

    [Test]
    public async Task SendBanCelebration_MultipleCalls_EachGifUsedExactlyOnceBeforeRepeat()
    {
        // Arrange - 5 GIFs, call 5 times to exhaust the bag
        var gifIds = new List<int> { 1, 2, 3, 4, 5 };
        var gifs = gifIds.Select(id => new BanCelebrationGif
        {
            Id = id,
            FilePath = $"ban-gifs/{id}.gif",
            FileId = $"file_id_{id}"
        }).ToList();

        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        foreach (var gif in gifs)
        {
            _mockGifRepository.GetByIdAsync(gif.Id, Arg.Any<CancellationToken>()).Returns(gif);
        }

        SetupSuccessfulSendAnimation();

        // Act - Call 5 times to fully exhaust the bag
        for (var i = 0; i < 5; i++)
        {
            var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);
            Assert.That(result, Is.True, $"Call {i + 1} should succeed");
        }

        // Assert - Each GIF should be retrieved exactly once (no duplicates, no skips)
        foreach (var gifId in gifIds)
        {
            await _mockGifRepository.Received(1).GetByIdAsync(gifId, Arg.Any<CancellationToken>());
        }
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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result, Is.False);
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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", isAutoBan: true);

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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", isAutoBan: false);

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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", isAutoBan: true);

        // Assert - Should return false because default config has Enabled=false
        Assert.That(result, Is.False);

        // Verify no GIF/caption repos were called (short-circuited at config check)
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
        await _sut.SendBanCelebrationAsync(123, "Test Group", 456, "SpammerBob", true);

        // Assert - Verify the caption sent to Telegram has placeholders replaced
        await _mockTelegramOperations.Received(1).SendAnimationAsync(
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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert - Should still succeed, using 0 as fallback ban count
        Assert.That(result, Is.True);

        // Verify caption used 0 as the ban count fallback
        await _mockTelegramOperations.Received(1).SendAnimationAsync(
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
            var gifIds = new List<int> { 1 };
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
            _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
            _mockGifRepository.GetFullPath(gif.FilePath).Returns(tempFile);
            _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
            _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

            // Setup SendAnimationAsync to return message with new FileId from Telegram
            var sentMessage = new Message
            {
                Animation = new Animation { FileId = "new_telegram_file_id" }
            };
            _mockTelegramOperations.SendAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode>(),
                Arg.Any<CancellationToken>())
                .Returns(sentMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

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
        var gifIds = new List<int> { 1 };
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "existing_file_id" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Setup SendAnimationAsync — returns message with same FileId (cached send)
        SetupSuccessfulSendAnimation("existing_file_id");

        // Act
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

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
            var gifIds = new List<int> { 1 };
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "stale_file_id" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
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
            _mockTelegramOperations.SendAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<string>(),
                Arg.Any<ParseMode>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    _ => throw new Exception("Bad Request: wrong file identifier/HTTP URL specified"),
                    _ => successMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

            // Assert
            Assert.That(result, Is.True);

            // Verify stale file_id was cleared
            await _mockGifRepository.Received(1).ClearFileIdAsync(1, Arg.Any<CancellationToken>());

            // Verify SendAnimationAsync was called twice (stale attempt + local upload)
            await _mockTelegramOperations.Received(2).SendAnimationAsync(
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
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

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
    public async Task SendBanCelebration_WhenBotClientNotAvailable_ReturnsFalse()
    {
        // Arrange
        _mockBotClientFactory.GetOperationsAsync()
            .Returns((ITelegramOperations?)null!);

        var gifIds = new List<int> { 1 };
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(gifIds);
        _mockGifRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        _mockCaptionRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(caption);

        // Act
        var result = await _sut.SendBanCelebrationAsync(123, "Chat", 456, "User", true);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
