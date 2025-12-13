using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Unit tests for CrossChatExecutor.
/// Tests executing actions across multiple chats with health gating and rate limiting.
/// </summary>
[TestFixture]
public class CrossChatExecutorTests
{
    private ITelegramBotClientFactory _mockBotFactory = null!;
    private IManagedChatsRepository _mockManagedChatsRepository = null!;
    private IChatManagementService _mockChatManagementService = null!;
    private ILogger<CrossChatExecutor> _mockLogger = null!;
    private ITelegramOperations _mockOperations = null!;
    private CrossChatExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBotFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockManagedChatsRepository = Substitute.For<IManagedChatsRepository>();
        _mockChatManagementService = Substitute.For<IChatManagementService>();
        _mockLogger = Substitute.For<ILogger<CrossChatExecutor>>();
        _mockOperations = Substitute.For<ITelegramOperations>();

        _mockBotFactory.GetOperationsAsync().Returns(_mockOperations);

        _executor = new CrossChatExecutor(
            _mockBotFactory,
            _mockManagedChatsRepository,
            _mockChatManagementService,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockBotFactory?.Dispose();
    }

    #region Success Scenarios

    [Test]
    public async Task ExecuteAcrossChatsAsync_AllChatsSucceed_ReturnsCorrectCounts()
    {
        // Arrange
        var chats = CreateManagedChats([-100001, -100002, -100003]);
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(chats);
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns([-100001, -100002, -100003]);

        var executedChatIds = new List<long>();

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            async (ops, chatId, ct) =>
            {
                executedChatIds.Add(chatId);
                await Task.CompletedTask;
            },
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(3));
            Assert.That(result.FailCount, Is.EqualTo(0));
            Assert.That(result.SkippedCount, Is.EqualTo(0));
            Assert.That(executedChatIds, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task ExecuteAcrossChatsAsync_SomeChatsUnhealthy_SkipsUnhealthyChats()
    {
        // Arrange - 3 active chats, only 2 are healthy
        var chats = CreateManagedChats([-100001, -100002, -100003]);
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(chats);
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns([-100001, -100002]); // Only 2 healthy

        var executedChatIds = new List<long>();

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            async (ops, chatId, ct) =>
            {
                executedChatIds.Add(chatId);
                await Task.CompletedTask;
            },
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailCount, Is.EqualTo(0));
            Assert.That(result.SkippedCount, Is.EqualTo(1));
            Assert.That(executedChatIds, Does.Not.Contain(-100003));
        });
    }

    #endregion

    #region Failure Scenarios

    [Test]
    public async Task ExecuteAcrossChatsAsync_SomeChatsFail_CountsFailuresCorrectly()
    {
        // Arrange
        var chats = CreateManagedChats([-100001, -100002, -100003]);
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(chats);
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns([-100001, -100002, -100003]);

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            async (ops, chatId, ct) =>
            {
                if (chatId == -100002)
                    throw new InvalidOperationException("Simulated failure");
                await Task.CompletedTask;
            },
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAcrossChatsAsync_AllChatsFail_ReturnsAllFailures()
    {
        // Arrange
        var chats = CreateManagedChats([-100001, -100002]);
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(chats);
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns([-100001, -100002]);

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            (ops, chatId, ct) => throw new InvalidOperationException("All fail"),
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailCount, Is.EqualTo(2));
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task ExecuteAcrossChatsAsync_NoActiveChats_ReturnsZeroCounts()
    {
        // Arrange - All chats are inactive or deleted
        var chats = new List<ManagedChatRecord>
        {
            CreateManagedChat(-100001, isActive: false, isDeleted: false),
            CreateManagedChat(-100002, isActive: true, isDeleted: true)
        };
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(chats);
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns(new List<long>());

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            async (ops, chatId, ct) => await Task.CompletedTask,
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailCount, Is.EqualTo(0));
            Assert.That(result.SkippedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAcrossChatsAsync_EmptyChatList_ReturnsZeroCounts()
    {
        // Arrange
        _mockManagedChatsRepository.GetAllChatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());
        _mockChatManagementService.FilterHealthyChats(Arg.Any<IEnumerable<long>>())
            .Returns(new List<long>());

        // Act
        var result = await _executor.ExecuteAcrossChatsAsync(
            async (ops, chatId, ct) => await Task.CompletedTask,
            "TestAction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailCount, Is.EqualTo(0));
            Assert.That(result.SkippedCount, Is.EqualTo(0));
        });
    }

    #endregion

    #region Helper Methods

    private static List<ManagedChatRecord> CreateManagedChats(long[] chatIds)
    {
        return chatIds.Select(id => CreateManagedChat(id)).ToList();
    }

    private static ManagedChatRecord CreateManagedChat(
        long chatId,
        bool isActive = true,
        bool isDeleted = false)
    {
        return new ManagedChatRecord(
            ChatId: chatId,
            ChatName: $"Test Chat {chatId}",
            ChatType: ManagedChatType.Supergroup,
            BotStatus: BotChatStatus.Administrator,
            IsAdmin: true,
            AddedAt: DateTimeOffset.UtcNow,
            IsActive: isActive,
            IsDeleted: isDeleted,
            LastSeenAt: DateTimeOffset.UtcNow,
            SettingsJson: null,
            ChatIconPath: null);
    }

    #endregion
}
