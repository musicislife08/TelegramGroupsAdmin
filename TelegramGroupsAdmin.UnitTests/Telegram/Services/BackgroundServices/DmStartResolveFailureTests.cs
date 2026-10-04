using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BackgroundServices;

/// <summary>
/// The DM pipeline resolves a command's sender before routing. An identity lookup failure must
/// not stop /start from enabling DMs (the guarantee StartCommand held when it resolved itself).
/// </summary>
[TestFixture]
public class DmStartResolveFailureTests
{
    private const long UserId = 42;

    [Test]
    public async Task PrivateStart_IdentityLookupFails_StillEnablesDms()
    {
        var users = Substitute.For<ITelegramUserRepository>();
        users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new InvalidOperationException("db down"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(users);
        services.AddSingleton(Substitute.For<IProfileScanGate>());
        services.AddSingleton<IUserIdentityService, UserIdentityService>();
        services.AddSingleton(Substitute.For<ITelegramPermissionService>());
        services.AddSingleton(Substitute.For<IBotMessageService>());
        services.AddKeyedScoped<IBotCommand>(CommandNames.Start, (sp, _) => new StartCommand(
            NullLogger<StartCommand>.Instance,
            Substitute.For<IWelcomeResponsesRepository>(),
            users,
            Substitute.For<IPendingNotificationsRepository>(),
            sp,
            Substitute.For<IBotMessageService>(),
            Substitute.For<IBotChatService>(),
            Substitute.For<IBotDmService>(),
            Substitute.For<IBanCelebrationSubscriptionService>()));
        await using var provider = services.BuildServiceProvider();

        var chatCache = Substitute.For<IChatCache>();
        var sut = new MessageProcessingService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AppOptions()),
            new CommandRouter(NullLogger<CommandRouter>.Instance, provider, new PipelineMetrics()),
            chatCache,
            provider,
            new PipelineMetrics(),
            new ChatMetrics(chatCache),
            NullLogger<MessageProcessingService>.Instance);

        await sut.HandleNewMessageAsync(new Message
        {
            Id = 1,
            Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            Chat = new Chat { Id = UserId, Type = ChatType.Private },
            From = new User { Id = UserId, FirstName = "Kim" },
            Text = "/start"
        }, CancellationToken.None);

        await users.Received(1).EnableBotDmAsync(UserId, Arg.Any<CancellationToken>());
    }
}
