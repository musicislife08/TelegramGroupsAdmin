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

[TestFixture]
public class ConfigServiceNameMaskingTests
{
    private IConfigRepository _repository = null!;
    private ServiceProvider _serviceProvider = null!;
    private ConfigService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IConfigRepository>();

        var services = new ServiceCollection();
        services.AddHybridCache();
        _serviceProvider = services.BuildServiceProvider();
        var cache = _serviceProvider.GetRequiredService<HybridCache>();

        _sut = new ConfigService(
            _repository,
            Substitute.For<IContentDetectionConfigRepository>(),
            Substitute.For<IAuditService>(),
            cache,
            NullLogger<ConfigService>.Instance);
    }

    [TearDown]
    public void TearDown() => _serviceProvider.Dispose();

    private void Effective(long chatId, bool scanEnabled, bool mask) =>
        _repository.GetEffectiveWelcomeAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(new WelcomeConfig
            {
                JoinSecurity = new JoinSecurityConfig
                {
                    ProfileScan = new ProfileScanConfig { Enabled = scanEnabled, MaskFlaggedNames = mask }
                }
            });

    [Test]
    public async Task ChatOverrideOff_ReturnsOff()
    {
        Effective(-100, scanEnabled: true, mask: false);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.Off));
    }

    [Test]
    public async Task ChatEffectiveOn_ReturnsOn()
    {
        Effective(-100, scanEnabled: true, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task NullChat_UsesGlobalRow()
    {
        Effective(0, scanEnabled: true, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(null), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task ScanDisabledInChat_StillMasksWhenSettingOn()
    {
        // Verdicts are per account, so a flag from another chat's scan masks here too.
        Effective(-100, scanEnabled: false, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task NoConfig_ReturnsOff()
    {
        _repository.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WelcomeConfig?)null);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.Off));
    }
}
