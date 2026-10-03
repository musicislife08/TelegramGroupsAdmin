using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// BanCallbackService's click-time gate: the /ban picker is posted in the group, so only a user who is
/// an admin in that chat at the moment of the click may use it.
/// </summary>
[TestFixture]
public class BanCallbackServiceTests
{
    private const long ChatId = -1001L;
    private const long ClickerId = 555L;
    private const long TargetId = 777L;

    private ITelegramPermissionService _permissionService = null!;
    private ITelegramUserRepository _userRepository = null!;
    private IUserIdentityService _identities = null!;
    private IBotModerationService _moderation = null!;
    private ServiceProvider _provider = null!;
    private BanCallbackService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _permissionService = Substitute.For<ITelegramPermissionService>();
        _userRepository = Substitute.For<ITelegramUserRepository>();
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(ClickerId, Arg.Any<CancellationToken>()).Returns(UserIdentity.ForTest(ClickerId, "Stored", "Admin"));
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = false, ErrorMessage = "test stops here" });

        var services = new ServiceCollection();
        services.AddScoped(_ => _permissionService);
        services.AddScoped(_ => _userRepository);
        services.AddScoped(_ => Substitute.For<IBotMessageService>());
        services.AddScoped(_ => _identities);
        services.AddScoped(_ => _moderation);
        services.AddScoped(_ => Substitute.For<IChatAdminsRepository>());
        _provider = services.BuildServiceProvider();

        _service = new BanCallbackService(
            NullLogger<BanCallbackService>.Instance,
            _provider.GetRequiredService<IServiceScopeFactory>());
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private static CallbackQuery Click(string data) => new()
    {
        Id = "cb1",
        Data = data,
        From = new User { Id = ClickerId, FirstName = "Clicker" },
        Message = new Message { Id = 42, Chat = new Chat { Id = ChatId } }
    };

    [Test]
    public async Task HandleCallbackAsync_NonAdminClicker_IsIgnored()
    {
        _permissionService.GetEffectiveLevelAsync(ChatId, ClickerId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.Member);

        await _service.HandleCallbackAsync(Click($"ban_select:{TargetId}:10"));

        // Ignored before the ban flow starts: the target is never even looked up.
        await _userRepository.DidNotReceiveWithAnyArgs().GetByTelegramIdAsync(default, default);
    }

    [Test]
    public async Task HandleCallbackAsync_AdminClicker_ProceedsToBanFlow()
    {
        _permissionService.GetEffectiveLevelAsync(ChatId, ClickerId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.Admin);

        // Target not found ends the flow right after the lookup, which is all this test needs to observe.
        await _service.HandleCallbackAsync(Click($"ban_select:{TargetId}:10"));

        await _userRepository.Received(1).GetByTelegramIdAsync(TargetId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_AdminClicker_BansAsTheResolvedClicker()
    {
        _permissionService.GetEffectiveLevelAsync(ChatId, ClickerId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.Admin);
        _userRepository.GetByTelegramIdAsync(TargetId, Arg.Any<CancellationToken>()).Returns(new TelegramUser(
            TelegramUserId: TargetId, Username: "target", FirstName: "Target", LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));

        await _service.HandleCallbackAsync(Click($"ban_select:{TargetId}:10"));

        // The executor is resolved by id, not built from the callback's names.
        await _identities.Received(1).ResolveAsync(ClickerId, Arg.Any<CancellationToken>());
        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.Executor.TelegramUserId == ClickerId && i.Executor.DisplayName == "Stored Admin"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_AdminClicker_BansTheTargetResolvedById()
    {
        _permissionService.GetEffectiveLevelAsync(ChatId, ClickerId, Arg.Any<CancellationToken>())
            .Returns(PermissionLevel.Admin);
        _userRepository.GetByTelegramIdAsync(TargetId, Arg.Any<CancellationToken>()).Returns(new TelegramUser(
            TelegramUserId: TargetId, Username: "row", FirstName: "Row", LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));
        var target = UserIdentity.ForTest(TargetId, "Resolved", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(target);

        await _service.HandleCallbackAsync(Click($"ban_select:{TargetId}:10"));

        await _moderation.Received(1).BanUserAsync(Arg.Is<BanIntent>(i => i!.User == target), Arg.Any<CancellationToken>());
    }
}
