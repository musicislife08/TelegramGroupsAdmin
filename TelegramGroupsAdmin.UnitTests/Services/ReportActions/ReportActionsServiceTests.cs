using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;

namespace TelegramGroupsAdmin.UnitTests.Services.ReportActions;

[TestFixture]
public class ReportActionsServiceTests
{
    private static readonly Actor TestExecutor = Actor.FromWebUser("admin-id", "admin@test.com");

    private IServiceScopeFactory _mockScopeFactory = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private IContentReportHandler _mockContentHandler = null!;
    private IProfileScanHandler _mockProfileScanHandler = null!;
    private IImpersonationHandler _mockImpersonationHandler = null!;
    private IExamHandler _mockExamHandler = null!;

    private ReportActionsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockContentHandler = Substitute.For<IContentReportHandler>();
        _mockProfileScanHandler = Substitute.For<IProfileScanHandler>();
        _mockImpersonationHandler = Substitute.For<IImpersonationHandler>();
        _mockExamHandler = Substitute.For<IExamHandler>();

        _mockScope.ServiceProvider.Returns(_mockServiceProvider);
        _mockScopeFactory.CreateScope().Returns(_mockScope);

        _mockServiceProvider.GetService(typeof(IContentReportHandler)).Returns(_mockContentHandler);
        _mockServiceProvider.GetService(typeof(IProfileScanHandler)).Returns(_mockProfileScanHandler);
        _mockServiceProvider.GetService(typeof(IImpersonationHandler)).Returns(_mockImpersonationHandler);
        _mockServiceProvider.GetService(typeof(IExamHandler)).Returns(_mockExamHandler);

        _service = new ReportActionsService(
            _mockScopeFactory,
            NullLogger<ReportActionsService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    #region Delegation Tests

    [Test]
    public async Task HandleContentBanAsync_DelegatesToContentHandler()
    {
        var expected = new ReviewActionResult(true, "Banned from 3 chats", "Ban");
        _mockContentHandler.BanAsync(123L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.HandleContentBanAsync(123L, TestExecutor, CancellationToken.None);

        Assert.That(result, Is.EqualTo(expected));
        await _mockContentHandler.Received(1).BanAsync(123L, TestExecutor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleProfileScanAllowAsync_DelegatesToProfileScanHandler()
    {
        var expected = new ReviewActionResult(true, "User allowed", "Allow");
        _mockProfileScanHandler.AllowAsync(456L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.HandleProfileScanAllowAsync(456L, TestExecutor, CancellationToken.None);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task HandleImpersonationConfirmAsync_DelegatesToImpersonationHandler()
    {
        var expected = new ReviewActionResult(true, "Confirmed", "Confirm");
        _mockImpersonationHandler.ConfirmAsync(789L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.HandleImpersonationConfirmAsync(789L, TestExecutor, CancellationToken.None);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task HandleExamApproveAsync_DelegatesToExamHandler()
    {
        var expected = new ReviewActionResult(true, "Approved", "Approve");
        _mockExamHandler.ApproveAsync(101L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.HandleExamApproveAsync(101L, TestExecutor, CancellationToken.None);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Scope Lifecycle Tests

    [Test]
    public async Task ExecuteWithLock_CreatesAndDisposesScope()
    {
        _mockContentHandler.SpamAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Done"));

        await _service.HandleContentSpamAsync(1L, TestExecutor, CancellationToken.None);

        _mockScopeFactory.Received(1).CreateScope();
        _mockScope.Received(1).Dispose();
    }

    #endregion

    #region Semaphore Tests

    [Test]
    public async Task SameReportId_ExecutesSerially()
    {
        var callOrder = new List<int>();
        var tcs1 = new TaskCompletionSource();

        _mockContentHandler.BanAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                callOrder.Add(1);
                await tcs1.Task;
                return new ReviewActionResult(true, "Ban done");
            });

        _mockContentHandler.SpamAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callOrder.Add(2);
                return Task.FromResult(new ReviewActionResult(true, "Spam done"));
            });

        // Start ban (will block on tcs1)
        var banTask = _service.HandleContentBanAsync(1L, TestExecutor, CancellationToken.None);

        // Start spam (same report ID — should block)
        var spamTask = _service.HandleContentSpamAsync(1L, TestExecutor, CancellationToken.None);

        // Give spam a chance to start (if it could)
        await Task.Delay(50);

        // Only ban should have started
        Assert.That(callOrder, Is.EqualTo(new[] { 1 }));

        // Release ban
        tcs1.SetResult();
        await banTask;
        await spamTask;

        // Now both completed, in order
        Assert.That(callOrder, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task DifferentReportIds_ExecuteConcurrently()
    {
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        var started = new List<long>();

        _mockContentHandler.BanAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                lock (started) started.Add(1L);
                await tcs1.Task;
                return new ReviewActionResult(true, "Done 1");
            });

        _mockContentHandler.BanAsync(2L, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                lock (started) started.Add(2L);
                await tcs2.Task;
                return new ReviewActionResult(true, "Done 2");
            });

        var task1 = _service.HandleContentBanAsync(1L, TestExecutor, CancellationToken.None);
        var task2 = _service.HandleContentBanAsync(2L, TestExecutor, CancellationToken.None);

        await Task.Delay(50);

        // Both should have started (different IDs, no blocking)
        lock (started)
        {
            Assert.That(started, Has.Count.EqualTo(2));
        }

        tcs1.SetResult();
        tcs2.SetResult();
        await task1;
        await task2;
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public async Task HandlerThrows_ReturnsFailureResult()
    {
        _mockContentHandler.BanAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var result = await _service.HandleContentBanAsync(1L, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("failed unexpectedly"));
    }

    [Test]
    public void OperationCanceled_PropagatesException()
    {
        _mockContentHandler.BanAsync(1L, TestExecutor, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.HandleContentBanAsync(1L, TestExecutor, CancellationToken.None));
    }

    #endregion
}
