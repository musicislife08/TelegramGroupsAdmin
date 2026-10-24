using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// messages.user_id references telegram_users: a sender the app has never seen is stored only after
/// it is observed, the order BotMessageService (ObserveBotAsync, then insert) and the message pipeline
/// use. Real identity service and repositories on canonical data; the rescan gate is substituted.
/// </summary>
[TestFixture]
public class ObserveThenStoreTests
{
    // Outside the canonical user range, so no row exists until the observation creates it
    private const long UnseenSenderId = 4242424244L;
    private const long NeverObservedSenderId = 4242424245L;

    // Message ids no canonical MainChat message uses (asserted absent first)
    private const int ObservedMessageId = 999801;
    private const int UnobservedMessageId = 999802;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;
    private string? _dataPath;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), $"observe_then_store_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataPath);

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.Configure<AppOptions>(options => options.DataPath = _dataPath);
        services.AddCoreServices();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddSingleton(Substitute.For<IProfileScanGate>());
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _testHelper?.Dispose();
        if (_dataPath is not null && Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    [Test]
    public async Task UnseenSender_ObservedThenStored_Succeeds()
    {
        await AssertAbsentAsync(UnseenSenderId, ObservedMessageId);
        var mainChat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId);
        var now = DateTimeOffset.UtcNow;

        await using var scope = _provider!.CreateAsyncScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ObserveAsync(
            new ObservedUser(UnseenSenderId, "Unseen", "Sender", "unseen_sender", IsBot: true,
                ObservationSource.BotUpdate, now),
            new ProfileChangeContext(mainChat));
        await scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>()
            .InsertMessageAsync(Message(ObservedMessageId, identity, mainChat, now));

        await using var ctx = _testHelper!.GetDbContext();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await ctx.TelegramUsers.AnyAsync(u => u.TelegramUserId == UnseenSenderId), Is.True);
            Assert.That(await ctx.Messages.AnyAsync(m =>
                m.MessageId == ObservedMessageId && m.ChatId == GoldenDatasetConstants.Chats.MainChatId
                && m.UserId == UnseenSenderId), Is.True);
        }
    }

    [Test]
    public async Task NeverObservedSender_Stored_FailsTheUserKey()
    {
        await AssertAbsentAsync(NeverObservedSenderId, UnobservedMessageId);
        var mainChat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId);

        await using var scope = _provider!.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
        var ex = Assert.CatchAsync(() => repository.InsertMessageAsync(
            Message(UnobservedMessageId, UserIdentity.ForTest(NeverObservedSenderId), mainChat, DateTimeOffset.UtcNow)));

        var postgres = ex as PostgresException ?? ex!.InnerException as PostgresException;
        Assert.That(postgres, Is.Not.Null, $"expected a PostgresException, got {ex}");
        Assert.That(postgres!.SqlState, Is.EqualTo(PostgresErrorCodes.ForeignKeyViolation));
    }

    private async Task AssertAbsentAsync(long userId, int messageId)
    {
        await using var ctx = _testHelper!.GetDbContext();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await ctx.TelegramUsers.AnyAsync(u => u.TelegramUserId == userId), Is.False);
            Assert.That(await ctx.Messages.AnyAsync(m =>
                m.MessageId == messageId && m.ChatId == GoldenDatasetConstants.Chats.MainChatId), Is.False);
        }
    }

    private static MessageRecord Message(int messageId, UserIdentity user, ChatIdentity chat, DateTimeOffset at) => new(
        MessageId: messageId,
        User: user,
        Chat: chat,
        Timestamp: at,
        MessageText: "observe then store",
        PhotoFileId: null,
        PhotoFileSize: null,
        Urls: null,
        EditDate: null,
        ContentHash: null,
        PhotoLocalPath: null,
        PhotoThumbnailPath: null,
        ChatIconPath: null,
        UserPhotoPath: null,
        DeletedAt: null,
        DeletionSource: null,
        ReplyToMessageId: null,
        ReplyToUser: null,
        ReplyToText: null,
        MediaType: null,
        MediaFileId: null,
        MediaFileSize: null,
        MediaFileName: null,
        MediaMimeType: null,
        MediaLocalPath: null,
        MediaDuration: null,
        Translation: null,
        ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);
}
