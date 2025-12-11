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
/// is done in E2E tests using ITelegramBotClientFactory with NSubstitute mocks.
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

    #endregion

    #region Null Guard Tests

    [Test]
    public void Constructor_WithNullBotClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TelegramOperations(null!, _mockLogger));

        Assert.That(ex!.ParamName, Is.EqualTo("botClient"));
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TelegramOperations(_mockBotClient, null!));

        Assert.That(ex!.ParamName, Is.EqualTo("logger"));
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void TelegramOperations_ImplementsITelegramOperations()
    {
        // Assert - Verifies the class properly implements the interface
        // (compiler enforces method signatures, this confirms runtime type)
        Assert.That(_sut, Is.InstanceOf<ITelegramOperations>());
    }

    #endregion
}
