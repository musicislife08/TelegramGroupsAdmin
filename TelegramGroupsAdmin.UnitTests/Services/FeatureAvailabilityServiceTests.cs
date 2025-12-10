using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for FeatureAvailabilityService
/// Uses NSubstitute to mock ISystemConfigRepository - no database required
/// </summary>
[TestFixture]
public class FeatureAvailabilityServiceTests
{
    private ISystemConfigRepository _mockConfigRepo = null!;
    private ILogger<FeatureAvailabilityService> _mockLogger = null!;
    private FeatureAvailabilityService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfigRepo = Substitute.For<ISystemConfigRepository>();
        _mockLogger = Substitute.For<ILogger<FeatureAvailabilityService>>();
        _service = new FeatureAvailabilityService(_mockConfigRepo, _mockLogger);
    }

    #region IsOpenAIConfiguredAsync Tests

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithNoAIProviderConfig_ReturnsFalse()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithNoConnections_ReturnsFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections = []
        };
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithDisabledConnectionAndKey_ReturnsFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = false }
            ]
        };
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False, "Disabled connection should return false even with API key");
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithEnabledConnectionButNoApiKeys_ReturnsFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = true }
            ]
        };
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns((ApiKeysConfig?)null);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithEnabledConnectionButNoMatchingKey_ReturnsFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = true }
            ]
        };
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["other-connection"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithEnabledConnectionAndMatchingKey_ReturnsTrue()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = true }
            ]
        };
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithEmptyStringKey_ReturnsFalse()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = true }
            ]
        };
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = ""
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False, "Empty string keys should not count as configured");
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WithMultipleConnections_OnlyOneNeedsKey()
    {
        // Arrange
        var config = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = "openai", Enabled = true },
                new AIConnection { Id = "azure", Enabled = true }
            ]
        };
        var apiKeys = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
                // azure has no key
            }
        };

        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns(apiKeys);

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.True, "Should return true if at least one enabled connection has a key");
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WhenRepositoryThrows_ReturnsFalse()
    {
        // Arrange
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns<AIProviderConfig?>(x => throw new InvalidOperationException("Database error"));

        // Act
        var result = await _service.IsOpenAIConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsOpenAIConfiguredAsync_WhenRepositoryThrows_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Database error");
        _mockConfigRepo.GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns<AIProviderConfig?>(x => throw exception);

        // Act
        await _service.IsOpenAIConfiguredAsync();

        // Assert
        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to check AI provider configuration status")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region IsEmailConfiguredAsync Tests

    [Test]
    public async Task IsEmailConfiguredAsync_WhenRepositoryThrows_ReturnsFalse()
    {
        // Arrange
        _mockConfigRepo.GetSendGridConfigAsync(Arg.Any<CancellationToken>())
            .Returns<SendGridConfig?>(x => throw new InvalidOperationException("Database error"));

        // Act
        var result = await _service.IsEmailConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsEmailConfiguredAsync_WhenRepositoryThrows_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Database error");
        _mockConfigRepo.GetSendGridConfigAsync(Arg.Any<CancellationToken>())
            .Returns<SendGridConfig?>(x => throw exception);

        // Act
        await _service.IsEmailConfiguredAsync();

        // Assert
        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to check email configuration status")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region IsVirusTotalConfiguredAsync Tests

    [Test]
    public async Task IsVirusTotalConfiguredAsync_WhenRepositoryThrows_ReturnsFalse()
    {
        // Arrange
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns<ApiKeysConfig?>(x => throw new InvalidOperationException("Database error"));

        // Act
        var result = await _service.IsVirusTotalConfiguredAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsVirusTotalConfiguredAsync_WhenRepositoryThrows_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Database error");
        _mockConfigRepo.GetApiKeysAsync(Arg.Any<CancellationToken>())
            .Returns<ApiKeysConfig?>(x => throw exception);

        // Act
        await _service.IsVirusTotalConfiguredAsync();

        // Assert
        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to check VirusTotal configuration status")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion
}
