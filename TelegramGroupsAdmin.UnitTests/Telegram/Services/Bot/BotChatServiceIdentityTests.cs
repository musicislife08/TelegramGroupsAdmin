using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Metrics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

/// <summary>
/// BotChatService records admins' names through IUserIdentityService wherever it writes a
/// chat_admins row (the row's foreign key needs the user row), and never rescans them: admins are
/// trusted.
/// </summary>
[TestFixture]
public class BotChatServiceIdentityTests
{
    private const long ChatId = -100555;
    private const long AdminId = 7;

    private IBotChatHandler _chatHandler = null!;
    private IChatAdminsRepository _chatAdmins = null!;
    private ITelegramUserRepository _users = null!;
    private IUserIdentityService _identities = null!;
    private IAdminNotificationService _notifications = null!;
    private BotChatService _sut = null!;

    private static readonly Chat Group = new() { Id = ChatId, Type = ChatType.Supergroup, Title = "Group" };
    private static readonly User Admin = new() { Id = AdminId, FirstName = "Ada", Username = "ada", IsBot = false };

    [SetUp]
    public void SetUp()
    {
        _chatHandler = Substitute.For<IBotChatHandler>();
        _chatAdmins = Substitute.For<IChatAdminsRepository>();
        _users = Substitute.For<ITelegramUserRepository>();
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(ci => UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "Stored"));
        _notifications = Substitute.For<IAdminNotificationService>();

        _sut = new BotChatService(
            _chatHandler,
            Substitute.For<IChatCache>(),
            Substitute.For<IChatHealthCache>(),
            Substitute.For<IConfigService>(),
            Substitute.For<IManagedChatsRepository>(),
            _chatAdmins,
            _users,
            _identities,
            Substitute.For<IUserActionsRepository>(),
            _notifications,
            new ApiMetrics(),
            NullLogger<BotChatService>.Instance);
    }

    [Test]
    public async Task AdminPromotion_ObservesPromotedUserWithUpdateDate_BeforeWritingAdminRow()
    {
        var update = new ChatMemberUpdated
        {
            Chat = Group,
            From = Admin,
            Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            OldChatMember = new ChatMemberMember { User = Admin },
            NewChatMember = new ChatMemberAdministrator { User = Admin }
        };

        await _sut.HandleAdminStatusChangeAsync(update, CancellationToken.None);

        Received.InOrder(() =>
        {
            _identities.ObserveAsync(
                Arg.Is<ObservedUser>(o => o!.Id == AdminId
                    && o.Source == ObservationSource.ChatMember
                    && o.ObservedAt == new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)),
                Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == ChatId),
                RenameRescan.None,
                Arg.Any<CancellationToken>());
            _chatAdmins.UpsertAsync(ChatId, AdminId, false, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AdminDemotion_DoesNotObserve()
    {
        var update = new ChatMemberUpdated
        {
            Chat = Group,
            From = Admin,
            Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            OldChatMember = new ChatMemberAdministrator { User = Admin },
            NewChatMember = new ChatMemberMember { User = Admin }
        };

        await _sut.HandleAdminStatusChangeAsync(update, CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
    }

    [Test]
    public async Task AdminDemotion_DeactivatesAndNotifiesWithTheIdentityResolvedById()
    {
        var resolved = UserIdentity.ForTest(AdminId, "Resolved");
        _identities.ResolveAsync(AdminId, Arg.Any<CancellationToken>()).Returns(resolved);
        var update = new ChatMemberUpdated
        {
            Chat = Group,
            From = Admin,
            Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            OldChatMember = new ChatMemberAdministrator { User = Admin },
            NewChatMember = new ChatMemberMember { User = Admin }
        };

        await _sut.HandleAdminStatusChangeAsync(update, CancellationToken.None);

        await _chatAdmins.Received(1).DeactivateAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is(resolved), Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendAdminChangedAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is(resolved), Arg.Is(false), Arg.Is(false), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MyChatMemberDemotion_DeactivatesTheIdentityResolvedById()
    {
        var resolved = UserIdentity.ForTest(AdminId, "Resolved");
        _identities.ResolveAsync(AdminId, Arg.Any<CancellationToken>()).Returns(resolved);
        var update = new ChatMemberUpdated
        {
            Chat = Group,
            From = Admin,
            Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            OldChatMember = new ChatMemberAdministrator { User = Admin },
            NewChatMember = new ChatMemberMember { User = Admin }
        };

        await _sut.HandleBotMembershipUpdateAsync(update, CancellationToken.None);

        await _chatAdmins.Received(1).DeactivateAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is(resolved), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshChatAdmins_ObservesEachAdminNow_WithoutRescan()
    {
        _chatHandler.GetChatAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ChatFullInfo { Id = ChatId, Type = ChatType.Supergroup, Title = "Group" });
        _chatHandler.GetChatAdministratorsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([new ChatMemberOwner { User = Admin }]);
        _chatAdmins.GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>()).Returns([]);
        _users.IsTrustedAsync(AdminId, Arg.Any<CancellationToken>()).Returns(true);
        var before = DateTimeOffset.UtcNow;

        await _sut.RefreshChatAdminsAsync(new ChatIdentity(ChatId, "Group"), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == AdminId
                && o.FirstName == "Ada"
                && o.Source == ObservationSource.ChatMember
                && o.ObservedAt >= before && o.ObservedAt <= after),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == ChatId),
            RenameRescan.None,
            Arg.Any<CancellationToken>());
        await _chatAdmins.Received(1).UpsertAsync(ChatId, AdminId, true, Arg.Any<CancellationToken>());
        // Already trusted: no trust write.
        await _users.DidNotReceiveWithAnyArgs().TrustUserAsync(default);
    }

    [Test]
    public async Task RefreshChatAdmins_UntrustedAdmin_IsTrusted()
    {
        _chatHandler.GetChatAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ChatFullInfo { Id = ChatId, Type = ChatType.Supergroup, Title = "Group" });
        _chatHandler.GetChatAdministratorsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([new ChatMemberAdministrator { User = Admin }]);
        _chatAdmins.GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>()).Returns([]);
        _users.IsTrustedAsync(AdminId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.RefreshChatAdminsAsync(new ChatIdentity(ChatId, "Group"), CancellationToken.None);

        await _users.Received(1).TrustUserAsync(AdminId, Arg.Any<CancellationToken>());
    }
}
