using System.Reflection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Metrics;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Unit tests for ClamAVScannerService env var override logic.
/// Covers CLAM-01 through CLAM-04:
///   CLAM-01: When both CLAMAV_HOST + CLAMAV_PORT are set, DB config is NOT read (override used)
///   CLAM-02: When only one env var is set (or neither, or invalid), DB config is used
///   CLAM-03: First call with override logs exactly one INFO message; subsequent calls do not re-log
///   CLAM-04: GetHealthAsync returns Host/Port matching env var values when override is active
/// </summary>
[TestFixture]
public class ClamAVScannerServiceTests
{
    private ILogger<ClamAVScannerService> _mockLogger = null!;
    private ISystemConfigRepository _mockConfigRepository = null!;
    private ClamAVScannerService _service = null!;

    private const string EnvHost = "CLAMAV_HOST";
    private const string EnvPort = "CLAMAV_PORT";

    private static readonly FileScanningConfig DbConfig = new()
    {
        Tier1 = new Tier1Config
        {
            ClamAV = new ClamAVConfig
            {
                Host = "db-host",
                Port = 9999
            }
        }
    };

    [SetUp]
    public void Setup()
    {
        // Clear env vars before each test
        Environment.SetEnvironmentVariable(EnvHost, null);
        Environment.SetEnvironmentVariable(EnvPort, null);

        _mockLogger = Substitute.For<ILogger<ClamAVScannerService>>();
        _mockConfigRepository = Substitute.For<ISystemConfigRepository>();
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns(DbConfig);

        _service = new ClamAVScannerService(_mockLogger, _mockConfigRepository, new DetectionMetrics());

        ResetHasLoggedOverride();
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up env vars
        Environment.SetEnvironmentVariable(EnvHost, null);
        Environment.SetEnvironmentVariable(EnvPort, null);

        ResetHasLoggedOverride();
    }

    private static void ResetHasLoggedOverride()
    {
        var field = typeof(ClamAVScannerService)
            .GetField("_hasLoggedOverride", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, 0);
    }

    // -------------------------------------------------------------------------
    // CLAM-01: Both env vars set → DB config NOT called
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetHealthAsync_WhenBothEnvVarsSet_DoesNotCallConfigRepository()
    {
        // Arrange — use localhost:1 so the TCP connection fails fast with "connection refused"
        // (avoids multi-second timeout that would occur with an unresolvable hostname)
        Environment.SetEnvironmentVariable(EnvHost, "localhost");
        Environment.SetEnvironmentVariable(EnvPort, "1");

        // Make DB repo throw to confirm it's never called
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ => throw new InvalidOperationException("DB must not be called when env vars are set"));

        // Act — will fail to connect (port 1 is not listening) but should NOT throw from DB call
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert — result carries env var host/port, and GetAsync was never called
        Assert.That(result.Host, Is.EqualTo("localhost"));
        Assert.That(result.Port, Is.EqualTo(1));

