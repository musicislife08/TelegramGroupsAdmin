using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for TelegramBotClientFactory.
/// Tests the refresh-on-change caching behavior and operations wrapper creation.
/// </summary>
[TestFixture]
public class TelegramBotClientFactoryTests
{
    private ITelegramConfigLoader _mockConfigLoader = null!;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ILoggerFactory _mockLoggerFactory = null!;
#pragma warning restore NUnit1032
    private ILogger<TelegramBotClientFactory> _mockFactoryLogger = null!;
    private ILogger<TelegramOperations> _mockOperationsLogger = null!;
    private TelegramBotClientFactory _sut = null!;

    private const string TestToken1 = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
    private const string TestToken2 = "987654:XYZ-ABC9876ghIkl-abc12W2v1u456ew22";

    [SetUp]
    public void SetUp()
    {
        _mockConfigLoader = Substitute.For<ITelegramConfigLoader>();
        _mockLoggerFactory = Substitute.For<ILoggerFactory>();
        _mockFactoryLogger = Substitute.For<ILogger<TelegramBotClientFactory>>();
        _mockOperationsLogger = Substitute.For<ILogger<TelegramOperations>>();

        _mockLoggerFactory.CreateLogger<TelegramBotClientFactory>()
            .Returns(_mockFactoryLogger);
        _mockLoggerFactory.CreateLogger<TelegramOperations>()
            .Returns(_mockOperationsLogger);

        _sut = new TelegramBotClientFactory(_mockConfigLoader, _mockLoggerFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _sut?.Dispose();
    }

    #region GetOrCreate Tests

    [Test]
    public void GetOrCreate_SameToken_ReturnsSameClient()
    {
        // Act
        var client1 = _sut.GetOrCreate(TestToken1);
        var client2 = _sut.GetOrCreate(TestToken1);

        // Assert
        Assert.That(client2, Is.SameAs(client1), "Same token should return cached client");
    }

    [Test]
    public void GetOrCreate_DifferentToken_ReturnsNewClient()
    {
        // Act
        var client1 = _sut.GetOrCreate(TestToken1);
        var client2 = _sut.GetOrCreate(TestToken2);

        // Assert
        Assert.That(client2, Is.Not.SameAs(client1), "Different token should return new client");
    }

    [Test]
    public void GetOrCreate_AfterTokenChange_LogsTokenChanged()
    {
        // Act
        _ = _sut.GetOrCreate(TestToken1);
        _ = _sut.GetOrCreate(TestToken2);

        // Assert - Verify LogInformation was called (token changed message)
        _mockFactoryLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("token changed")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region GetOperationsAsync Tests

    [Test]
    public async Task GetOperationsAsync_ReturnsITelegramOperations()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);

        // Act
        var operations = await _sut.GetOperationsAsync();

        // Assert
        Assert.That(operations, Is.Not.Null);
        Assert.That(operations, Is.InstanceOf<ITelegramOperations>());
    }

    [Test]
    public async Task GetOperationsAsync_SameToken_ReturnsSameOperations()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);

        // Act
        var operations1 = await _sut.GetOperationsAsync();
        var operations2 = await _sut.GetOperationsAsync();

        // Assert
        Assert.That(operations2, Is.SameAs(operations1), "Same token should return cached operations");
    }

    [Test]
    public async Task GetOperationsAsync_DifferentToken_ReturnsNewOperations()
    {
        // Arrange - First call returns token1, second returns token2
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1, TestToken2);

        // Act
        var operations1 = await _sut.GetOperationsAsync();
        var operations2 = await _sut.GetOperationsAsync();

        // Assert
        Assert.That(operations2, Is.Not.SameAs(operations1), "Different token should return new operations");
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ClearsCache_SubsequentCallsCreateNewClient()
    {
        // Arrange
        var clientBefore = _sut.GetOrCreate(TestToken1);
        _sut.Dispose();

        // Create new factory (simulating app restart)
        var newFactory = new TelegramBotClientFactory(_mockConfigLoader, _mockLoggerFactory);

        // Act
        var clientAfter = newFactory.GetOrCreate(TestToken1);

        // Assert
        Assert.That(clientAfter, Is.Not.SameAs(clientBefore), "After dispose, new client should be created");

        // Cleanup
        newFactory.Dispose();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
        {
            _sut.Dispose();
            _sut.Dispose();
            _sut.Dispose();
        });
    }

    #endregion
}
