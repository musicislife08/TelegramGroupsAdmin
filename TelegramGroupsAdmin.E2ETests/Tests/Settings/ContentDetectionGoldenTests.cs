using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Content Detection settings on canonical data as it is: the Training Samples page
/// (/settings/training-data/samples) and the Training Mode switch on the Detection Algorithms page
/// (/settings/content-detection/algorithms), as the canonical Owner. The add-sample and save writes
/// are the assertion subjects; everything the UI shows is read back from this test's clone.
/// </summary>
/// <remarks>
/// The Training Samples list is <c>detection_results</c> rows that are the current verdict of a
/// message in the <c>message_verdicts</c> view with a curated classification
/// (<see cref="VerdictClassifications.Curated"/>); a manually added sample is a <c>chat_id = 0</c>
/// message with a negative <c>message_id</c> plus a <see cref="VerdictSource.TrainingDataPage"/>
/// decision row. The Source filter keys off the view's <c>source</c> column and labels options with
/// <c>VerdictSource.ToDisplayText()</c>. The table has no pager: every filtered row renders, and the
/// "Showing X of Y samples" counter is the page's own count of the same list.
/// </remarks>
[TestFixture]
public class ContentDetectionGoldenTests : GoldenE2ETestBase
{
    /// <summary>
    /// The Source filter anchor: a source that some, but not all, canonical training samples carry,
    /// so filtering on it visibly narrows the list. Guarded in <see cref="ArrangeDataAsync"/>; the
    /// counts themselves are read from the clone at runtime (canonical 2026-10-01: 69 of 297 curated
    /// current verdicts are AutoBan decisions; LegacyManual 119, ContentScan 78, Import 20, the rest
    /// under 5 each).
    /// </summary>
    private const VerdictSource FilterSource = VerdictSource.AutoBan;

    private SettingsPage _settings = null!;
    private int _curatedTotal;
    private int _curatedWithFilterSource;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        // The Training Samples page lists curated current verdicts; count them exactly as the
        // repository does (GetTrainingDataStatsAsync), by source, and pin the shapes the tests rely on.
        var bySource = await context.MessageVerdicts.AsNoTracking()
            .Where(v => v.VerdictId != null && VerdictClassifications.CuratedValues.Contains(v.Classification))
            .GroupBy(v => v.Source!.Value)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync();

        _curatedTotal = bySource.Sum(s => s.Count);
        _curatedWithFilterSource = bySource.Where(s => s.Source == (int)FilterSource).Sum(s => s.Count);

        Assert.That(_curatedTotal, Is.GreaterThan(0), "canonical must hold training samples");
        Assert.That(_curatedWithFilterSource, Is.GreaterThan(0).And.LessThan(_curatedTotal),
            $"the Source filter anchor {FilterSource} must narrow the list without emptying it");

