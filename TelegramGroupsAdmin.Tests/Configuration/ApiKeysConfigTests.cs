using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Tests.Configuration;

/// <summary>
/// Unit tests for ApiKeysConfig model
/// Tests migration logic, dictionary operations, and HasAnyKey() validation
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

    #region MigrateLegacyKeys Tests

    [Test]
    public void MigrateLegacyKeys_WithOpenAI_MigratesAndClearsLegacy()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "sk-test-openai-key"
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.True);
            Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.True);
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-test-openai-key"));
            Assert.That(config.OpenAI, Is.Null);
        });
    }

    [Test]
    public void MigrateLegacyKeys_WithAzureOpenAI_MigratesAndClearsLegacy()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AzureOpenAI = "azure-test-key"
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.True);
            Assert.That(config.AIConnectionKeys.ContainsKey("azure-openai"), Is.True);
            Assert.That(config.AIConnectionKeys["azure-openai"], Is.EqualTo("azure-test-key"));
            Assert.That(config.AzureOpenAI, Is.Null);
        });
    }

    [Test]
    public void MigrateLegacyKeys_WithLocalAI_MigratesAndClearsLegacy()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            LocalAI = "local-test-key"
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.True);
            Assert.That(config.AIConnectionKeys.ContainsKey("local-ai"), Is.True);
            Assert.That(config.AIConnectionKeys["local-ai"], Is.EqualTo("local-test-key"));
            Assert.That(config.LocalAI, Is.Null);
        });
    }

    [Test]
    public void MigrateLegacyKeys_WithAllThreeLegacyKeys_MigratesAll()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "sk-openai",
            AzureOpenAI = "azure-key",
            LocalAI = "local-key"
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.True);
            Assert.That(config.AIConnectionKeys.Count, Is.EqualTo(3));
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-openai"));
            Assert.That(config.AIConnectionKeys["azure-openai"], Is.EqualTo("azure-key"));
            Assert.That(config.AIConnectionKeys["local-ai"], Is.EqualTo("local-key"));
            Assert.That(config.OpenAI, Is.Null);
            Assert.That(config.AzureOpenAI, Is.Null);
            Assert.That(config.LocalAI, Is.Null);
        });
    }

    [Test]
    public void MigrateLegacyKeys_WhenNoLegacyKeys_ReturnsFalse()
    {
        // Arrange
        var config = new ApiKeysConfig();

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.That(migrated, Is.False);
        Assert.That(config.AIConnectionKeys, Is.Empty);
    }

    [Test]
    public void MigrateLegacyKeys_WhenAlreadyMigrated_DoesNotOverwrite()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "sk-old-key",
            AIConnectionKeys = new Dictionary<string, string>
            {
                ["openai"] = "sk-new-key"
            }
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.False, "Should not migrate when key already exists");
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-new-key"), "Should preserve existing key");
            Assert.That(config.OpenAI, Is.EqualTo("sk-old-key"), "Should not clear legacy key if not migrated");
        });
    }

    [Test]
    public void MigrateLegacyKeys_IsIdempotent()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "sk-test-key"
        };

        // Act - Migrate twice
        var firstMigration = config.MigrateLegacyKeys();
        var secondMigration = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstMigration, Is.True, "First migration should return true");
            Assert.That(secondMigration, Is.False, "Second migration should return false");
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-test-key"));
            Assert.That(config.AIConnectionKeys.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void MigrateLegacyKeys_WithEmptyString_DoesNotMigrate()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = ""
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.False);
            Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.False);
        });
    }

    [Test]
    public void MigrateLegacyKeys_WithWhitespace_DoesNotMigrate()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "   ",
            AzureOpenAI = "\t",
            LocalAI = "  \n  "
        };

        // Act
        var migrated = config.MigrateLegacyKeys();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.False);
            Assert.That(config.AIConnectionKeys, Is.Empty);
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(config.AIConnectionKeys.ContainsKey("openai"), Is.True);
            Assert.That(config.AIConnectionKeys["openai"], Is.EqualTo("sk-new-key"));
        });
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
    public void HasAnyKey_WithLegacyOpenAI_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            OpenAI = "sk-legacy-key"
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    [Test]
    public void HasAnyKey_WithLegacyAzureOpenAI_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            AzureOpenAI = "azure-legacy-key"
        };

        // Act & Assert
        Assert.That(config.HasAnyKey(), Is.True);
    }

    [Test]
    public void HasAnyKey_WithLegacyLocalAI_ReturnsTrue()
    {
        // Arrange
        var config = new ApiKeysConfig
        {
            LocalAI = "local-legacy-key"
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
            SendGrid = "\t",
            OpenAI = "  \n  "
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
            SendGrid = "",
            OpenAI = ""
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
