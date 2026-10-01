using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Reports;

/// <summary>
/// The exam review card's Multiple Choice chips on canonical data, as the Owner: the canonical
/// auto-approved exam pass (<see cref="GoldenDatasetConstants.Reports.AutoApprovedExamPassId"/>, score above its
/// passing threshold, in a chat whose welcome config carries an MC question) rendered under
/// "Exam Reviews" + "All Statuses". Read-only — nothing is written.
/// </summary>
[TestFixture]
public class ReportsExamChipsGoldenTests : GoldenE2ETestBase
{
    private const long ExamId = GoldenDatasetConstants.Reports.AutoApprovedExamPassId;
    private const long ExamUserId = GoldenDatasetConstants.Reports.AutoApprovedExamPassUserId;

    private ReportsPage _reports = null!;
    private int _answered;
    private int _correct;
    private int _score;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var report = await context.Reports.AsNoTracking()
            .Where(r => r.Id == ExamId)
            .Select(r => new { r.Type, r.Context })
            .SingleAsync();
        Assert.That(report.Type, Is.EqualTo((short)ReportType.ExamResult));
        Assert.That(report.Context, Is.Not.Null);

        // Independent mirror of the card's rule: an answer is correct when the shuffled option at the
        // picked letter's position is original index 0.
        using var json = JsonDocument.Parse(report.Context!);
        var root = json.RootElement;
        _score = root.GetProperty("score").GetInt32();
        var threshold = root.GetProperty("passingThreshold").GetInt32();
        Assert.That(_score, Is.GreaterThanOrEqualTo(threshold), "the anchor exam must be above its passing threshold");

        _answered = 0;
        _correct = 0;
        var answers = root.GetProperty("mcAnswers");
        var shuffle = root.GetProperty("shuffleState");
        foreach (var answer in answers.EnumerateObject())
        {
            _answered++;
            var position = answer.Value.GetString()![0] - 'A';
            if (shuffle.GetProperty(answer.Name)[position].GetInt32() == 0)
            {
                _correct++;
            }
        }
        Assert.That(_answered, Is.GreaterThan(0));
    }

    [SetUp]
    public async Task OpenExamReviewsAsOwner()
    {
        _reports = new ReportsPage(Page);
        await LoginAsOwnerAsync();
        await _reports.NavigateAsync();
        await _reports.WaitForLoadAsync();
        await _reports.SelectTypeFilterAsync("Exam Reviews");
        await _reports.SelectStatusFilterAsync("All Statuses");
    }

    private Microsoft.Playwright.ILocator Card =>
        _reports.ExamReviewCards.Filter(new() { HasTextRegex = new Regex($@"ID:\s*{ExamUserId}(?!\d)") });

    [Test]
    public async Task McSection_ShowsTheScoreChipFromTheStoredResult()
    {
        await Expect(Card).ToHaveCountAsync(1);

        await Expect(ReportsPage.ExamMcSectionHeaderIn(Card).Locator(".mud-chip").Filter(new() { HasText = "correct" }))
            .ToContainTextAsync($"{_correct}/{_answered} correct ({_score}%)");
    }

    [Test]
    public async Task McSection_ShowsPassedAndNotFailed_ForAnAboveThresholdExam()
    {
        await Expect(Card).ToHaveCountAsync(1);

        var header = ReportsPage.ExamMcSectionHeaderIn(Card);
        await Expect(header.Locator(".mud-chip").Filter(new() { HasTextRegex = new Regex(@"^\s*Passed\s*$") })).ToHaveCountAsync(1);
        await Expect(header.Locator(".mud-chip").Filter(new() { HasTextRegex = new Regex(@"^\s*Failed\s*$") })).ToHaveCountAsync(0);

        // The page-object chip is scoped to the MC section: exactly the one MC chip, never the card's
        // "Passed — auto-admitted" chip nor the AI evaluation chip (both also contain "Passed" text).
        await Expect(_reports.ExamMcPassedChip).ToHaveCountAsync(1);
        await Expect(Card.GetByText("Passed — auto-admitted")).ToHaveCountAsync(1);
        await Expect(_reports.ExamMcFailedChip).ToHaveCountAsync(0);
    }
}
