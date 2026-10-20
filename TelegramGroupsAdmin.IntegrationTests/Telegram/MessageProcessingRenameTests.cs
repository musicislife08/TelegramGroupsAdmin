using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Pins the rename-on-a-new-message regression through the real
/// <see cref="MessageProcessingService.HandleNewMessageAsync"/>. The sender is observed before the
/// message row exists, so the rename's <c>ProfileChange</c> audit must not reference the message:
/// before, the composite foreign key rejected the row and the whole observation rolled back,
/// losing the new names, the history row and the audit.
///
/// <para>The rename also triggers the profile-change rescan (only that trigger is enabled). The
/// substituted scan service plays the real scan's outcome: it bans the user in the database and
/// returns Banned, so the text message takes the already-banned early return after it is stored
/// and the rename must still be fully recorded. Harness shared with
/// <see cref="MessageProcessingFirstMessageScanTests"/> via <see cref="MessageProcessingTestHarness"/>.</para>
/// </summary>
[TestFixture]
public class MessageProcessingRenameTests
{
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;
    private const long UserId = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
    private const int TextMessageId = 990101;
    private const int ServiceMessageId = 990102;

    private MigrationTestHelper _testHelper = null!;
    private ServiceProvider _serviceProvider = null!;
    private IMessageProcessingService _sut = null!;
    private IProfileScanService _profileScanService = null!;
#pragma warning disable NUnit1032 // Substitutes are not owned resources
    private ITelegramSessionManager _sessionManager = null!;
#pragma warning restore NUnit1032
    private string _dataPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), $"tga-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataPath);

        _profileScanService = Substitute.For<IProfileScanService>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();

        // The rename rescan's outcome: a real profile ban marks the user banned, which is what the
        // pipeline's already-banned check reads. The substitute reproduces that effect.
        _profileScanService
            .ScanUserProfileAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(),
                Arg.Any<bool>(), ProfileScanOrigin.Rescan)
            .Returns(async _ =>
            {
                await using var context = _testHelper.GetDbContext();
                var user = await context.TelegramUsers.SingleAsync(u => u.TelegramUserId == UserId);
                user.IsBanned = true;
                await context.SaveChangesAsync();
                return BannedScanResult();
            });

        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);

        _serviceProvider = MessageProcessingTestHarness.Build(_testHelper, _dataPath, _profileScanService, _sessionManager);
        _sut = _serviceProvider.GetRequiredService<IMessageProcessingService>();

        await EnableProfileChangeScanOnlyAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _testHelper.Dispose();

        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    [Test]
    public async Task NewMessage_FromRenamedUser_RecordsRenameAndStoresMessage()
    {
        var before = await ReadStateAsync();

        await _sut.HandleNewMessageAsync(CreateTextMessage("Renamed Sender"), CancellationToken.None);

        await AssertRenameRecordedAsync(before, "Renamed Sender");
        await AssertMessageStoredAsync(TextMessageId);
    }

    [Test]
    public async Task ServiceMessage_FromRenamedUser_RecordsRename()
    {
        var before = await ReadStateAsync();

        await _sut.HandleNewMessageAsync(CreateJoinMessage("Renamed Joiner"), CancellationToken.None);

        await AssertRenameRecordedAsync(before, "Renamed Joiner");
        await AssertMessageStoredAsync(ServiceMessageId);
    }

    // Only the profile-change trigger is on, so the rename rescan is what reaches the scan service.
    private async Task EnableProfileChangeScanOnlyAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var welcome = await configService.GetEffectiveWelcomeAsync(MainChatId) ?? WelcomeConfig.Default;

        welcome.JoinSecurity.ProfileScan.Enabled = true;
        welcome.JoinSecurity.ProfileScan.ScanOnJoin = false;
        welcome.JoinSecurity.ProfileScan.ScanOnFirstMessage = false;
        welcome.JoinSecurity.ProfileScan.ScanOnProfileChange = true;

        await configService.SaveWelcomeAsync(new ChatIdentity(MainChatId, "Main Community"), welcome, Actor.SystemSeed);
    }

    private async Task AssertMessageStoredAsync(int messageId)
    {
        using var scope = _serviceProvider.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>()
            .GetMessageAsync(messageId, MainChatId);
        Assert.That(stored, Is.Not.Null, $"Message {messageId} was not stored.");
    }

    private sealed record State(string? FirstName, int HistoryCount, int ProfileChangeCount);

    private async Task<State> ReadStateAsync()
    {
        await using var context = _testHelper.GetDbContext();
        var user = await context.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        var history = await context.UsernameHistory.CountAsync(h => h.UserId == UserId);
        var audits = await context.UserActions.CountAsync(a =>
            a.UserId == UserId && a.ActionType == (int)UserActionType.ProfileChange);
        Assert.That(user.FirstName, Is.Not.Null.And.Not.EqualTo("Renamed Sender").And.Not.EqualTo("Renamed Joiner"),
            "Precondition: the anchor must not already carry the new name.");
        return new State(user.FirstName, history, audits);
    }

    private async Task AssertRenameRecordedAsync(State before, string newFirstName)
    {
        await using var context = _testHelper.GetDbContext();
        var user = await context.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        var history = await context.UsernameHistory.AsNoTracking()
            .Where(h => h.UserId == UserId).OrderByDescending(h => h.Id).ToListAsync();
        var audits = await context.UserActions.AsNoTracking()
            .Where(a => a.UserId == UserId && a.ActionType == (int)UserActionType.ProfileChange).ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.FirstName, Is.EqualTo(newFirstName));
            Assert.That(history, Has.Count.EqualTo(before.HistoryCount + 1));
            Assert.That(history[0].FirstName, Is.EqualTo(before.FirstName));
            Assert.That(audits, Has.Count.EqualTo(before.ProfileChangeCount + 1));
            var audit = audits.OrderByDescending(a => a.Id).First();
            Assert.That(audit.ChatId, Is.EqualTo(MainChatId));
            Assert.That(audit.MessageId, Is.Null);
        }

        // The rename rescan reached the scan service exactly once and banned the user.
        await _profileScanService.Received(1).ScanUserProfileAsync(
            Arg.Is<UserIdentity>(u => u != null && u.Id == UserId),
            Arg.Is<ChatIdentity?>(c => c != null && c.Id == MainChatId),
            Arg.Any<CancellationToken>(), true, ProfileScanOrigin.Rescan);
        Assert.That(user.IsBanned, Is.True, "The rename rescan's Banned outcome was not applied.");
    }

    private static Message CreateTextMessage(string firstName) => new()
    {
        Id = TextMessageId,
        Date = DateTime.UtcNow,
        Chat = TelegramTestFactory.CreateChat(MainChatId, ChatType.Supergroup, "Main Community"),
        From = TelegramTestFactory.CreateUser(UserId, firstName, username: "pastramiherbs"),
        Text = "hi"
    };

    private static Message CreateJoinMessage(string firstName)
    {
        var from = TelegramTestFactory.CreateUser(UserId, firstName, username: "pastramiherbs");
        return new Message
        {
            Id = ServiceMessageId,
            Date = DateTime.UtcNow,
            Chat = TelegramTestFactory.CreateChat(MainChatId, ChatType.Supergroup, "Main Community"),
            From = from,
            NewChatMembers = [from]
        };
    }

    private static ProfileScanResult BannedScanResult() => new(
        TelegramUserId: UserId,
        Bio: null,
        PersonalChannelId: null,
        PersonalChannelTitle: null,
        PersonalChannelAbout: null,
        HasPinnedStories: false,
        PinnedStoryCaptions: null,
        IsScam: true,
        IsFake: false,
        IsVerified: false,
        Score: 5.0m,
        Outcome: ProfileScanOutcome.Banned,
        AiReason: "test",
        AiSignalsDetected: []);
}
