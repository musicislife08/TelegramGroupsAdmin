using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BackgroundServices;

/// <summary>
/// The new-message pipeline records the sender through IUserIdentityService before anything
/// else reads the user, and marks the user active instead of upserting the row.
/// </summary>
[TestFixture]
public class MessageProcessingServiceObserveTests
{
    private const long ChatId = -100123;
    private const long SenderId = 7;

    private ServiceProvider _provider = null!;
    private IUserIdentityService _identities = null!;
    private ITelegramUserRepository _users = null!;
    private ITelegramPermissionService _permissions = null!;
    private IBotCommand _help = null!;
    private IProfileScanGate _scanGate = null!;
    private IManagedChatsRepository _chats = null!;
    private IContentCheckCoordinator _coordinator = null!;
    private MessageProcessingService _sut = null!;
    private string _dataPath = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _users = Substitute.For<ITelegramUserRepository>();
        _permissions = Substitute.For<ITelegramPermissionService>();

        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(ci => UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "A"));

        var translation = Substitute.For<ITranslationHandler>();
        translation.GetTextForDetectionAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ci => new TranslationForDetectionResult(ci.ArgAt<string?>(0) ?? "", null, null));

        var services = new ServiceCollection();
        services.AddLogging();
        _dataPath = Path.Combine(Path.GetTempPath(), $"tga-observe-{Guid.NewGuid():N}");
        services.Configure<AppOptions>(o => o.DataPath = _dataPath);

        services.AddSingleton(_identities);
        services.AddSingleton(_users);
        services.AddSingleton(_permissions);
        services.AddSingleton(translation);
        _help = Substitute.For<IBotCommand>();
        services.AddKeyedSingleton(CommandNames.Help, _help);

        // Managed-chat lookups return null, so content detection is skipped as for an inactive chat.
        services.AddSingleton(Substitute.For<IConfigService>());
        _chats = Substitute.For<IManagedChatsRepository>();
        services.AddSingleton(_chats);
        services.AddSingleton(Substitute.For<IChatAdminsRepository>());
        services.AddSingleton(Substitute.For<IMessageHistoryRepository>());
        _scanGate = Substitute.For<IProfileScanGate>();
        services.AddSingleton(_scanGate);
        // Content detection is the observable consumer of the sender after the first-message scan.
        // It only runs for an active managed chat; the coordinator is where the sender lands.
        _coordinator = Substitute.For<IContentCheckCoordinator>();
        services.AddSingleton(_coordinator);
        services.AddSingleton(Substitute.For<IDetectionResultsRepository>());
        services.AddScoped(sp => new ContentDetectionOrchestrator(
            sp, null!, NullLogger<ContentDetectionOrchestrator>.Instance)); // action service unused: no result
        services.AddSingleton(Substitute.For<IJobScheduler>());
        services.AddSingleton(Substitute.For<IImageProcessor>());
        services.AddSingleton(Substitute.For<IBotChatService>());
        services.AddSingleton(Substitute.For<IBotUserService>());
        services.AddSingleton(Substitute.For<IBotMessageService>());
        services.AddSingleton(Substitute.For<IBotMediaService>());
        services.AddSingleton(Substitute.For<IBotModerationService>());
        services.AddSingleton(Substitute.For<IMessageTranslationService>());
        services.AddSingleton(Substitute.For<IUrlContentScrapingService>());
        services.AddSingleton(Substitute.For<IExamFlowService>());

        services.AddScoped<AdminMentionHandler>();
        services.AddScoped<ImageProcessingHandler>();
        services.AddScoped<TelegramMediaService>();
        services.AddScoped<MediaProcessingHandler>();
        services.AddScoped<FileScanningHandler>();
        services.AddScoped<BackgroundJobScheduler>();

        _provider = services.BuildServiceProvider();

        var commandRouter = new CommandRouter(NullLogger<CommandRouter>.Instance, _provider, new PipelineMetrics());
        var chatCache = Substitute.For<IChatCache>();

        _sut = new MessageProcessingService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IOptions<AppOptions>>(),
            commandRouter,
            chatCache,
            _provider,
            new PipelineMetrics(),
            new ChatMetrics(chatCache),
            NullLogger<MessageProcessingService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    [Test]
    public async Task NewMessage_ObservesSenderBeforeRoutingCommands()
    {
        var order = new List<string>();
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(ci => { order.Add("observe"); return UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "A"); });
        // The router's first dependency read once it routes a command.
        _permissions.GetEffectiveLevelAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("command"); return PermissionLevel.Member; });

        await _sut.HandleNewMessageAsync(TextMessage(from: SenderId, text: "/help"), CancellationToken.None);

        Assert.That(order, Is.EqualTo(new[] { "observe", "command" }));
    }

    [Test]
    public async Task GroupCommand_ReceivesTheObservedSender()
    {
        var observed = UserIdentity.ForTest(SenderId, "Observed");
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(observed);

        await _sut.HandleNewMessageAsync(TextMessage(from: SenderId, text: "/help"), CancellationToken.None);

        await _help.Received(1).ExecuteAsync(
            Arg.Any<Message>(), Arg.Any<string[]>(), Arg.Any<PermissionLevel>(), Arg.Is(observed), Arg.Any<CancellationToken>());
        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default, default);
    }

    [Test]
    public async Task PrivateCommand_ResolvesTheSenderByIdAndPassesItToTheCommand()
    {
        var resolved = UserIdentity.ForTest(SenderId, "Resolved");
        _identities.ResolveAsync(SenderId, Arg.Any<CancellationToken>()).Returns(resolved);

        await _sut.HandleNewMessageAsync(
            TextMessage(from: SenderId, text: "/help", chatType: ChatType.Private, chatId: SenderId), CancellationToken.None);

        await _help.Received(1).ExecuteAsync(
            Arg.Any<Message>(), Arg.Any<string[]>(), Arg.Any<PermissionLevel>(), Arg.Is(resolved), Arg.Any<CancellationToken>());
        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
    }

    [Test]
    public async Task NewMessage_ObservationCarriesMessageDateAndChatContext()
    {
        var message = TextMessage(from: SenderId, text: "hi");
        var expectedAt = new DateTimeOffset(DateTime.SpecifyKind(message.Date, DateTimeKind.Utc));

        await _sut.HandleNewMessageAsync(message, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == SenderId && o.ObservedAt == expectedAt && o.Source == ObservationSource.BotUpdate),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == message.Chat.Id && c.MessageId == message.MessageId),
            RenameRescan.Inline,
            Arg.Any<CancellationToken>());
        await _users.Received(1).MarkActiveAsync(SenderId, expectedAt, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PrivateMessage_IsNotObserved()
    {
        // DMs never recorded names or rescanned before; recording a DM rename without a
        // rescan would use the rename up before the next group message could act on it.
        var message = TextMessage(from: SenderId, text: "hello", chatType: ChatType.Private, chatId: SenderId);

        await _sut.HandleNewMessageAsync(message, CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
        await _users.DidNotReceiveWithAnyArgs().MarkActiveAsync(default, default, default);
    }

    [Test]
    public async Task GroupMessageWithoutSender_IsNotObservedOrMarkedActive()
    {
        var message = TextMessage(from: SenderId, text: "hi");
        message.From = null;

        await _sut.HandleNewMessageAsync(message, CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
        await _users.DidNotReceiveWithAnyArgs().MarkActiveAsync(default, default, default);
    }

    [Test]
    public async Task FirstMessageScan_DownstreamUsesIdentityResolvedAfterTheScan()
    {
        // Observed before the scan: no verdict. The scan flags the name, so the identity resolved
        // after it carries the verdict, and content detection must receive that one.
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(SenderId, "A"));
        var rescanned = UserIdentity.ForTest(SenderId, "A", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(SenderId, Arg.Any<CancellationToken>()).Returns(rescanned);
        _scanGate.ScanIfEligibleAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), ProfileScanTrigger.FirstMessage, Arg.Any<CancellationToken>())
            .Returns(new ProfileScanResult(SenderId, null, null, null, null, false, null, false, false, false,
                3m, ProfileScanOutcome.HeldForReview, "explicit name", ["explicit"], ExplicitDisplayText: true));
        _chats.GetByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ManagedChatRecord(ChatIdentity.FromId(ChatId), default, default, IsAdmin: true,
                DateTimeOffset.UtcNow, IsActive: true, IsDeleted: false, null, null, null));

        await _sut.HandleNewMessageAsync(TextMessage(from: SenderId, text: "hello"), CancellationToken.None);

        await _coordinator.Received(1).CheckAsync(
            Arg.Is<ContentCheckRequest>(r => r!.User == rescanned), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NoFirstMessageScan_DoesNotReResolve()
    {
        await _sut.HandleNewMessageAsync(TextMessage(from: SenderId, text: "hello"), CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default, default);
    }

    private static Message TextMessage(long from, string text, ChatType chatType = ChatType.Supergroup, long chatId = ChatId) => new()
    {
        Id = 42,
        Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
        Chat = new Chat { Id = chatId, Type = chatType, Title = chatType == ChatType.Private ? null : "Group" },
        From = new User { Id = from, FirstName = "A", IsBot = false },
        Text = text
    };
}
