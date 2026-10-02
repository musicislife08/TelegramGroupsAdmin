using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Background Jobs settings on canonical data as it is, as the canonical Owner. The global job
/// config (configs row chat_id = 0, background_jobs_config) is the state under test.
/// </summary>
[TestFixture]
public class BackgroundJobsGoldenTests : GoldenE2ETestBase
{
    private const string JobDisplayName = "Data Cleanup";
    private const string JobKey = BackgroundJobNames.DataCleanup;

    private SettingsPage _settings = null!;
    private string _originalSchedule = string.Empty;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        // The schedule the table must keep showing, read from the canonical row before the app starts.
        var json = await context.Configs.AsNoTracking()
            .Where(c => c.ChatId == 0).Select(c => c.BackgroundJobsConfig).SingleAsync();
        Assert.That(json, Is.Not.Null, "canonical global config must hold background jobs config");

        using var doc = JsonDocument.Parse(json!);
        _originalSchedule = doc.RootElement.GetProperty("Jobs").GetProperty(JobKey).GetProperty("Schedule").GetString()!;
        Assert.That(_originalSchedule, Is.Not.Empty, "canonical Data Cleanup job must have a schedule");
    }

    [SetUp]
    public async Task LoginAsOwner()
    {
        _settings = new SettingsPage(Page);
        await LoginAsOwnerAsync();
    }

    private async Task<string?> ReadJobsConfigAsync()
    {
        await using var ctx = CreateDbContext();
        return await ctx.Configs.AsNoTracking()
            .Where(c => c.ChatId == 0).Select(c => c.BackgroundJobsConfig).SingleAsync();
    }

    [Test]
    public async Task CancelJobConfigDialog_DiscardsEditsAndLeavesTheScheduleAndStoredConfigUnchanged()
    {
        await _settings.NavigateToBackgroundJobsAsync();

        // Presence first: the row shows the canonical schedule.
        var scheduleCell = _settings.JobScheduleCell(JobDisplayName);
        await Expect(scheduleCell).ToBeVisibleAsync();
        await Expect(scheduleCell).ToHaveTextAsync(_originalSchedule);

        // Stored config as the running app holds it, before any edit.
        var configBefore = await ReadJobsConfigAsync();

        await _settings.OpenJobConfigDialogAsync(JobDisplayName);
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog.GetByText($"Configure {JobDisplayName}")).ToBeVisibleAsync();

        // Edit the schedule and a job-specific retention field, committing each (retention is non-Immediate).
        const string editedSchedule = "every 6 hours";
        const string editedRetention = "5d";
        var scheduleField = dialog.GetByLabel("Schedule");
        await Expect(scheduleField).ToHaveValueAsync(_originalSchedule);
        await scheduleField.FillAsync(editedSchedule);
        await scheduleField.BlurAsync();
        var retentionField = dialog.GetByLabel("Message History");
        await retentionField.FillAsync(editedRetention);
        await retentionField.PressAsync("Tab");
        await Expect(scheduleField).ToHaveValueAsync(editedSchedule);
        await Expect(retentionField).ToHaveValueAsync(editedRetention);

        await _settings.CancelJobConfigDialogAsync();

        // Sync point: the dialog is hidden. The dialog only closes after the Cancel handler (and any
        // awaited save it wrongly performs) has run, so everything below observes the settled state.
        await Expect(dialog).Not.ToBeVisibleAsync();

        // Table still shows the original schedule and nothing was saved.
        await Expect(scheduleCell).ToHaveTextAsync(_originalSchedule);
        await Expect(scheduleCell).Not.ToContainTextAsync(editedSchedule);
        await Expect(_settings.Snackbar).Not.ToBeVisibleAsync();
        Assert.That(await ReadJobsConfigAsync(), Is.EqualTo(configBefore), "Cancel must not change the stored job config");

        // Reopening shows the stored values, not the discarded edits.
        await _settings.OpenJobConfigDialogAsync(JobDisplayName);
        await Expect(dialog.GetByLabel("Schedule")).ToHaveValueAsync(_originalSchedule);
        await Expect(dialog.GetByLabel("Message History")).Not.ToHaveValueAsync(editedRetention);
    }
}
