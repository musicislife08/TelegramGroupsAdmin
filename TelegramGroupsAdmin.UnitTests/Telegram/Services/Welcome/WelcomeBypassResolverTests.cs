using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

[TestFixture]
public class WelcomeBypassResolverTests
{
    private const long TestUserId = 11111L;
    private const long TestChatId = -22222L;

    private IBotUserService _botUserService = null!;
    private ITelegramUserMappingRepository _mappingRepo = null!;
    private ITelegramUserRepository _userRepo = null!;
    private IConfigService _configService = null!;
    private WelcomeBypassResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _botUserService = Substitute.For<IBotUserService>();
        _mappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _userRepo = Substitute.For<ITelegramUserRepository>();
        _configService = Substitute.For<IConfigService>();

        var services = new ServiceCollection();
        services.AddSingleton(_botUserService);
        services.AddSingleton(_mappingRepo);
        services.AddSingleton(_userRepo);
        services.AddSingleton(_configService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _resolver = new WelcomeBypassResolver(scopeFactory, NullLogger<WelcomeBypassResolver>.Instance);
    }

    private static UserIdentity TestUser() => UserIdentity.FromId(TestUserId);
    private static ChatIdentity TestChat() => ChatIdentity.FromId(TestChatId);

    private void StubChatMember(ChatMemberStatus status)
    {
        ChatMember member = status switch
        {
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = new User { Id = TestUserId } },
            ChatMemberStatus.Creator => new ChatMemberOwner { User = new User { Id = TestUserId } },
            ChatMemberStatus.Member => new ChatMemberMember { User = new User { Id = TestUserId } },
            _ => new ChatMemberMember { User = new User { Id = TestUserId } },
        };
        _botUserService.GetChatMemberAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>()).Returns(member);
    }

    [Test]
    public async Task Resolve_ChatAdmin_ReturnsChatAdmin()
    {
        StubChatMember(ChatMemberStatus.Administrator);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
    }

    [Test]
    public async Task Resolve_ChatCreator_ReturnsChatAdmin()
    {
        StubChatMember(ChatMemberStatus.Creator);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
    }

    [Test]
    public async Task Resolve_LinkedOwner_ReturnsWebAdmin()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)PermissionLevel.Owner);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
    }

    [Test]
    public async Task Resolve_LinkedGlobalAdmin_ReturnsWebAdmin()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)PermissionLevel.GlobalAdmin);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
    }

    [Test]
    public async Task Resolve_LinkedChatLevelAdmin_FallsThroughToTrustCheck()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)PermissionLevel.Admin);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = false } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(false);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_UnlinkedTrustedUser_ToggleOn_ReturnsTrusted()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Trusted));
    }

    [Test]
    public async Task Resolve_TrustedUser_ToggleOff_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = false } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_UnlinkedUntrustedUser_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(false);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_NullConfig_FallsBackToDefaultToggleOff_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((PermissionLevel?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns((WelcomeConfig?)null);
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }
}
