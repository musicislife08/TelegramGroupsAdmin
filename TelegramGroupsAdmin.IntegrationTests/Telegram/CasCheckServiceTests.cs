using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Integration tests for CasCheckService using WireMock to mock the CAS API.
/// Tests the full HTTP pipeline including serialization, headers, timeout handling, and fail-open behavior.
/// </summary>
/// <remarks>
/// The service uses the "caller gates" pattern - callers are responsible for checking
/// if CAS is enabled before calling. Tests pass CasConfig directly to the service.
/// </remarks>
[TestFixture]
public class CasCheckServiceTests
{
    private WireMockServer _mockServer = null!;
    private ILogger<CasCheckService> _mockLogger = null!;
    private HybridCache _cache = null!;
    private ServiceProvider _cacheServiceProvider = null!;
    private IHttpClientFactory _httpClientFactory = null!;
    private CasCheckService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Start WireMock server on a random port
        _mockServer = WireMockServer.Start();
        _mockServer.Reset(); // Clear any stubs from previous tests

        _mockLogger = Substitute.For<ILogger<CasCheckService>>();

        // Create HybridCache via DI
        var cacheServices = new ServiceCollection();
        cacheServices.AddHybridCache();
        _cacheServiceProvider = cacheServices.BuildServiceProvider();
        _cache = _cacheServiceProvider.GetRequiredService<HybridCache>();

        // Create real HttpClientFactory
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();
        _httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        // Create service - now only takes logger, httpClientFactory, and cache
        _service = new CasCheckService(
            _mockLogger,
            _httpClientFactory,
            _cache);
    }

    [TearDown]
    public void TearDown()
    {
        _mockServer.Stop();
        _mockServer.Dispose();
        _cacheServiceProvider.Dispose();
    }

    #region Happy Path Tests

    [Test]
    public async Task CheckUserAsync_UserNotBanned_ReturnsNotBanned()
    {
        // Arrange - CAS API returns ok=false when user is NOT in ban database
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .WithParam("user_id", userId.ToString())
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok": false, "description": "Record not found."}"""));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsBanned, Is.False);
            Assert.That(result.Reason, Is.Null);
        });
    }

    [Test]
    public async Task CheckUserAsync_UserBanned_ReturnsBannedWithReason()
    {
        // Arrange - CAS API returns ok=true when user IS in ban database
        const long userId = 987654321;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .WithParam("user_id", userId.ToString())
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "ok": true,
                    "result": {
                        "reasons": [1],
                        "offenses": 3,
                        "messages": ["Spam message content"],
                        "time_added": "2021-01-01T00:00:00.000Z"
                    }
                }
                """));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsBanned, Is.True);
            Assert.That(result.Reason, Does.Contain("3 offense"));
        });
    }

    #endregion

    #region Caching Tests

    [Test]
    public async Task CheckUserAsync_SecondCall_UsesCachedResult()
    {
        // Arrange
        const long userId = 111222333;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .WithParam("user_id", userId.ToString())
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok": true, "result": {"reasons": [1], "offenses": 1, "messages": ["Spam"], "time_added": "2021-01-01T00:00:00.000Z"}}"""));

        // Act
        var result1 = await _service.CheckUserAsync(userId, casConfig);
        var result2 = await _service.CheckUserAsync(userId, casConfig);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1.IsBanned, Is.True);
            Assert.That(result2.IsBanned, Is.True);
        });

        // Verify only one request was made (second was cached)
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    #endregion

    #region Fail-Open Tests

    [Test]
    public async Task CheckUserAsync_ApiReturns500_FailsOpen()
    {
        // Arrange
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - Fail open means return not banned
        Assert.Multiple(() =>
        {
            Assert.That(result.IsBanned, Is.False);
            Assert.That(result.Reason, Is.Null);
        });
    }

    [Test]
    public async Task CheckUserAsync_ApiReturns404_FailsOpen()
    {
        // Arrange
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody("Not Found"));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - Fail open
        Assert.That(result.IsBanned, Is.False);
    }

    [Test]
    public async Task CheckUserAsync_InvalidJsonResponse_FailsOpen()
    {
        // Arrange
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("not valid json at all"));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - Fail open on parse error
        Assert.That(result.IsBanned, Is.False);
    }

    [Test]
    public async Task CheckUserAsync_Timeout_FailsOpen()
    {
        // Arrange
        const long userId = 123456789;
        var casConfig = CreateCasConfig(timeout: TimeSpan.FromMilliseconds(100)); // Very short timeout

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(5)) // Response takes 5 seconds
                .WithBody("""{"ok": true, "result": {"reasons": [1], "offenses": 1, "messages": ["Spam"], "time_added": "2021-01-01T00:00:00.000Z"}}"""));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - Fail open on timeout
        Assert.That(result.IsBanned, Is.False);
    }

    [Test]
    public async Task CheckUserAsync_NetworkError_FailsOpen()
    {
        // Arrange
        const long userId = 123456789;

        // Point to non-existent server
        var badConfig = new CasConfig
        {
            Enabled = true,
            ApiUrl = "http://localhost:99999", // Invalid port
            Timeout = TimeSpan.FromSeconds(1)
        };

        // Act
        var result = await _service.CheckUserAsync(userId, badConfig);

        // Assert - Fail open on network error
        Assert.That(result.IsBanned, Is.False);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task CheckUserAsync_OkFalse_ReturnsNotBanned()
    {
        // Arrange - CAS API returns ok=false when user is NOT in ban database
        // This is the normal "not banned" response
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok": false, "description": "Record not found."}"""));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - ok=false means user is NOT banned
        Assert.That(result.IsBanned, Is.False);
    }

    [Test]
    public async Task CheckUserAsync_BannedWithZeroOffenses_ReturnsBanned()
    {
        // Arrange - Edge case: banned but with 0 offenses reported
        const long userId = 123456789;
        var casConfig = CreateCasConfig();

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok": true, "result": {"reasons": [1], "offenses": 0, "messages": ["Spam"], "time_added": "2021-01-01T00:00:00.000Z"}}"""));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - ok=true means banned, even with 0 offenses
        Assert.Multiple(() =>
        {
            Assert.That(result.IsBanned, Is.True);
            Assert.That(result.Reason, Does.Contain("0 offense"));
        });
    }

    [Test]
    public async Task CheckUserAsync_UserAgentConfigured_SendsHeader()
    {
        // Arrange
        const long userId = 123456789;
        const string expectedUserAgent = "TelegramGroupsAdmin/1.0";

        var casConfig = new CasConfig
        {
            Enabled = true,
            ApiUrl = _mockServer.Urls[0],
            Timeout = TimeSpan.FromSeconds(5),
            UserAgent = expectedUserAgent
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/check")
                .WithHeader("User-Agent", expectedUserAgent)
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok": false, "description": "Record not found."}"""));

        // Act
        var result = await _service.CheckUserAsync(userId, casConfig);

        // Assert - User not banned, but we verified the header was sent
        Assert.That(result.IsBanned, Is.False);
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    #endregion

    #region Helper Methods

    private CasConfig CreateCasConfig(TimeSpan? timeout = null)
    {
        return new CasConfig
        {
            Enabled = true,
            ApiUrl = _mockServer.Urls[0],
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
    }

    #endregion
}
