using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Integration tests for the enriched_reports view.
/// The view is raw SQL, so only a real PostgreSQL round-trip proves the joins resolve.
/// </summary>
[TestFixture]
public class EnrichedReportsViewTests
{
    private const long ContentReportId = 186;
    private const long ExamReportId = 187;
    private const long ProfileReportId = 188;
    private const long SubjectUserId = 9465377455871;

    private MigrationTestHelper? _testHelper;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _testHelper?.Dispose();

    [Test]
    public async Task View_ContentReport_ResolvesContentUserIdToMessageAuthor()
    {
        await using var context = _testHelper!.GetDbContext();

        var view = await context.EnrichedReports
            .AsNoTracking()
            .SingleAsync(r => r.Id == ContentReportId);

        Assert.That(view.ContentUserId, Is.EqualTo(SubjectUserId),
            "content_user_id should resolve through messages.user_id for the reported message");
    }

    [Test]
    public async Task View_NonContentReports_LeaveContentUserIdNull()
    {
        await using var context = _testHelper!.GetDbContext();

        var views = await context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Id == ExamReportId || r.Id == ProfileReportId)
            .ToListAsync();

        Assert.That(views.Select(v => v.ContentUserId), Is.All.Null,
            "the content join is gated on type = 0");
    }
}
