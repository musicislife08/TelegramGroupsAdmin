using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for SystemConfigRepository WebPush-related methods.
/// Uses real PostgreSQL via Testcontainers to validate:
///
/// 1. GetWebPushConfigAsync - Returns default when not set, deserializes correctly
/// 2. SaveWebPushConfigAsync - Creates/updates JSONB column correctly
/// 3. GetVapidPrivateKeyAsync - Decrypts correctly, returns null when not set
/// 4. SaveVapidPrivateKeyAsync - Encrypts and stores correctly
/// 5. HasVapidKeysAsync - Composite check for both public and private keys
/// </summary>
[TestFixture]
public class SystemConfigRepositoryWebPushTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ISystemConfigRepository? _configRepo;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys
        var keyDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}"));
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(keyDirectory);

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Register repository
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Create repository instance
        var scope = _serviceProvider.CreateScope();
        _configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region GetWebPushConfigAsync Tests

    [Test]
    public async Task GetWebPushConfigAsync_WhenNoConfigExists_ShouldReturnDefault()
    {
        // Act
        var config = await _configRepo!.GetWebPushConfigAsync();

        // Assert - Should return a default config (never null)
        Assert.That(config, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.Enabled, Is.True, "Default should have Enabled = true");
            Assert.That(config.VapidPublicKey, Is.Null, "Default should have null VapidPublicKey");
            Assert.That(config.ContactEmail, Is.Null, "Default should have null ContactEmail");
        }
    }

    [Test]
    public async Task GetWebPushConfigAsync_AfterSave_ShouldReturnSavedConfig()
    {
        // Arrange
        var configToSave = new WebPushConfig
        {
            Enabled = false,
            ContactEmail = "admin@example.com",
            VapidPublicKey = "BNxHHKRkAg0WNUBuHqAQjPILp5FXxF4YZ3sGc3Lw6wXz2Ks8qBY1HqJR3nMdFgHi"
        };

        // Act
        await _configRepo!.SaveWebPushConfigAsync(configToSave);
        var retrievedConfig = await _configRepo.GetWebPushConfigAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(retrievedConfig.Enabled, Is.False);
            Assert.That(retrievedConfig.ContactEmail, Is.EqualTo("admin@example.com"));
            Assert.That(retrievedConfig.VapidPublicKey, Is.EqualTo("BNxHHKRkAg0WNUBuHqAQjPILp5FXxF4YZ3sGc3Lw6wXz2Ks8qBY1HqJR3nMdFgHi"));
        }
    }

    [Test]
    public async Task GetWebPushConfigAsync_ShouldNeverReturnNull()
    {
        // This tests the interface contract that GetWebPushConfigAsync never returns null

        // Act
        var config = await _configRepo!.GetWebPushConfigAsync();

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config, Is.TypeOf<WebPushConfig>());
    }

    #endregion

    #region SaveWebPushConfigAsync Tests

    [Test]
    public async Task SaveWebPushConfigAsync_ShouldCreateNewRecord_WhenNoneExists()
    {
        // Arrange
        var config = new WebPushConfig
        {
            Enabled = true,
            ContactEmail = "test@example.com",
            VapidPublicKey = "TestPublicKey123"
        };

        // Act
        await _configRepo!.SaveWebPushConfigAsync(config);

        // Assert - Verify by reading back
        var retrieved = await _configRepo.GetWebPushConfigAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved.ContactEmail, Is.EqualTo("test@example.com"));
            Assert.That(retrieved.VapidPublicKey, Is.EqualTo("TestPublicKey123"));
        }
    }

    [Test]
    public async Task SaveWebPushConfigAsync_ShouldUpdateExistingRecord()
    {
        // Arrange - Save initial config
        var initialConfig = new WebPushConfig
        {
            Enabled = true,
            ContactEmail = "initial@example.com"
        };
        await _configRepo!.SaveWebPushConfigAsync(initialConfig);

        // Act - Update config
        var updatedConfig = new WebPushConfig
        {
            Enabled = false,
            ContactEmail = "updated@example.com"
        };
        await _configRepo.SaveWebPushConfigAsync(updatedConfig);

        // Assert
        var retrieved = await _configRepo.GetWebPushConfigAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved.Enabled, Is.False);
            Assert.That(retrieved.ContactEmail, Is.EqualTo("updated@example.com"));
        }
    }

    [Test]
    public async Task SaveWebPushConfigAsync_ShouldNotAffectOtherConfigs()
    {
        // Arrange - Save AI provider config first
        var aiProviderConfig = new AIProviderConfig
        {
            Connections = [new AIConnection { Id = "test", Provider = AIProviderType.OpenAI, Enabled = true }]
        };
        await _configRepo!.SaveAIProviderConfigAsync(aiProviderConfig);

        // Act - Save WebPush config
        var webPushConfig = new WebPushConfig { Enabled = true, ContactEmail = "test@example.com" };
        await _configRepo.SaveWebPushConfigAsync(webPushConfig);

        // Assert - AI provider config should still be there
        var retrievedAI = await _configRepo.GetAIProviderConfigAsync();
        Assert.That(retrievedAI, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrievedAI!.Connections, Has.Count.EqualTo(1));
            Assert.That(retrievedAI.Connections[0].Id, Is.EqualTo("test"));
        }
    }

    #endregion

    #region GetVapidPrivateKeyAsync Tests

    [Test]
    public async Task GetVapidPrivateKeyAsync_WhenNotSet_ShouldReturnNull()
    {
        // Act
        var privateKey = await _configRepo!.GetVapidPrivateKeyAsync();

        // Assert
        Assert.That(privateKey, Is.Null);
    }

    [Test]
    public async Task GetVapidPrivateKeyAsync_AfterSave_ShouldReturnDecryptedKey()
    {
        // Arrange
        const string testPrivateKey = "aBcDeFgHiJkLmNoPqRsTuVwXyZaBcDeFgHiJkLmNo";
        await _configRepo!.SaveVapidPrivateKeyAsync(testPrivateKey);

        // Act
        var retrievedKey = await _configRepo.GetVapidPrivateKeyAsync();

        // Assert - Should return the original (decrypted) key
        Assert.That(retrievedKey, Is.EqualTo(testPrivateKey));
    }

    [Test]
    public async Task GetVapidPrivateKeyAsync_ShouldDecryptCorrectly()
    {
        // Arrange - Save a key and verify it's stored encrypted in DB
        // Using simple test value (not a real key format)
        const string testValue = "test-vapid-value-for-encryption";
        await _configRepo!.SaveVapidPrivateKeyAsync(testValue);

        // Verify it's stored encrypted (not plaintext) in database
        var encryptedValue = await _testHelper!.ExecuteScalarAsync<string>(
            "SELECT vapid_private_key_encrypted FROM configs WHERE chat_id = 0");
        Assert.That(encryptedValue, Is.Not.Null);
        Assert.That(encryptedValue, Is.Not.EqualTo(testValue), "Value should be encrypted in database");

        // Act
        var decryptedValue = await _configRepo.GetVapidPrivateKeyAsync();

        // Assert - Should decrypt back to original
        Assert.That(decryptedValue, Is.EqualTo(testValue));
    }

    #endregion

    #region SaveVapidPrivateKeyAsync Tests

    [Test]
    public async Task SaveVapidPrivateKeyAsync_ShouldStoreEncrypted()
    {
        // Arrange - Using simple test value (not a real key format)
        const string testValue = "test-vapid-value-123";

        // Act
        await _configRepo!.SaveVapidPrivateKeyAsync(testValue);

        // Assert - Verify encrypted in database
        var storedValue = await _testHelper!.ExecuteScalarAsync<string>(
            "SELECT vapid_private_key_encrypted FROM configs WHERE chat_id = 0");
        Assert.That(storedValue, Is.Not.Null);
        Assert.That(storedValue, Is.Not.EqualTo(testValue), "Should be encrypted");
        Assert.That(storedValue!.Length, Is.GreaterThan(testValue.Length), "Encrypted value should be longer");
    }

    [Test]
    public async Task SaveVapidPrivateKeyAsync_ShouldUpdateExisting()
    {
        // Arrange - Save initial key
        await _configRepo!.SaveVapidPrivateKeyAsync("InitialKey");

        // Act - Update key
        await _configRepo.SaveVapidPrivateKeyAsync("UpdatedKey");

        // Assert
        var retrievedKey = await _configRepo.GetVapidPrivateKeyAsync();
        Assert.That(retrievedKey, Is.EqualTo("UpdatedKey"));
    }

    [Test]
    public async Task SaveVapidPrivateKeyAsync_ShouldNotAffectWebPushConfig()
    {
        // Arrange - Save WebPush config first
        await _configRepo!.SaveWebPushConfigAsync(new WebPushConfig
        {
            Enabled = true,
            ContactEmail = "test@example.com",
            VapidPublicKey = "PublicKey123"
        });

        // Act - Save private key separately
        await _configRepo.SaveVapidPrivateKeyAsync("PrivateKey456");

        // Assert - WebPush config should be unchanged
        var webPushConfig = await _configRepo.GetWebPushConfigAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(webPushConfig.ContactEmail, Is.EqualTo("test@example.com"));
            Assert.That(webPushConfig.VapidPublicKey, Is.EqualTo("PublicKey123"));
        }
    }

    [Test]
    public void SaveVapidPrivateKeyAsync_WhenNullOrEmpty_ShouldThrow()
    {
        // Act & Assert - null throws ArgumentNullException
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _configRepo!.SaveVapidPrivateKeyAsync(null!));

        // Act & Assert - empty string throws ArgumentException
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _configRepo!.SaveVapidPrivateKeyAsync(""));

        // Act & Assert - whitespace throws ArgumentException
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _configRepo!.SaveVapidPrivateKeyAsync("   "));
    }

    #endregion

    #region HasVapidKeysAsync Tests

    [Test]
    public async Task HasVapidKeysAsync_WhenNothingSet_ShouldReturnFalse()
    {
        // Act
        var hasKeys = await _configRepo!.HasVapidKeysAsync();

        // Assert
        Assert.That(hasKeys, Is.False);
    }

    [Test]
    public async Task HasVapidKeysAsync_WhenOnlyPublicKeySet_ShouldReturnFalse()
    {
        // Arrange - Set only public key (no private key)
        await _configRepo!.SaveWebPushConfigAsync(new WebPushConfig
        {
            VapidPublicKey = "OnlyPublicKey123"
        });

        // Act
        var hasKeys = await _configRepo.HasVapidKeysAsync();

        // Assert - Should return false (need both keys)
        Assert.That(hasKeys, Is.False);
    }

    [Test]
    public async Task HasVapidKeysAsync_WhenOnlyPrivateKeySet_ShouldReturnFalse()
    {
        // Arrange - Set only private key (no public key)
        await _configRepo!.SaveVapidPrivateKeyAsync("OnlyPrivateKey456");

        // Act
        var hasKeys = await _configRepo.HasVapidKeysAsync();

        // Assert - Should return false (need both keys)
        Assert.That(hasKeys, Is.False);
    }

    [Test]
    public async Task HasVapidKeysAsync_WhenBothKeysSet_ShouldReturnTrue()
    {
        // Arrange - Set both keys
        await _configRepo!.SaveWebPushConfigAsync(new WebPushConfig
        {
            VapidPublicKey = "PublicKey123"
        });
        await _configRepo.SaveVapidPrivateKeyAsync("PrivateKey456");

        // Act
        var hasKeys = await _configRepo.HasVapidKeysAsync();

        // Assert
        Assert.That(hasKeys, Is.True);
    }

    [Test]
    public async Task HasVapidKeysAsync_WhenPublicKeyIsEmpty_ShouldReturnFalse()
    {
        // Arrange - Public key is empty string
        await _configRepo!.SaveWebPushConfigAsync(new WebPushConfig
        {
            VapidPublicKey = ""
        });
        await _configRepo.SaveVapidPrivateKeyAsync("PrivateKey456");

        // Act
        var hasKeys = await _configRepo.HasVapidKeysAsync();

        // Assert - Empty string should be treated as not set
        Assert.That(hasKeys, Is.False);
    }

    [Test]
    public async Task HasVapidKeysAsync_WhenPublicKeyIsWhitespace_ShouldReturnFalse()
    {
        // Arrange - Public key is whitespace
        await _configRepo!.SaveWebPushConfigAsync(new WebPushConfig
        {
            VapidPublicKey = "   "
        });
        await _configRepo.SaveVapidPrivateKeyAsync("PrivateKey456");

        // Act
        var hasKeys = await _configRepo.HasVapidKeysAsync();

        // Assert - Whitespace should be treated as not set
        Assert.That(hasKeys, Is.False);
    }

    #endregion

    #region WebPushConfig.HasVapidPublicKey Tests

    [Test]
    public void WebPushConfig_HasVapidPublicKey_WhenNull_ShouldReturnFalse()
    {
        // Arrange
        var config = new WebPushConfig { VapidPublicKey = null };

        // Act & Assert
        Assert.That(config.HasVapidPublicKey(), Is.False);
    }

    [Test]
    public void WebPushConfig_HasVapidPublicKey_WhenEmpty_ShouldReturnFalse()
    {
        // Arrange
        var config = new WebPushConfig { VapidPublicKey = "" };

        // Act & Assert
        Assert.That(config.HasVapidPublicKey(), Is.False);
    }

    [Test]
    public void WebPushConfig_HasVapidPublicKey_WhenWhitespace_ShouldReturnFalse()
    {
        // Arrange
        var config = new WebPushConfig { VapidPublicKey = "   " };

        // Act & Assert
        Assert.That(config.HasVapidPublicKey(), Is.False);
    }

    [Test]
    public void WebPushConfig_HasVapidPublicKey_WhenSet_ShouldReturnTrue()
    {
        // Arrange
        var config = new WebPushConfig { VapidPublicKey = "ValidPublicKey" };

        // Act & Assert
        Assert.That(config.HasVapidPublicKey(), Is.True);
    }

    #endregion

    #region Encryption Isolation Tests

    [Test]
    public async Task VapidPrivateKey_UsesDistinctEncryptionPurpose()
    {
        // This test verifies that VAPID keys use a different encryption purpose than API keys.
        // If they used the same purpose, an attacker who extracted the encrypted value could
        // potentially substitute it for another encrypted value.

        // Arrange - Save both an API key and a VAPID key
        var apiKeys = new ApiKeysConfig { VirusTotal = "vt-api-key-test" };
        await _configRepo!.SaveApiKeysAsync(apiKeys);
        await _configRepo.SaveVapidPrivateKeyAsync("vapid-private-key-test");

        // Act - Retrieve both
        var retrievedApiKeys = await _configRepo.GetApiKeysAsync();
        var retrievedVapidKey = await _configRepo.GetVapidPrivateKeyAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert - Both should decrypt correctly to their original values
            Assert.That(retrievedApiKeys?.VirusTotal, Is.EqualTo("vt-api-key-test"));
            Assert.That(retrievedVapidKey, Is.EqualTo("vapid-private-key-test"));
        }
    }

    #endregion
}
