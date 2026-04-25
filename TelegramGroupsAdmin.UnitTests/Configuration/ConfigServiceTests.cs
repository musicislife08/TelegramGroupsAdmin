using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Unit tests for the typed ConfigService.
/// Verifies repo delegation, audit emission, and cache invalidation paths.
/// </summary>
[TestFixture]
public class ConfigServiceTests
{
    private IConfigRepository _repo = null!;
    private IContentDetectionConfigRepository _cdRepo = null!;
    private IAuditService _audit = null!;
    private HybridCache _cache = null!;
    private ServiceProvider _serviceProvider = null!;
    private ConfigService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IConfigRepository>();
        _cdRepo = Substitute.For<IContentDetectionConfigRepository>();
        _audit = Substitute.For<IAuditService>();

        var services = new ServiceCollection();
        services.AddHybridCache();
        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<HybridCache>();

        _sut = new ConfigService(_repo, _cdRepo, _audit, _cache, NullLogger<ConfigService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    // ------------------------------------------------------------------
    // Welcome
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveWelcomeAsync_DelegatesToRepoAndEmitsAudit()
    {
        var chat = new ChatIdentity(42, "Test Chat");
        var config = new WelcomeConfig { Enabled = true };
        var actor = Actor.FromWebUser("user-1", "test@example.com");

        await _sut.SaveWelcomeAsync(chat, config, actor);

        await _repo.Received(1).SaveWelcomeAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("Welcome") && v.Contains("Test Chat")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteWelcomeAsync_EmitsDeletedAudit()
    {
        var chat = new ChatIdentity(42, "Test Chat");
        var actor = Actor.FromWebUser("user-1", "u@e.com");

        await _sut.DeleteWelcomeAsync(chat, actor);

        await _repo.Received(1).DeleteWelcomeAsync(chat, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("deleted")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveWelcomeAsync_GlobalScope_StillEmitsAudit()
    {
        var chat = new ChatIdentity(0, "global");
        var config = new WelcomeConfig();
        var actor = Actor.SystemSeed;

        await _sut.SaveWelcomeAsync(chat, config, actor);

        await _repo.Received(1).SaveWelcomeAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Log
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveLogAsync_DelegatesToRepoAndEmitsAudit()
    {
        var chat = ChatIdentity.FromId(0);
        var config = new LogConfig();
        var actor = Actor.FromSystem("runtime_logging");

        await _sut.SaveLogAsync(chat, config, actor);

        await _repo.Received(1).SaveLogAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("Log")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteLogAsync_EmitsDeletedAudit()
    {
        var chat = ChatIdentity.FromId(0);
        var actor = Actor.FromSystem("runtime_logging");

        await _sut.DeleteLogAsync(chat, actor);

        await _repo.Received(1).DeleteLogAsync(chat, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("deleted")),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // BotProtection
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveBotProtectionAsync_DelegatesToRepoAndEmitsAudit()
    {
        var chat = new ChatIdentity(99, "Some Chat");
        var config = new BotProtectionConfig { Enabled = true };
        var actor = Actor.FromWebUser("u", "u@e.com");

        await _sut.SaveBotProtectionAsync(chat, config, actor);

        await _repo.Received(1).SaveBotProtectionAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("BotProtection")),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // BanCelebration
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveBanCelebrationAsync_DelegatesToRepoAndEmitsAudit()
    {
        var chat = new ChatIdentity(123, "Chat A");
        var config = new BanCelebrationConfig { Enabled = true };
        var actor = Actor.FromWebUser("u", "u@e.com");

        await _sut.SaveBanCelebrationAsync(chat, config, actor);

        await _repo.Received(1).SaveBanCelebrationAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("BanCelebration")),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Bot token (security-critical: plaintext must not appear in audit)
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveBotTokenAsync_AuditValueDoesNotContainPlaintext()
    {
        var actor = Actor.FromWebUser("user-1", "u@e.com");
        const string secret = "1234567890:SECRET-TOKEN-VALUE";

        await _sut.SaveBotTokenAsync(secret, actor);

        await _repo.Received(1).SaveBotTokenAsync(secret, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => !v.Contains(secret)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveBotTokenAsync_WithBlankToken_Throws()
    {
        var actor = Actor.SystemSeed;

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _sut.SaveBotTokenAsync(string.Empty, actor));
    }

    // ------------------------------------------------------------------
    // ContentDetection helper delegates
    // ------------------------------------------------------------------

    [Test]
    public async Task GetCriticalCheckNamesAsync_DelegatesToContentDetectionRepo()
    {
        var expected = new HashSet<string> { "UrlBlocklist", "FileScanning" };
        _cdRepo.GetCriticalCheckNamesAsync(7, Arg.Any<CancellationToken>()).Returns(expected);

        var actual = await _sut.GetCriticalCheckNamesAsync(7);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
