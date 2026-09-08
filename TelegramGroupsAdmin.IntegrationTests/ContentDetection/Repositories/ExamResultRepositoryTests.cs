using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Exam result reads/writes against the golden template. Read tests assert on real
/// canonical exam rows; the only SUT writes are the inserts whose born-state IS the subject.
/// </summary>
[TestFixture]
public class ExamResultRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IReportsRepository, ReportsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task GetExamResultAsync_PendingFailure_MapsFailedAndPending()
    {
        var result = await _repository!.GetExamResultAsync(GoldenDatasetConstants.Reports.PendingExamFailureId);

        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Outcome, Is.EqualTo(ExamOutcome.Failed));
            Assert.That(result.ReviewedAt, Is.Null);
            Assert.That(result.ActionTaken, Is.Null);
            Assert.That(result.Score, Is.EqualTo(20));
            Assert.That(result.PassingThreshold, Is.EqualTo(80));
        }
    }

    [Test]
    public async Task GetExamResultAsync_PendingFailure_PreservesJsonb()
    {
        var result = await _repository!.GetExamResultAsync(GoldenDatasetConstants.Reports.PendingExamFailureId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.McAnswers, Is.EqualTo(new Dictionary<int, string> { { 0, "A" } }));
            Assert.That(result.ShuffleState!.Count, Is.EqualTo(1));
            Assert.That(result.ShuffleState[0], Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.OpenEndedAnswer, Is.EqualTo("Lorem ipsum"));
        }
    }

    [Test]
    public async Task GetExamResultAsync_ResolvedFailure_MapsFailedAndReviewed()
    {
        var result = await _repository!.GetExamResultAsync(GoldenDatasetConstants.Reports.ResolvedExamFailureId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Outcome, Is.EqualTo(ExamOutcome.Failed));
            Assert.That(result.ReviewedAt, Is.Not.Null);
            Assert.That(result.ActionTaken, Is.EqualTo("approve"));
        }
    }

    [Test]
    public async Task GetExamResultAsync_AutoApprovedPass_MapsPassedWithSentinel()
    {
        var result = await _repository!.GetExamResultAsync(GoldenDatasetConstants.Reports.AutoApprovedExamPassId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Outcome, Is.EqualTo(ExamOutcome.Passed));
            Assert.That(result.ReviewedAt, Is.Not.Null);
            Assert.That(result.ReviewedBy, Is.EqualTo(Actor.ExamFlow.GetDisplayText()));
            Assert.That(result.ActionTaken, Is.EqualTo(ExamResultRecord.AutoApprovedActionTaken));
            Assert.That(result.User.Id, Is.EqualTo(GoldenDatasetConstants.Reports.AutoApprovedExamPassUserId));
            Assert.That(result.Chat.Id, Is.EqualTo(GoldenDatasetConstants.Chats.MainChatId));
        }
    }

    [Test]
    public async Task GetExamResultsAsync_PendingOnly_IncludesPendingFailureExcludesResolvedAndPass()
    {
        var pending = await _repository!.GetExamResultsAsync(pendingOnly: true);

        var ids = pending.Select(r => r.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(GoldenDatasetConstants.Reports.PendingExamFailureId));
            Assert.That(ids, Does.Not.Contain(GoldenDatasetConstants.Reports.ResolvedExamFailureId));
            Assert.That(ids, Does.Not.Contain(GoldenDatasetConstants.Reports.AutoApprovedExamPassId));
            Assert.That(pending.All(r => r.ReviewedAt == null), Is.True);
        }
    }

    [Test]
    public async Task GetExamResultsAsync_All_IncludesAutoApprovedPass()
    {
        var all = await _repository!.GetExamResultsAsync(pendingOnly: false);

        var pass = all.SingleOrDefault(r => r.Id == GoldenDatasetConstants.Reports.AutoApprovedExamPassId);
        Assert.That(pass, Is.Not.Null);
        Assert.That(pass!.Outcome, Is.EqualTo(ExamOutcome.Passed));
    }

    [Test]
    public async Task GetExamResultsAsync_FiltersByChatId()
    {
        var mainChat = await _repository!.GetExamResultsAsync(
            chatId: GoldenDatasetConstants.Chats.MainChatId, pendingOnly: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mainChat.Select(r => r.Id), Does.Contain(GoldenDatasetConstants.Reports.AutoApprovedExamPassId));
            Assert.That(mainChat.All(r => r.Chat.Id == GoldenDatasetConstants.Chats.MainChatId), Is.True);
        }
    }

    // ---- Writer tests: the insert's born-state IS the subject ----

    [Test]
    public async Task InsertExamResultAsync_PassedOutcome_BornCompleted()
    {
        var record = new ExamResultRecord
        {
            User = new UserIdentity(GoldenDatasetConstants.Reports.AutoApprovedExamPassUserId, "Early", "Spirits", "sillywolf"),
            Chat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId),
            Outcome = ExamOutcome.Passed,
            Score = 100,
            PassingThreshold = 80,
            AiEvaluation = "Genuine interest, on-topic answer",
            CompletedAt = DateTimeOffset.UtcNow
        };

        var id = await _repository!.InsertExamResultAsync(record);

        var stored = await _repository.GetExamResultAsync(id);
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Outcome, Is.EqualTo(ExamOutcome.Passed));
            Assert.That(stored.ReviewedAt, Is.Not.Null, "pass records are born completed");
            Assert.That(stored.ActionTaken, Is.EqualTo(ExamResultRecord.AutoApprovedActionTaken));
            Assert.That(stored.ReviewedBy, Is.EqualTo(Actor.ExamFlow.GetDisplayText()));
            Assert.That(stored.AdminNotes, Is.EqualTo("Genuine interest, on-topic answer"));
        }
    }

    [Test]
    public async Task InsertExamResultAsync_FailedOutcome_BornPending()
    {
        var record = new ExamResultRecord
        {
            User = new UserIdentity(GoldenDatasetConstants.Reports.AutoApprovedExamPassUserId, "Early", "Spirits", "sillywolf"),
            Chat = ChatIdentity.FromId(GoldenDatasetConstants.Chats.MainChatId),
            Outcome = ExamOutcome.Failed,
            Score = 20,
            PassingThreshold = 80,
            CompletedAt = DateTimeOffset.UtcNow
        };

        var id = await _repository!.InsertExamResultAsync(record);

        var stored = await _repository.GetExamResultAsync(id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Outcome, Is.EqualTo(ExamOutcome.Failed));
            Assert.That(stored.ReviewedAt, Is.Null, "failures stay pending, unchanged");
            Assert.That(stored.ActionTaken, Is.Null);
        }
    }
}
