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

    #region GetBotClientAsync Tests

    [Test]
    public async Task GetBotClientAsync_SameToken_ReturnsSameClient()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);

        // Act
        var client1 = await _sut.GetBotClientAsync();
        var client2 = await _sut.GetBotClientAsync();

        // Assert
        Assert.That(client2, Is.SameAs(client1), "Same token should return cached client");
    }

    [Test]
    public async Task GetBotClientAsync_DifferentToken_ReturnsNewClient()
    {
        // Arrange - First call returns token1, second returns token2
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1, TestToken2);

        // Act
        var client1 = await _sut.GetBotClientAsync();
        var client2 = await _sut.GetBotClientAsync();

        // Assert
        Assert.That(client2, Is.Not.SameAs(client1), "Different token should return new client");
    }

    [Test]
    public async Task GetBotClientAsync_AfterTokenChange_LogsTokenChanged()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1, TestToken2);

        // Act
        _ = await _sut.GetBotClientAsync();
        _ = await _sut.GetBotClientAsync();

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

    #region Exception Handling Tests

    [Test]
    public void GetBotClientAsync_WhenLoaderReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns((string)null!);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _sut.GetBotClientAsync());

        Assert.That(ex!.ParamName, Is.EqualTo("token"));
    }

    [Test]
    public void GetBotClientAsync_WhenLoaderReturnsEmptyString_ThrowsArgumentException()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(string.Empty);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _sut.GetBotClientAsync());

        Assert.That(ex!.ParamName, Is.EqualTo("token"));
    }

    [Test]
    public void GetBotClientAsync_WhenLoaderReturnsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns("   ");

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _sut.GetBotClientAsync());

        Assert.That(ex!.ParamName, Is.EqualTo("token"));
    }

    [Test]
    public void GetBotClientAsync_WhenLoaderThrowsException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Config not available");
        _mockConfigLoader.LoadConfigAsync().Returns<string>(_ => throw expectedException);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sut.GetBotClientAsync());

        Assert.That(ex, Is.SameAs(expectedException));
    }

    [Test]
    public void GetOperationsAsync_WhenLoaderReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns((string)null!);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _sut.GetOperationsAsync());

        Assert.That(ex!.ParamName, Is.EqualTo("token"));
    }

    [Test]
    public void GetOperationsAsync_WhenLoaderThrowsException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Config not available");
        _mockConfigLoader.LoadConfigAsync().Returns<string>(_ => throw expectedException);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sut.GetOperationsAsync());

        Assert.That(ex, Is.SameAs(expectedException));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public async Task Dispose_ClearsCache_SubsequentCallsCreateNewClient()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);
        var clientBefore = await _sut.GetBotClientAsync();
        _sut.Dispose();

        // Create new factory (simulating app restart)
        var newFactory = new TelegramBotClientFactory(_mockConfigLoader, _mockLoggerFactory);

        // Act
        var clientAfter = await newFactory.GetBotClientAsync();

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

    [Test]
    public async Task GetBotClientAsync_AfterDispose_ReturnsNewClient()
    {
        // Arrange - Get a client, then dispose
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);
        var clientBefore = await _sut.GetBotClientAsync();
        _sut.Dispose();

        // Act - Call again after dispose (factory allows reuse after dispose)
        var clientAfter = await _sut.GetBotClientAsync();

        // Assert - Should create new client since cache was cleared
        Assert.That(clientAfter, Is.Not.SameAs(clientBefore));
    }

    [Test]
    public async Task GetOperationsAsync_AfterDispose_ReturnsNewOperations()
    {
        // Arrange - Get operations, then dispose
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);
        var operationsBefore = await _sut.GetOperationsAsync();
        _sut.Dispose();

        // Act - Call again after dispose
        var operationsAfter = await _sut.GetOperationsAsync();

        // Assert - Should create new operations since cache was cleared
        Assert.That(operationsAfter, Is.Not.SameAs(operationsBefore));
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task GetBotClientAsync_ConcurrentCalls_ReturnsSameClientInstance()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);

        // Act - Call from multiple threads simultaneously
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetBotClientAsync())
            .ToList();

        var clients = await Task.WhenAll(tasks);

        // Assert - All should return the same cached instance
        var firstClient = clients[0];
        Assert.That(clients.All(c => ReferenceEquals(c, firstClient)), Is.True,
            "All concurrent calls should return the same client instance");
    }

    [Test]
    public async Task GetOperationsAsync_ConcurrentCalls_ReturnsSameOperationsInstance()
    {
        // Arrange
        _mockConfigLoader.LoadConfigAsync().Returns(TestToken1);

        // Act - Call from multiple threads simultaneously
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetOperationsAsync())
            .ToList();

        var operationsList = await Task.WhenAll(tasks);

        // Assert - All should return the same cached instance
        var firstOperations = operationsList[0];
        Assert.That(operationsList.All(o => ReferenceEquals(o, firstOperations)), Is.True,
            "All concurrent calls should return the same operations instance");
    }

    [Test]
    public async Task GetBotClientAsync_ConcurrentCallsWithTokenChange_HandlesRaceCondition()
    {
        // Arrange - First few calls get token1, rest get token2
        var callCount = 0;
        _mockConfigLoader.LoadConfigAsync().Returns(_ =>
        {
            var count = Interlocked.Increment(ref callCount);
            return count <= 5 ? TestToken1 : TestToken2;
        });

        // Act - Call from multiple threads
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetBotClientAsync())
            .ToList();

        var clients = await Task.WhenAll(tasks);

        // Assert - Should have at most 2 distinct client instances (one per token)
        var distinctClients = clients.Distinct().Count();
        Assert.That(distinctClients, Is.LessThanOrEqualTo(2),
            "Should have at most 2 client instances (one per token)");
    }

    #endregion
}
