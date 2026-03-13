using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Unit tests for ApiKeysConfig model
/// Tests dictionary operations and HasAnyKey() validation
/// </summary>
[TestFixture]
public class ApiKeysConfigTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_InitializesEmptyDictionary()
    {
        // Act
        var config = new ApiKeysConfig();

        // Assert
        Assert.That(config.AIConnectionKeys, Is.Not.Null);
        Assert.That(config.AIConnectionKeys, Is.Empty);
    }

    #endregion

    #region GetAIConnectionKey Tests

    [Test]
    public void GetAIConnectionKey_WhenKeyExists_ReturnsKey()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act
        var key = config.GetAIConnectionKey("openai");

        // Assert
        Assert.That(key, Is.EqualTo("sk-test-key"));
    }

    [Test]
    public void GetAIConnectionKey_WhenKeyDoesNotExist_ReturnsNull()
    {
        // Arrange
        var config = new ApiKeysConfig();

        // Act
        var key = config.GetAIConnectionKey("nonexistent");

        // Assert
        Assert.That(key, Is.Null);
    }

    [Test]
    public void GetAIConnectionKey_IsCaseSensitive()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act
        var key = config.GetAIConnectionKey("OpenAI");

        // Assert
        Assert.That(key, Is.Null, "Connection IDs should be case-sensitive");
    }

    #endregion

    #region SetAIConnectionKey Tests

    [Test]
    public void SetAIConnectionKey_WithValidKey_AddsToDict()
    {
        // Arrange
        var config = new ApiKeysConfig();

        // Act
        config.SetAIConnectionKey("openai", "sk-new-key");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.True);
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-new-key"));
        }
    }

    [Test]
    public void SetAIConnectionKey_OverwritesExistingKey()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-old-key"
            }
        };

        // Act
        config.SetAIConnectionKey("openai", "sk-new-key");

        // Assert
        Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-new-key"));
    }

    [Test]
    public void SetAIConnectionKey_WithNull_RemovesKey()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act
        config.SetAIConnectionKey("openai", null);

        // Assert
        Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.False);
    }

    [Test]
    public void SetAIConnectionKey_WithEmptyString_RemovesKey()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act
        config.SetAIConnectionKey("openai", "");

        // Assert
        Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.False);
    }

    [Test]
    public void SetAIConnectionKey_WithWhitespace_RemovesKey()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act
        config.SetAIConnectionKey("openai", "   ");

        // Assert
        Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.False);
    }

    [Test]
    public void SetAIConnectionKey_RemovingNonexistentKey_DoesNotThrow()
    {
        // Arrange
        var config = new ApiKeysConfig();

        // Act & Assert
        Assert.DoesNotThrow(() => config.SetAIConnectionKey("nonexistent", null));
    }

    #endregion

    #region HasAnyKey Tests

    [Test]
    public void HasAnyKey_WithVirusTotal_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            VirusTotal = "vt-api-key"
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    [Test]
    public void HasAnyKey_WithSendGrid_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            SendGrid = "sg-api-key"
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    [Test]
    public void HasAnyKey_WithAIConnectionKey_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-test-key"
            }
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    [Test]
    public void HasAnyKey_WithNoKeys_ReturnsFalse()
    {
        // Arrange
        var config = new ApiKeysConfig();

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.False);
    }

    [Test]
    public void HasAnyKey_WithWhitespaceKeys_ReturnsFalse()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            VirusTotal = "   ",
            SendGrid = "\t"
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.False);
    }

    [Test]
    public void HasAnyKey_WithEmptyStrings_ReturnsFalse()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            VirusTotal = "",
            SendGrid = ""
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.False);
    }

    [Test]
    public void HasAnyKey_WithMultipleKeys_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            VirusTotal = "vt-key",
            SendGrid = "sg-key",
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-key"
            }
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    #endregion
}
