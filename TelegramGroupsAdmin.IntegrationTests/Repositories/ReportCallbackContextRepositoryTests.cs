using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for ReportCallbackContextRepository.
///
/// Covers the orphan-based cleanup strategy: contexts whose associated report
/// no longer exists (anti-join against reports) are deleted, while contexts
/// still referencing a live report are retained regardless of age.
/// </summary>
[TestFixture]
public class ReportCallbackContextRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _reportsRepo;
    private IReportCallbackContextRepository? _callbackRepo;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IReportsRepository, ReportsRepository>();
        services.AddScoped<IReportCallbackContextRepository, ReportCallbackContextRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _reportsRepo = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        _callbackRepo = _scope.ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task DeleteOrphanedAsync_RemovesContextsForMissingReports_KeepsContextsForExistingReports()
    {
        // Arrange - insert a real report; its context must be kept.
        var reportId = await _reportsRepo!.InsertContentReportAsync(new Report(
            Id: 0,
            MessageId: 100,
            Chat: new ChatIdentity(-1001L, "TestChat"),
            ReportCommandMessageId: null,
            ReportedByUserId: 200L,
            ReportedByUserName: "tester",
            ReportedAt: DateTimeOffset.UtcNow,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: null),
            CancellationToken.None);

        // Context 1: tied to existing report — must survive cleanup.
        var liveContextId = await _callbackRepo!.CreateAsync(new ReportCallbackContext(
            Id: 0,
            ReportId: reportId,
            ReportType: ReportType.ContentReport,
            ChatId: -1001L,
            UserId: 300L,
            CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Context 2: orphan (ReportId references a report that does not exist) — must be deleted.
        var orphanContextId = await _callbackRepo.CreateAsync(new ReportCallbackContext(
            Id: 0,
            ReportId: 9999L,
            ReportType: ReportType.ContentReport,
            ChatId: -1001L,
            UserId: 300L,
            CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Act
        var deleted = await _callbackRepo.DeleteOrphanedAsync(CancellationToken.None);

        // Assert
        Assert.That(deleted, Is.EqualTo(1), "exactly one orphan deleted");

        var liveContext = await _callbackRepo.GetByIdAsync(liveContextId, CancellationToken.None);
        var orphanContext = await _callbackRepo.GetByIdAsync(orphanContextId, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(liveContext, Is.Not.Null, "context tied to existing report must survive");
            Assert.That(orphanContext, Is.Null, "orphan context must be removed");
        }
    }
}
