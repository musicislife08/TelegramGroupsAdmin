using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Unit tests for SystemConfigRepository's strict reads of the global config row (sendgrid_config, api_keys):
/// the strict Read* methods report unreadable storage, while the lenient Get* methods keep their defaults.
/// Uses EF Core InMemory and an ephemeral data-protection key ring - no database required.
/// </summary>
[TestFixture]
public class SystemConfigRepositoryTests
{
    private AppDbContext _context = null!;
    private IDataProtectionProvider _dataProtection = null!;
    private SystemConfigRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        var contextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        // The repository disposes the context it gets; hand out a fresh one over the same in-memory store each time.
        contextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new AppDbContext(options)));

        _dataProtection = new EphemeralDataProtectionProvider();
        _repository = new SystemConfigRepository(
            Substitute.For<ILogger<SystemConfigRepository>>(), contextFactory, _dataProtection);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private async Task StoreGlobalRowAsync(string? sendGridJson = null, string? apiKeysPlaintext = null)
    {
        var protector = _dataProtection.CreateProtector(DataProtectionPurposes.ApiKeys);
        _context.Configs.Add(new ConfigRecordDto
        {
            ChatId = 0,
            SendGridConfig = sendGridJson,
            ApiKeys = apiKeysPlaintext is null ? null : protector.Protect(apiKeysPlaintext),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    #region SendGrid config

    [Test]
    public async Task ReadSendGridConfigAsync_WithCorruptJson_ReportsUnreadable_WhileTheLenientReadReturnsTheDefault()
    {
        // Arrange
        await StoreGlobalRowAsync(sendGridJson: "{\"enabled\": true, \"fromAddress\": ");

        // Act
        var strict = await _repository.ReadSendGridConfigAsync();
        var lenient = await _repository.GetSendGridConfigAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strict.Status, Is.EqualTo(SendGridConfigReadStatus.Unreadable));
            Assert.That(strict.Config, Is.Null);
            Assert.That(lenient, Is.Not.Null);
            Assert.That(lenient!.Enabled, Is.False, "the lenient read keeps returning a default (disabled) config");
        }
    }

    [Test]
    public async Task ReadSendGridConfigAsync_WithValidJson_ReportsParsed()
    {
        // Arrange
        await StoreGlobalRowAsync(sendGridJson: JsonSerializer.Serialize(
            new SendGridConfig { Enabled = true, FromAddress = "noreply@unit.test", FromName = "Unit" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // Act
        var strict = await _repository.ReadSendGridConfigAsync();
        var lenient = await _repository.GetSendGridConfigAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strict.Status, Is.EqualTo(SendGridConfigReadStatus.Parsed));
            Assert.That(strict.Config!.Enabled, Is.True);
            Assert.That(strict.Config.FromAddress, Is.EqualTo("noreply@unit.test"));
            Assert.That(lenient!.FromAddress, Is.EqualTo("noreply@unit.test"));
        }
    }

    [TestCase(null, Description = "column NULL")]
    [TestCase("null", Description = "JSON null literal")]
    public async Task ReadSendGridConfigAsync_WithNothingStored_ReportsNotStored(string? stored)
    {
        // Arrange
        await StoreGlobalRowAsync(sendGridJson: stored);

        // Act
        var strict = await _repository.ReadSendGridConfigAsync();
        var lenient = await _repository.GetSendGridConfigAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strict.Status, Is.EqualTo(SendGridConfigReadStatus.NotStored));
            Assert.That(lenient!.Enabled, Is.False);
        }
    }

    #endregion

    #region API keys

    [Test]
    public async Task ReadApiKeysAsync_WhenTheCiphertextDecryptsToAJsonNull_ReportsUndecryptable_WhileTheLenientReadReturnsNull()
    {
        // Arrange - nothing the app writes decrypts to "null" (SaveApiKeysAsync serialises an object)
        await StoreGlobalRowAsync(apiKeysPlaintext: "null");

        // Act
        var strict = await _repository.ReadApiKeysAsync();
        var lenient = await _repository.GetApiKeysAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strict.Status, Is.EqualTo(ApiKeysReadStatus.Undecryptable));
            Assert.That(strict.Keys, Is.Null);
            Assert.That(lenient, Is.Null);
        }
    }

    [Test]
    public async Task ReadApiKeysAsync_WithCiphertextFromAnotherKeyRing_ReportsUndecryptable()
    {
        // Arrange - protected under a key ring this repository does not hold
        var foreign = new EphemeralDataProtectionProvider().CreateProtector(DataProtectionPurposes.ApiKeys);
        _context.Configs.Add(new ConfigRecordDto { ChatId = 0, ApiKeys = foreign.Protect("{}"), CreatedAt = DateTimeOffset.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        var strict = await _repository.ReadApiKeysAsync();

        // Assert
        Assert.That(strict.Status, Is.EqualTo(ApiKeysReadStatus.Undecryptable));
    }

    [Test]
    public async Task ReadApiKeysAsync_WithDecryptableKeys_ReportsDecrypted()
    {
        // Arrange
        await StoreGlobalRowAsync(apiKeysPlaintext: "{\"sendGrid\": \"SG.unit-test-key\"}");

        // Act
        var strict = await _repository.ReadApiKeysAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strict.Status, Is.EqualTo(ApiKeysReadStatus.Decrypted));
            Assert.That(strict.Keys!.SendGrid, Is.EqualTo("SG.unit-test-key"));
        }
    }

    [Test]
    public async Task ReadApiKeysAsync_WithNoCiphertext_ReportsNotStored()
    {
        // Arrange
        await StoreGlobalRowAsync();

        // Act
        var strict = await _repository.ReadApiKeysAsync();

        // Assert
        Assert.That(strict.Status, Is.EqualTo(ApiKeysReadStatus.NotStored));
    }

    #endregion
}
