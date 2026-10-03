using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// Unit tests for ReportCommand entity-based message building.
/// Validates that the success response uses a TextMention entity (not raw @username markdown)
/// and an Italic entity for the trailer, fixing #468.
/// </summary>
[TestFixture]
public class ReportCommandTests
{
    private const long ReportedUserId = 555L;
    private const long ReporterUserId = 1001L;
    private const long TestChatId = -100999888L;
    private const int TestMessageId = 42;
    private const int TestReplyMessageId = 41;

    private ILogger<ReportCommand> _mockLogger = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockScopeServiceProvider = null!;
    private IReportsRepository _mockReportsRepository = null!;
    private IReportService _mockReportService = null!;
    private IUserIdentityService _identities = null!;
    private IConfigService _config = null!;

    private ReportCommand _command = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<ReportCommand>>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockScopeServiceProvider = Substitute.For<IServiceProvider>();
        _mockReportsRepository = Substitute.For<IReportsRepository>();
        _mockReportService = Substitute.For<IReportService>();

        // Wire up scope factory — CreateScope() extension method resolves IServiceScopeFactory
        var mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockServiceProvider.GetService(typeof(IServiceScopeFactory))
            .Returns(mockScopeFactory);

        // Wire up the scoped services
        _mockScope.ServiceProvider.Returns(_mockScopeServiceProvider);
        _mockScopeServiceProvider.GetService(typeof(IReportsRepository))
            .Returns(_mockReportsRepository);
        _mockScopeServiceProvider.GetService(typeof(IReportService))
            .Returns(_mockReportService);

        _identities = Substitute.For<IUserIdentityService>();
        _config = Substitute.For<IConfigService>();

        _command = new ReportCommand(_mockLogger, _mockServiceProvider, _identities, _config);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    [Test]
    public async Task Report_success_builds_entity_message_with_mention_and_no_markdown_trailer()
    {
        // Arrange: reported user whose username contains underscore
        var reportedUser = new User
        {
            Id = ReportedUserId,
            FirstName = "Sofia",
            LastName = "Rodriguez",
            Username = "rodriguez_sofi"
        };
        var reporter = new User
        {
            Id = ReporterUserId,
            FirstName = "Alex",
            Username = "alex_reporter"
        };

        var replyMessage = new Message
        {
            Id = TestReplyMessageId,
            From = reportedUser,
            Chat = new Chat { Id = TestChatId },
            Text = "some message content"
        };

        var message = new Message
        {
            Id = TestMessageId,
            From = reporter,
            Chat = new Chat { Id = TestChatId },
            ReplyToMessage = replyMessage,
            Text = "/report"
        };

        // No existing pending report
        _mockReportsRepository
            .GetExistingPendingContentReportAsync(TestReplyMessageId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((Report?)null);

        // CreateReportAsync returns ReportId = 7
        _mockReportService
            .CreateReportAsync(Arg.Any<Report>(), Arg.Any<Message>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReportCreationResult(ReportId: 7));

        _identities.ResolveAsync(ReportedUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(ReportedUserId, "Sofia", "Rodriguez", "rodriguez_sofi"));

        // Act
        var result = await _command.ExecuteAsync(message, [], PermissionLevel.Admin, Reporter);

        // Assert: text contains the report number
        Assert.That(result.Message.Text, Does.Contain("Report #7"));

        // Assert: a TextMention entity with the reported user's ID is present
        Assert.That(result.Message.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == ReportedUserId));

        // Assert: an Italic entity is present for the trailer
        Assert.That(result.Message.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.Italic));

