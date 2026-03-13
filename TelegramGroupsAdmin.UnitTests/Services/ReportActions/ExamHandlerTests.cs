using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;

namespace TelegramGroupsAdmin.UnitTests.Services.ReportActions;

[TestFixture]
public class ExamHandlerTests
{
    private const long TestExamId = 700L;
    private const long TestUserId = 800L;
    private const long TestChatId = -100555666777L;
    private static readonly Actor TestExecutor = Actor.FromWebUser("admin-id", "admin@test.com");

    private IReportsRepository _mockReportsRepo = null!;
    private IExamFlowService _mockExamFlowService = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;

    private ExamHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockExamFlowService = Substitute.For<IExamFlowService>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler = new ExamHandler(
            _mockReportsRepo,
            _mockExamFlowService,
            _mockCallbackContextRepo,
            NullLogger<ExamHandler>.Instance);
    }

    #region ApproveAsync Tests

    [Test]
    public async Task ApproveAsync_Success_ReturnsSuccessAndCleansUp()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.ApproveExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                TestExamId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Approve"));
        Assert.That(result.Message, Does.Contain("permissions restored"));

        await _mockCallbackContextRepo.Received(1)
            .DeleteByReportIdAsync(TestExamId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApproveAsync_ExamNotFound_ReturnsFailure()
    {
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns((ExamFailureRecord?)null);

        var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task ApproveAsync_AlreadyHandled_ReturnsFailureWithAttribution()
    {
        var exam = CreateTestExam(reviewed: true);
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);

        var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
        Assert.That(result.Message, Does.Contain("other@test.com"));
    }

    [Test]
    public async Task ApproveAsync_ExamFlowFails_ReturnsFailure()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.ApproveExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                TestExamId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User already left"));

        var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("User already left"));
    }

    #endregion

    #region DenyAsync Tests

    [Test]
    public async Task DenyAsync_Success_ReturnsSuccess()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.DenyAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Deny"));
        Assert.That(result.Message, Does.Contain("kicked"));
    }

    [Test]
    public async Task DenyAsync_AlreadyHandled_ReturnsFailure()
    {
        var exam = CreateTestExam(reviewed: true);
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);

        var result = await _handler.DenyAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task DenyAsync_FlowFails_ReturnsFailure()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Chat not found"));

        var result = await _handler.DenyAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Chat not found"));
    }

    #endregion

    #region DenyAndBanAsync Tests

    [Test]
    public async Task DenyAndBanAsync_Success_ReturnsSuccess()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.DenyAndBanExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.DenyAndBanAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("DenyAndBan"));
        Assert.That(result.Message, Does.Contain("banned"));
    }

    [Test]
    public async Task DenyAndBanAsync_AlreadyHandled_ReturnsFailure()
    {
        var exam = CreateTestExam(reviewed: true);
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);

        var result = await _handler.DenyAndBanAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task DenyAndBanAsync_FlowFails_ReturnsFailure()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.DenyAndBanExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Ban API error"));

        var result = await _handler.DenyAndBanAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Ban API error"));
    }

    #endregion

    #region Race Condition Tests

    [Test]
    public async Task ApproveAsync_RaceCondition_TryUpdateFails_ReturnsFailure()
    {
        var exam = CreateTestExam();
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam);
        _mockExamFlowService.ApproveExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                TestExamId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Re-fetch shows handled
        var handled = CreateTestExam(reviewed: true);
        _mockReportsRepo.GetExamFailureAsync(TestExamId, Arg.Any<CancellationToken>())
            .Returns(exam, handled);

        var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
    }

    #endregion

    #region Helper Methods

    private static ExamFailureRecord CreateTestExam(bool reviewed = false)
    {
        return new ExamFailureRecord
        {
            Id = TestExamId,
            User = new UserIdentity(TestUserId, "Test", null, "testuser"),
            Chat = new ChatIdentity(TestChatId, "Test Chat"),
            Score = 40,
            PassingThreshold = 70,
            FailedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ReviewedAt = reviewed ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
            ReviewedBy = reviewed ? "other@test.com" : null,
            ActionTaken = reviewed ? "approve" : null
        };
    }

    #endregion
}