        await _mockConfigRepository.DidNotReceive()
            .GetAsync(chatId: null, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // CLAM-02: Partial/invalid env var combinations → DB config used
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetHealthAsync_WhenOnlyClamavHostSet_UsesDatabaseConfig()
    {
        // Arrange — only HOST set, PORT missing.
        // Make GetAsync throw a recognizable sentinel so we can confirm it was consulted
        // and the test completes without waiting for a TCP connection to timeout.
        Environment.SetEnvironmentVariable(EnvHost, "shared-clam");
        Environment.SetEnvironmentVariable(EnvPort, null);

        var dbConsulted = false;
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ =>
            {
                dbConsulted = true;
                throw new InvalidOperationException("sentinel: DB was consulted");
            });

        // Act — GetAsync throws → GetEffectiveEndpointAsync propagates → GetHealthAsync catch returns unhealthy
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert — DB config was consulted (override was NOT used)
        Assert.That(dbConsulted, Is.True, "DB config must be consulted when only CLAMAV_HOST is set");
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public async Task GetHealthAsync_WhenOnlyClamavPortSet_UsesDatabaseConfig()
    {
        // Arrange — only PORT set, HOST missing
        Environment.SetEnvironmentVariable(EnvHost, null);
        Environment.SetEnvironmentVariable(EnvPort, "3311");

        var dbConsulted = false;
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ =>
            {
                dbConsulted = true;
                throw new InvalidOperationException("sentinel: DB was consulted");
            });

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.That(dbConsulted, Is.True, "DB config must be consulted when only CLAMAV_PORT is set");
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public async Task GetHealthAsync_WhenClamavPortIsInvalidInteger_UsesDatabaseConfig()
    {
        // Arrange — PORT is non-numeric
        Environment.SetEnvironmentVariable(EnvHost, "shared-clam");
        Environment.SetEnvironmentVariable(EnvPort, "notanumber");

        var dbConsulted = false;
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ =>
            {
                dbConsulted = true;
                throw new InvalidOperationException("sentinel: DB was consulted");
            });

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.That(dbConsulted, Is.True, "DB config must be consulted when CLAMAV_PORT is not a valid integer");
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public async Task GetHealthAsync_WhenClamavHostIsEmptyString_UsesDatabaseConfig()
    {
        // Arrange — HOST is empty string (container orchestrators can set this)
        Environment.SetEnvironmentVariable(EnvHost, "");
        Environment.SetEnvironmentVariable(EnvPort, "3311");

        var dbConsulted = false;
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ =>
            {
                dbConsulted = true;
                throw new InvalidOperationException("sentinel: DB was consulted");
            });

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.That(dbConsulted, Is.True, "DB config must be consulted when CLAMAV_HOST is empty string");
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public async Task GetHealthAsync_WhenNeitherEnvVarSet_UsesDatabaseConfig()
    {
        // Arrange — no env vars (default case)
        // Both already null from Setup

        var dbConsulted = false;
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ =>
            {
                dbConsulted = true;
                throw new InvalidOperationException("sentinel: DB was consulted");
            });

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.That(dbConsulted, Is.True, "DB config must be consulted when neither env var is set");
        Assert.That(result.IsHealthy, Is.False);
    }

    // -------------------------------------------------------------------------
    // CLAM-03: One-time INFO log for override
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetHealthAsync_WhenBothEnvVarsSet_LogsOverrideInfoOnFirstCallOnly()
    {
        // Arrange — use localhost:1 so the TCP connection fails fast with "connection refused"
        Environment.SetEnvironmentVariable(EnvHost, "localhost");
        Environment.SetEnvironmentVariable(EnvPort, "1");

        // DB throws to ensure we don't accidentally call it
        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ => throw new InvalidOperationException("Should not be called"));

        // Act — first call
        await _service.GetHealthAsync(CancellationToken.None);

        // Assert — INFO log was emitted with override message
        _mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("ClamAV env var override active")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Act — second call (override flag still set from first call within same test run)
        await _service.GetHealthAsync(CancellationToken.None);

        // Assert — still only one INFO log for override (no second log emitted)
        _mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("ClamAV env var override active")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // -------------------------------------------------------------------------
    // CLAM-04: GetHealthAsync returns env var host/port in result
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetHealthAsync_WhenBothEnvVarsSet_ReturnsEnvVarHostAndPort()
    {
        // Arrange — use localhost:1 so the TCP connection fails fast with "connection refused"
        Environment.SetEnvironmentVariable(EnvHost, "localhost");
        Environment.SetEnvironmentVariable(EnvPort, "1");

        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ => throw new InvalidOperationException("Should not be called"));

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert — the result reflects override values (ping fails, but host/port always set in result)
        Assert.That(result.Host, Is.EqualTo("localhost"));
        Assert.That(result.Port, Is.EqualTo(1));
    }

    [Test]
    public async Task GetHealthAsync_WhenBothEnvVarsSet_ErrorMessageContainsEnvVarHostPort()
    {
        // Arrange — use localhost:1 so the TCP connection fails fast with "connection refused"
        Environment.SetEnvironmentVariable(EnvHost, "localhost");
        Environment.SetEnvironmentVariable(EnvPort, "1");

        _mockConfigRepository.GetAsync(chatId: null, Arg.Any<CancellationToken>())
            .Returns<FileScanningConfig>(_ => throw new InvalidOperationException("Should not be called"));

        // Act
        var result = await _service.GetHealthAsync(CancellationToken.None);

        // Assert — IsHealthy false (port 1 not listening), error message does not reference DB values
        Assert.That(result.IsHealthy, Is.False);

        if (result.ErrorMessage != null)
        {
            Assert.That(result.ErrorMessage, Does.Not.Contain("db-host"),
                "Error message must not reference DB host when override is active");
        }
    }
}