        // The global content detection config row has Training Mode off, so turning it on is a real change.
        var globalConfig = await context.ContentDetectionConfigs.AsNoTracking()
            .Where(c => c.Id == GoldenDatasetConstants.ContentDetectionConfigs.GlobalRowId)
            .Select(c => new { c.ChatId, c.Config!.TrainingMode }).SingleAsync();
        Assert.That(globalConfig.ChatId, Is.EqualTo(0));
        Assert.That(globalConfig.TrainingMode, Is.False, "canonical global config must have Training Mode off");
    }

    [SetUp]
    public async Task LoginAsOwner()
    {
        _settings = new SettingsPage(Page);
        await LoginAsOwnerAsync();
    }

    #region Training samples

    [TestCase(true)]
    [TestCase(false)]
    public async Task AddTrainingSample_AppearsInTheListAndIsStoredAsATrainingDataPageDecision(bool isSpam)
    {
        var text = $"e2e synthetic {(isSpam ? "spam" : "clean")} sample {Guid.NewGuid():N}";
        await _settings.NavigateToTrainingSamplesAsync();
        await Expect(_settings.TrainingSamplesShowingText).ToHaveTextAsync($"Showing {_curatedTotal} of {_curatedTotal} samples");

        await _settings.ClickAddTrainingSampleAsync();
        await _settings.FillAndSubmitAddSampleDialogAsync(text, isSpam);

        await Expect(_settings.SnackbarWithText("Training sample added successfully")).ToBeVisibleAsync();
        await Expect(_settings.TrainingSample(text)).ToHaveCountAsync(1);
        await Expect(_settings.TrainingSampleTypeChip(text)).ToHaveTextAsync(isSpam ? "SPAM" : "CLEAN");
        await Expect(_settings.TrainingSampleSourceChip(text)).ToHaveTextAsync(VerdictSource.TrainingDataPage.ToDisplayText());
        await Expect(_settings.TrainingSamplesShowingText)
            .ToHaveTextAsync($"Showing {_curatedTotal + 1} of {_curatedTotal + 1} samples");

        await using var ctx = CreateDbContext();
        var message = await ctx.Messages.AsNoTracking()
            .Where(m => m.MessageText == text)
            .Select(m => new { m.ChatId, m.MessageId, m.UserId }).SingleAsync();
        Assert.That(message.ChatId, Is.EqualTo(0), "manual samples live in chat 0");
        Assert.That(message.MessageId, Is.LessThan(0), "manual samples take negative message ids");
        Assert.That(message.UserId, Is.EqualTo(0));

        var decision = await ctx.DetectionResults.AsNoTracking()
            .Where(d => d.ChatId == 0 && d.MessageId == message.MessageId)
            .Select(d => new { d.Id, d.Source, d.Classification, d.IsSpam, d.WebUserId, d.DetectionMethod }).SingleAsync();
        Assert.That((VerdictSource)decision.Source, Is.EqualTo(VerdictSource.TrainingDataPage));
        Assert.That((VerdictClassification)decision.Classification,
            Is.EqualTo(isSpam ? VerdictClassification.ExplicitSpam : VerdictClassification.ExplicitHam));
        Assert.That(decision.IsSpam, Is.EqualTo(isSpam));
        Assert.That(decision.WebUserId, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerId), "the Owner is the recorded actor");
        Assert.That(decision.DetectionMethod, Is.EqualTo(nameof(VerdictSource.TrainingDataPage)));

        // The view resolves the new message to that decision, which is what the list reads.
        var verdict = await ctx.MessageVerdicts.AsNoTracking()
            .Where(v => v.ChatId == 0 && v.MessageId == message.MessageId)
            .Select(v => new { v.VerdictId, v.IsSpam, v.Source }).SingleAsync();
        Assert.That(verdict.VerdictId, Is.EqualTo(decision.Id));
        Assert.That(verdict.IsSpam, Is.EqualTo(isSpam));
        Assert.That(verdict.Source, Is.EqualTo((int)VerdictSource.TrainingDataPage));
    }

    [Test]
    public async Task SourceFilter_NarrowsTheRowsToTheChosenSource()
    {
        var sourceLabel = FilterSource.ToDisplayText();
        await _settings.NavigateToTrainingSamplesAsync();
        await Expect(_settings.TrainingSamplesShowingText).ToHaveTextAsync($"Showing {_curatedTotal} of {_curatedTotal} samples");
        await Expect(_settings.TrainingSampleRows).ToHaveCountAsync(_curatedTotal);

        await _settings.SelectSourceFilterAsync(sourceLabel);

        await Expect(_settings.TrainingSamplesShowingText)
            .ToHaveTextAsync($"Showing {_curatedWithFilterSource} of {_curatedTotal} samples");
        await Expect(_settings.TrainingSampleRows).ToHaveCountAsync(_curatedWithFilterSource);
        // Every remaining row carries the chosen source: as many matching chips as rows, and no other chips.
        await Expect(_settings.TrainingSampleSourceChips.Filter(new() { HasText = sourceLabel }))
            .ToHaveCountAsync(_curatedWithFilterSource);
        await Expect(_settings.TrainingSampleSourceChips).ToHaveCountAsync(_curatedWithFilterSource);

        await _settings.SelectSourceFilterAsync("All Sources");

        await Expect(_settings.TrainingSamplesShowingText).ToHaveTextAsync($"Showing {_curatedTotal} of {_curatedTotal} samples");
        await Expect(_settings.TrainingSampleRows).ToHaveCountAsync(_curatedTotal);
    }

    #endregion

    #region Training mode

    [Test]
    public async Task TrainingMode_ToggleAndSave_PersistsAcrossReload()
    {
        await _settings.NavigateToDetectionAlgorithmsAsync();
        await Expect(_settings.TrainingModeToggleInput).Not.ToBeCheckedAsync();
        await Expect(_settings.TrainingModeActiveChip).ToHaveCountAsync(0);

        await _settings.ToggleTrainingModeAsync();
        await Expect(_settings.TrainingModeToggleInput).ToBeCheckedAsync();
        await Expect(_settings.TrainingModeActiveChip).ToBeVisibleAsync();

        await _settings.ClickSaveAllChangesAsync();
        await Expect(_settings.SnackbarWithText("Global configuration saved successfully")).ToBeVisibleAsync();

        // A fresh navigation starts a new circuit that reads the config again.
        await _settings.NavigateToDetectionAlgorithmsAsync();
        await Expect(_settings.TrainingModeToggleInput).ToBeCheckedAsync();
        await Expect(_settings.TrainingModeActiveChip).ToBeVisibleAsync();

        Assert.That(await ReadGlobalTrainingModeAsync(), Is.True);
    }

    [Test]
    public async Task TrainingMode_ToggleWithoutSave_IsNotPersisted()
    {
        await _settings.NavigateToDetectionAlgorithmsAsync();
        await Expect(_settings.TrainingModeToggleInput).Not.ToBeCheckedAsync();

        await _settings.ToggleTrainingModeAsync();
        await Expect(_settings.TrainingModeToggleInput).ToBeCheckedAsync();
        await Expect(_settings.TrainingModeActiveChip).ToBeVisibleAsync();

        await _settings.NavigateToDetectionAlgorithmsAsync();
        // The switch is the presence check for the reloaded render; the chip is absent from it.
        await Expect(_settings.TrainingModeToggleInput).Not.ToBeCheckedAsync();
        await Expect(_settings.TrainingModeActiveChip).ToHaveCountAsync(0);

        Assert.That(await ReadGlobalTrainingModeAsync(), Is.False);
    }

    #endregion

    private async Task<bool> ReadGlobalTrainingModeAsync()
    {
        await using var ctx = CreateDbContext();
        return await ctx.ContentDetectionConfigs.AsNoTracking()
            .Where(c => c.ChatId == 0)
            .Select(c => c.Config!.TrainingMode).SingleAsync();
    }
}
