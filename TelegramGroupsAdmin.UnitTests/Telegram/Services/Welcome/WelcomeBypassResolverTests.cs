using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

[TestFixture]
public class WelcomeBypassResolverTests
{
    private const long UserId = 11111L;
    private const long ChatId = -22222L;

    private IChatAdminsRepository _chatAdminsRepo = null!;
    private ITelegramUserMappingRepository _mappingRepo = null!;
    private ITelegramUserRepository _userRepo = null!;
    private IConfigService _configService = null!;
    private WelcomeBypassResolver _resolver = null!;

    private static UserIdentity User => UserIdentity.FromId(UserId);
    private static ChatIdentity Chat => ChatIdentity.FromId(ChatId);

    [SetUp]
    public void SetUp()
    {
        _chatAdminsRepo = Substitute.For<IChatAdminsRepository>();
        _mappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _userRepo = Substitute.For<ITelegramUserRepository>();
        _configService = Substitute.For<IConfigService>();

        var services = new ServiceCollection();
        services.AddSingleton(_chatAdminsRepo);
        services.AddSingleton(_mappingRepo);
        services.AddSingleton(_userRepo);
        services.AddSingleton(_configService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _resolver = new WelcomeBypassResolver(scopeFactory, NullLogger<WelcomeBypassResolver>.Instance);
    }

    [Test]
    public async Task ResolveAsync_UserIsChatAdminElsewhere_ReturnsAdminWithReason()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long> { 1001L, 1002L });
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.Admin));
            Assert.That(result.ReasonDetail, Does.Contain("Telegram chat admin"));
            Assert.That(result.ReasonDetail, Does.Contain("2"));
        });
    }

    [Test]
    public async Task ResolveAsync_UserIsGlobalAdmin_ReturnsAdminWithReason()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long>());
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.GlobalAdmin);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.Admin));
            Assert.That(result.ReasonDetail, Does.Contain("web admin"));
            Assert.That(result.ReasonDetail, Does.Contain("GlobalAdmin"));
        });
    }

    [Test]
    public async Task ResolveAsync_UserIsOwner_ReturnsAdminWithReason()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long>());
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.Owner);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.Admin));
            Assert.That(result.ReasonDetail, Does.Contain("web admin"));
            Assert.That(result.ReasonDetail, Does.Contain("Owner"));
        });
    }

    [Test]
    public async Task ResolveAsync_UserIsTrusted_ToggleEnabled_ReturnsTrustedWithReason()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long>());
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig { TrustedBypass = { Enabled = true } });
        _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.Trusted));
            Assert.That(result.ReasonDetail, Does.Contain("Trusted"));
        });
    }

    [Test]
    public async Task ResolveAsync_UserIsTrusted_ToggleOff_ReturnsNone()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long>());
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig { TrustedBypass = { Enabled = false } });
        _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.None));
            Assert.That(result.ReasonDetail, Is.Null);
        });
    }

    [Test]
    public async Task ResolveAsync_NothingMatches_ReturnsNone()
    {
        _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<long>());
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)PermissionLevel.Admin); // lowest — not enough
        _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig { TrustedBypass = { Enabled = true } });
        _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _resolver.ResolveAsync(User, Chat, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(BypassDecision.None));
            Assert.That(result.ReasonDetail, Is.Null);
        });
    }
}
