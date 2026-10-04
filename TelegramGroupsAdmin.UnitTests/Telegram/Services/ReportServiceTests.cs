using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

[TestFixture]
public class ReportServiceTests
{
    private const long ChatId = -1001L;
    private const long AuthorId = 7L;

    private IAdminNotificationService _notifications = null!;
    private IAuditService _audit = null!;
    private IUserIdentityService _identities = null!;
    private ReportService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _notifications = Substitute.For<IAdminNotificationService>();
        _audit = Substitute.For<IAuditService>();
        _identities = Substitute.For<IUserIdentityService>();
        var reports = Substitute.For<IReportsRepository>();
        reports.InsertContentReportAsync(Arg.Any<Report>(), Arg.Any<CancellationToken>()).Returns(5L);

        _sut = new ReportService(
            reports, _notifications, _audit, Substitute.For<IMessageHistoryRepository>(),
            _identities, new ReportMetrics(), NullLogger<ReportService>.Instance);
    }

    [Test]
    public async Task CreateReport_NotifiesAndAuditsWithTheAuthorResolvedById()
    {
        var author = UserIdentity.ForTest(AuthorId, "Current", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(AuthorId, Arg.Any<CancellationToken>()).Returns(author);
        var chat = new ChatIdentity(ChatId, "Workshop");
        var original = new Message
        {
            Id = 42,
            Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup },
            From = new User { Id = AuthorId, FirstName = "Stale" },
            Text = "buy now"
        };
        var report = new Report(0, 42, chat, null, null, "Auto-Detection", DateTimeOffset.UtcNow,
            ReportStatus.Pending, null, null, null, null);

        await _sut.CreateReportAsync(report, original, Actor.AutoDetection);

        await _audit.Received(1).LogEventAsync(
            Arg.Is(AuditEventType.ReportCreated), Arg.Is(Actor.AutoDetection),
            Arg.Is<Actor>(a => a!.TelegramUserId == AuthorId && a.DisplayName == author.DisplayName),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendReportNotificationAsync(
            Arg.Is(chat), Arg.Is(author), Arg.Is(Actor.AutoDetection), Arg.Any<string>(), Arg.Any<string?>(), Arg.Is(5L),
            Arg.Any<ReportType>(), Arg.Any<CancellationToken>());
    }
}
