using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
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

        _command = new ReportCommand(_mockLogger, _mockServiceProvider);
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

        // Act
        var result = await _command.ExecuteAsync(message, [], userPermission: PermissionLevel.Admin);

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
}
