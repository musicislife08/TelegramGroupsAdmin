using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for TelegramOperations wrapper.
///
/// Note: The Telegram.Bot library uses extension methods that internally call MakeRequestAsync.
/// Since extension methods can't be mocked directly with NSubstitute, these tests verify:
/// 1. Property delegation (BotId)
/// 2. Constructor initialization
///
/// Full integration testing of the wrapper methods (SendMessageAsync, BanChatMemberAsync, etc.)
/// is done in E2E tests where TestTelegramBotClientFactory provides a mock client.
/// </summary>
[TestFixture]
public class TelegramOperationsTests
{
    private ITelegramBotClient _mockBotClient = null!;
    private ILogger<TelegramOperations> _mockLogger = null!;
    private TelegramOperations _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBotClient = Substitute.For<ITelegramBotClient>();
        _mockLogger = Substitute.For<ILogger<TelegramOperations>>();
        _sut = new TelegramOperations(_mockBotClient, _mockLogger);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var operations = new TelegramOperations(_mockBotClient, _mockLogger);

        // Assert
        Assert.That(operations, Is.Not.Null);
    }

    #endregion

    #region Property Delegation Tests

    [Test]
    public void BotId_ReturnsBotClientBotId()
    {
        // Arrange
        var expectedBotId = 987654321L;
        _mockBotClient.BotId.Returns(expectedBotId);

        // Act
        var result = _sut.BotId;

        // Assert
        Assert.That(result, Is.EqualTo(expectedBotId));
    }

    [Test]
    public void BotId_WhenBotClientReturnsZero_ReturnsZero()
    {
        // Arrange
        _mockBotClient.BotId.Returns(0L);

        // Act
        var result = _sut.BotId;

        // Assert
        Assert.That(result, Is.EqualTo(0L));
    }

    [Test]
    public void BotId_WhenBotClientReturnsNegative_ReturnsNegative()
    {
        // Arrange - Telegram bot IDs are always positive, but test edge case
        _mockBotClient.BotId.Returns(-1L);

        // Act
        var result = _sut.BotId;

        // Assert
        Assert.That(result, Is.EqualTo(-1L));
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void TelegramOperations_ImplementsITelegramOperations()
    {
        // Assert
        Assert.That(_sut, Is.InstanceOf<ITelegramOperations>());
    }

    [Test]
    public void ITelegramOperations_HasExpectedMethods()
    {
        // Verify key methods exist on the interface
        var interfaceType = typeof(ITelegramOperations);

        Assert.Multiple(() =>
        {
            Assert.That(interfaceType.GetMethod("SendMessageAsync"), Is.Not.Null, "SendMessageAsync should exist");
            Assert.That(interfaceType.GetMethod("DeleteMessageAsync"), Is.Not.Null, "DeleteMessageAsync should exist");
            Assert.That(interfaceType.GetMethod("BanChatMemberAsync"), Is.Not.Null, "BanChatMemberAsync should exist");
            Assert.That(interfaceType.GetMethod("UnbanChatMemberAsync"), Is.Not.Null, "UnbanChatMemberAsync should exist");
            Assert.That(interfaceType.GetMethod("GetMeAsync"), Is.Not.Null, "GetMeAsync should exist");
            Assert.That(interfaceType.GetMethod("GetFileAsync"), Is.Not.Null, "GetFileAsync should exist");
            Assert.That(interfaceType.GetMethod("DownloadFileAsync"), Is.Not.Null, "DownloadFileAsync should exist");
        });
    }

    #endregion
}
