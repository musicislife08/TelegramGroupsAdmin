using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands;

/// <summary>
/// Tests that CommandRouter correctly wires GetPermissionLevelAsync → PermissionResolver.Resolve
/// → permission gate, producing a denial or executing the command accordingly.
/// </summary>
[TestFixture]
public class CommandRouterTests
{
    private const string DenialMessage = "❌ This command is only available to group administrators.";
    private const string ExecutedSentinel = "EXECUTED";

    // Keyed under the real "ban" command name; MinPermissionLevel = Admin
    private sealed class StubBanCommand : IBotCommand
    {
        public string Name => "ban";
        public string Description => "Stub ban command";
        public string Usage => "/ban";
        public PermissionLevel MinPermissionLevel => PermissionLevel.Admin;
        public bool RequiresReply => false;
        public bool DeleteCommandMessage => false;
        public int? DeleteResponseAfterSeconds => null;

        public bool WasExecuted { get; private set; }

        public Task<CommandResult> ExecuteAsync(
            Message message,
            string[] args,
            PermissionLevel userPermission,
            CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.FromResult(new CommandResult(TelegramMessage.Plain(ExecutedSentinel), false));
        }
    }

    private static readonly Message BanMessage = new()
    {
        Text = "/ban",
        From = new User { Id = 123 },
        Chat = new Chat { Id = -1001 }
    };

    private static (CommandRouter router, StubBanCommand stub, ITelegramUserMappingRepository mappingRepo, IChatAdminsRepository chatAdminsRepo)
        BuildRouter()
    {
        var stub = new StubBanCommand();
        var mappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        var chatAdminsRepo = Substitute.For<IChatAdminsRepository>();

        var services = new ServiceCollection();

        // Register the stub as the keyed "ban" command
        services.AddKeyedScoped<IBotCommand>(CommandNames.Ban, (_, _) => stub);

        // Register all other commands as minimal stubs (CommandRouter.GetAvailableCommands
        // iterates CommandNames.All; we need every name registered to avoid exceptions)
        foreach (var name in CommandNames.All.Where(n => n != CommandNames.Ban))
        {
            var otherName = name; // capture
            services.AddKeyedScoped<IBotCommand>(otherName, (_, _) => new MinimalStub(otherName));
        }

        // Register repositories as scoped so CreateScope() resolves the substitutes
        services.AddScoped(_ => mappingRepo);
        services.AddScoped(_ => chatAdminsRepo);

        var provider = services.BuildServiceProvider();
        var router = new CommandRouter(
            NullLogger<CommandRouter>.Instance,
            provider,
            new PipelineMetrics());

        return (router, stub, mappingRepo, chatAdminsRepo);
    }

    /// <summary>Minimal do-nothing command for names other than "ban".</summary>
    private sealed class MinimalStub(string name) : IBotCommand
    {
        public string Name => name;
        public string Description => string.Empty;
        public string Usage => $"/{name}";
        public PermissionLevel MinPermissionLevel => PermissionLevel.Member;
        public bool RequiresReply => false;
        public bool DeleteCommandMessage => false;
        public int? DeleteResponseAfterSeconds => null;

        public Task<CommandResult> ExecuteAsync(Message message, string[] args, PermissionLevel userPermission, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult(TelegramMessage.Plain("other"), false));
    }

    [Test]
    public async Task Member_Denied_WhenBelowAdmin()
    {
        // Arrange: no web tier, not a chat admin → effective Member → below Admin
        var (router, stub, mappingRepo, chatAdminsRepo) = BuildRouter();
        mappingRepo.GetPermissionLevelByTelegramIdAsync(123L, Arg.Any<CancellationToken>())
                   .Returns((PermissionLevel?)null);
        chatAdminsRepo.IsAdminAsync(-1001L, 123L, Arg.Any<CancellationToken>())
                      .Returns(false);

        // Act
        var result = await router.RouteCommandAsync(BanMessage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Message.Text, Does.Contain(DenialMessage));
        Assert.That(stub.WasExecuted, Is.False, "Command must not execute when permission denied");
    }

    [Test]
    public async Task NativeChatAdmin_Allowed()
    {
        // Arrange: no web tier, IS a chat admin → effective Admin ≥ Admin
        var (router, stub, mappingRepo, chatAdminsRepo) = BuildRouter();
        mappingRepo.GetPermissionLevelByTelegramIdAsync(123L, Arg.Any<CancellationToken>())
                   .Returns((PermissionLevel?)null);
        chatAdminsRepo.IsAdminAsync(-1001L, 123L, Arg.Any<CancellationToken>())
                      .Returns(true);

        // Act
        var result = await router.RouteCommandAsync(BanMessage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Message.Text, Is.EqualTo(ExecutedSentinel));
        Assert.That(stub.WasExecuted, Is.True, "Command must execute for native chat admin");
    }

    [Test]
    public async Task WebAdmin_InChatTheyDontAdminister_IsDenied()
    {
        // Arrange: web tier = Admin, NOT a chat admin in this chat → effective Member → denied.
        // This is the canonical chat-scoping rule: web Admin only counts where they're also
        // a Telegram admin/creator in the specific chat.
        var (router, stub, mappingRepo, chatAdminsRepo) = BuildRouter();
        mappingRepo.GetPermissionLevelByTelegramIdAsync(123L, Arg.Any<CancellationToken>())
                   .Returns(PermissionLevel.Admin);
        chatAdminsRepo.IsAdminAsync(-1001L, 123L, Arg.Any<CancellationToken>())
                      .Returns(false);

        // Act
        var result = await router.RouteCommandAsync(BanMessage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Message.Text, Does.Contain(DenialMessage));
        Assert.That(stub.WasExecuted, Is.False, "Web Admin without chat admin status must be denied");
    }

    [Test]
    public async Task GlobalAdmin_Allowed_Anywhere()
    {
        // Arrange: GlobalAdmin bypasses chat-admin requirement → effective GlobalAdmin ≥ Admin
        var (router, stub, mappingRepo, chatAdminsRepo) = BuildRouter();
        mappingRepo.GetPermissionLevelByTelegramIdAsync(123L, Arg.Any<CancellationToken>())
                   .Returns(PermissionLevel.GlobalAdmin);
        chatAdminsRepo.IsAdminAsync(-1001L, 123L, Arg.Any<CancellationToken>())
                      .Returns(false);

        // Act
        var result = await router.RouteCommandAsync(BanMessage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Message.Text, Is.EqualTo(ExecutedSentinel));
        Assert.That(stub.WasExecuted, Is.True, "GlobalAdmin must execute in any chat");
    }
}