        // Assert: no raw Markdown italic syntax (underscore-wrapped) in the text
        Assert.That(result.Message.Text, Does.Not.Contain("_Admins"));
    }

    [Test]
    public async Task Report_mentions_resolved_target_with_chat_masking_and_records_sender_as_reporter()
    {
        var message = new Message
        {
            Id = TestMessageId,
            From = new User { Id = ReporterUserId, FirstName = "Stale reporter" },
            Chat = new Chat { Id = TestChatId },
            Text = "/report",
            ReplyToMessage = new Message
            {
                Id = TestReplyMessageId,
                From = new User { Id = ReportedUserId, FirstName = "Stale" },
                Chat = new Chat { Id = TestChatId },
                Text = "spam"
            }
        };
        _mockReportsRepository
            .GetExistingPendingContentReportAsync(TestReplyMessageId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((Report?)null);
        _mockReportService
            .CreateReportAsync(Arg.Any<Report>(), Arg.Any<Message>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReportCreationResult(ReportId: 7));
        _identities.ResolveAsync(ReportedUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(ReportedUserId, "Promo", verdict: NameVerdict.Promotional));
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);

        var result = await _command.ExecuteAsync(message, [], PermissionLevel.Member, Reporter);

        Assert.That(result.Message.Text, Does.Contain("Reported user: " + NameRedaction.Spam));
        Assert.That(result.Message.Text, Does.Not.Contain("Promo"));
        await _mockReportService.Received(1).CreateReportAsync(
            Arg.Is<Report>(r => r!.ReportedByUserId == ReporterUserId && r.ReportedByUserName == "alex_reporter"),
            Arg.Any<Message>(),
            Arg.Is<Actor>(a => a!.TelegramUserId == ReporterUserId && a.DisplayName == Reporter.DisplayName),
            Arg.Any<CancellationToken>());
    }

    private const long EarlierReporterId = 2002L;

    private Message ReplyReportMessage() => new()
    {
        Id = TestMessageId,
        From = new User { Id = ReporterUserId, FirstName = "Alex" },
        Chat = new Chat { Id = TestChatId },
        Text = "/report",
        ReplyToMessage = new Message
        {
            Id = TestReplyMessageId,
            From = new User { Id = ReportedUserId, FirstName = "Target" },
            Chat = new Chat { Id = TestChatId },
            Text = "spam"
        }
    };

    private void ExistingReport(long? reportedByUserId, string? reportedByUserName) =>
        _mockReportsRepository
            .GetExistingPendingContentReportAsync(TestReplyMessageId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(new Report(9, TestReplyMessageId, ChatIdentity.FromId(TestChatId), 40, reportedByUserId,
                reportedByUserName, DateTimeOffset.UtcNow, ReportStatus.Pending, null, null, null, null));

    [Test]
    public async Task AlreadyReported_FlaggedEarlierReporter_IsMentionedWithChatMasking()
    {
        // The stored reporter name is a snapshot; the reporter is resolved by id and masked.
        ExistingReport(EarlierReporterId, "BadName");
        _identities.ResolveAsync(ReportedUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(ReportedUserId, "Target"));
        _identities.ResolveAsync(EarlierReporterId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(EarlierReporterId, "BadName", verdict: NameVerdict.Explicit));
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);

        var result = await _command.ExecuteAsync(ReplyReportMessage(), [], PermissionLevel.Member, Reporter);

        Assert.That(result.Message.Text, Does.Contain("Reported by: " + NameRedaction.Explicit));
        Assert.That(result.Message.Text, Does.Not.Contain("BadName"));
        Assert.That(result.Message.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == EarlierReporterId));
    }

    [Test]
    public async Task AlreadyReported_NoReporterId_FallsBackToStoredNameOrSystem()
    {
        ExistingReport(reportedByUserId: null, reportedByUserName: null);
        _identities.ResolveAsync(ReportedUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(ReportedUserId, "Target"));

        var result = await _command.ExecuteAsync(ReplyReportMessage(), [], PermissionLevel.Member, Reporter);

        Assert.That(result.Message.Text, Does.Contain("Reported by: System"));
        await _identities.DidNotReceive().ResolveAsync(Arg.Is<long>(id => id != ReportedUserId), Arg.Any<CancellationToken>());
    }

    private static readonly UserIdentity Reporter = UserIdentity.ForTest(ReporterUserId, "Alex", username: "alex_reporter");
}
