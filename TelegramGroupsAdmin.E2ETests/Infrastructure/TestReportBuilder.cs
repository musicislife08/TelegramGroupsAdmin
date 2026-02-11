using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating test reports with various configurations.
/// Each test should build exactly the reports it needs with specific states.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// var report = await new TestReportBuilder(Factory.Services)
///     .ForMessage(message)
///     .InChat(chat.ChatId)
///     .ReportedBy(123456789, "testuser")
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestReportBuilder
{
    private readonly IServiceProvider _services;
    private int _messageId = 1;
    private long _chatId = -1001234567890;
    private int? _reportCommandMessageId;
    private long? _reportedByUserId;
    private string? _reportedByUserName;
    private DateTimeOffset _reportedAt = DateTimeOffset.UtcNow;
    private ReportStatus _status = ReportStatus.Pending;
    private string? _reviewedBy;
    private DateTimeOffset? _reviewedAt;
    private string? _actionTaken;
    private string? _adminNotes;
    private string? _webUserId;

    public TestReportBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the message ID being reported.
    /// </summary>
    public TestReportBuilder ForMessageId(int messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets the message being reported using a TestMessage.
    /// </summary>
    public TestReportBuilder ForMessage(TestMessage message)
    {
        _messageId = (int)message.MessageId;
        _chatId = message.ChatId;
        return this;
    }

    /// <summary>
    /// Sets the chat ID where the reported message is located.
    /// </summary>
    public TestReportBuilder InChat(long chatId)
    {
        _chatId = chatId;
        return this;
    }

    /// <summary>
    /// Sets the chat using a TestChat.
    /// </summary>
    public TestReportBuilder InChat(TestChat chat)
    {
        _chatId = chat.ChatId;
        return this;
    }

    /// <summary>
    /// Sets the report as coming from a Telegram /report command.
    /// </summary>
    public TestReportBuilder FromTelegramCommand(int reportCommandMessageId, long userId, string? userName = null)
    {
        _reportCommandMessageId = reportCommandMessageId;
        _reportedByUserId = userId;
        _reportedByUserName = userName;
        return this;
    }

    /// <summary>
    /// Sets the reporter information (Telegram user who reported).
    /// </summary>
    public TestReportBuilder ReportedBy(long userId, string? userName = null)
    {
        _reportedByUserId = userId;
        _reportedByUserName = userName;
        return this;
    }

    /// <summary>
    /// Sets the report as coming from the web UI.
    /// </summary>
    public TestReportBuilder FromWebUser(string webUserId)
    {
        _webUserId = webUserId;
        return this;
    }

    /// <summary>
    /// Sets the timestamp when the report was created.
    /// </summary>
    public TestReportBuilder At(DateTimeOffset timestamp)
    {
        _reportedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Marks the report as reviewed (approved/action taken).
    /// </summary>
    public TestReportBuilder AsReviewed(string reviewedBy, string actionTaken, string? notes = null)
    {
        _status = ReportStatus.Reviewed;
        _reviewedBy = reviewedBy;
        _reviewedAt = DateTimeOffset.UtcNow;
        _actionTaken = actionTaken;
        _adminNotes = notes;
        return this;
    }

    /// <summary>
    /// Marks the report as dismissed (false positive).
    /// </summary>
    public TestReportBuilder AsDismissed(string reviewedBy, string? notes = null)
    {
        _status = ReportStatus.Dismissed;
        _reviewedBy = reviewedBy;
        _reviewedAt = DateTimeOffset.UtcNow;
        _actionTaken = "Dismissed";
        _adminNotes = notes;
        return this;
    }

    /// <summary>
    /// Sets the report status explicitly.
    /// </summary>
    public TestReportBuilder WithStatus(ReportStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>
    /// Builds and persists the report to the database.
    /// Returns a TestReport containing the report record for testing.
    /// </summary>
    public async Task<TestReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var reportRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: _messageId,
            Chat: new ChatIdentity(_chatId, null),
            ReportCommandMessageId: _reportCommandMessageId,
            ReportedByUserId: _reportedByUserId,
            ReportedByUserName: _reportedByUserName,
            ReportedAt: _reportedAt,
            Status: _status,
            ReviewedBy: _reviewedBy,
            ReviewedAt: _reviewedAt,
            ActionTaken: _actionTaken,
            AdminNotes: _adminNotes,
            WebUserId: _webUserId
        );

        var id = await reportRepository.InsertContentReportAsync(report, cancellationToken);

        // Return the report with the assigned ID
        var savedReport = report with { Id = id };
        return new TestReport(savedReport);
    }
}

/// <summary>
/// Represents a test report for E2E testing.
/// </summary>
public record TestReport(Report Record)
{
    public long Id => Record.Id;
    public int MessageId => Record.MessageId;
    public long ChatId => Record.Chat.Id;
    public ReportStatus Status => Record.Status;
    public DateTimeOffset ReportedAt => Record.ReportedAt;
}
