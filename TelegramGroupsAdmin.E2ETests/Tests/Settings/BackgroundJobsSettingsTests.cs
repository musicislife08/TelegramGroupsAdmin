using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Tests for the Background Jobs Settings page.
/// Tests job table display, enable/disable toggling, and schedule configuration.
/// Requires Owner role to access (infrastructure settings).
/// </summary>
[TestFixture]
public class BackgroundJobsSettingsTests : AuthenticatedTestBase
{
    private SettingsPage _settingsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsPage = new SettingsPage(Page);
    }

    [Test]
    public async Task BackgroundJobs_PageLoads_ShowsJobTable()
    {
        // Arrange - login as Owner (required for infrastructure settings)
        await LoginAsOwnerAsync();

        // Act - navigate to background jobs page
        await _settingsPage.NavigateToBackgroundJobsAsync();

        // Assert - page title is visible
        await Expect(Page.GetByText("Background Jobs", new() { Exact = true }).First).ToBeVisibleAsync();

        // Assert - the jobs table is visible with header columns
        await Expect(Page.Locator(".mud-table")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Job" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Status" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Schedule" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Last Run" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Next Run" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Actions" })).ToBeVisibleAsync();

        // Assert - at least one job is displayed
        var jobCount = await _settingsPage.GetBackgroundJobCountAsync();
        Assert.That(jobCount, Is.GreaterThanOrEqualTo(1),
            "Should display at least one background job");

        // Assert - Media Refetch Queue status is visible
        await Expect(Page.GetByText("Media Refetch Queue:", new() { Exact = false })).ToBeVisibleAsync();
    }

    [Test]
    public async Task BackgroundJobs_EnableJob_TogglesState()
    {
        // Arrange - login as Owner and navigate to page
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToBackgroundJobsAsync();

        // Wait for table to load
        await Expect(Page.Locator(".mud-table tbody tr").First).ToBeVisibleAsync();

        // Use "Database Maintenance" which is disabled by default per BackgroundJobConfigService
        var jobName = "Database Maintenance";

        // Verify the job starts as disabled (expected default state)
        var initialEnabled = await _settingsPage.IsJobEnabledAsync(jobName);
        Assert.That(initialEnabled, Is.False,
            "Database Maintenance should be disabled by default - if this fails, the default config may have changed");

        // Assert - status chip shows Disabled initially
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Disabled')")).ToBeVisibleAsync();

        // Act - enable the job
        await _settingsPage.ToggleJobAsync(jobName);

        // Assert - snackbar confirms enabling (message includes job name)
        await Expect(Page.Locator(".mud-snackbar").First).ToContainTextAsync("enabled", new() { IgnoreCase = true });

        // Assert - status chip shows Enabled
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Enabled')")).ToBeVisibleAsync();

        // Assert - toggle switch is now checked
        var isEnabled = await _settingsPage.IsJobEnabledAsync(jobName);
        Assert.That(isEnabled, Is.True, "Job should be enabled after toggling");

        // Cleanup - disable the job again to restore original state
        // Wait for the previous snackbar to disappear first
        await Expect(Page.Locator(".mud-snackbar")).Not.ToBeVisibleAsync(new() { Timeout = 10000 });
        await _settingsPage.ToggleJobAsync(jobName);
        // Just verify the toggle happened by checking the chip state, not the snackbar
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Disabled')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task BackgroundJobs_DisableJob_TogglesState()
    {
        // Arrange - login as Owner and navigate to page
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToBackgroundJobsAsync();

        // Wait for table to load
        await Expect(Page.Locator(".mud-table tbody tr").First).ToBeVisibleAsync();

        // Use "Chat Health Monitoring" which is enabled by default per BackgroundJobConfigService
        var jobName = "Chat Health Monitoring";

        // Verify the job starts as enabled (expected default state)
        var initialEnabled = await _settingsPage.IsJobEnabledAsync(jobName);
        Assert.That(initialEnabled, Is.True,
            "Chat Health Monitoring should be enabled by default - if this fails, the default config may have changed");

        // Assert - status chip shows Enabled initially
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Enabled')")).ToBeVisibleAsync();

        // Act - disable the job
        await _settingsPage.ToggleJobAsync(jobName);

        // Assert - snackbar confirms disabling (message includes job name)
        await Expect(Page.Locator(".mud-snackbar").First).ToContainTextAsync("disabled", new() { IgnoreCase = true });

        // Assert - status chip shows Disabled
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Disabled')")).ToBeVisibleAsync();

        // Assert - toggle switch is now unchecked
        var isEnabled = await _settingsPage.IsJobEnabledAsync(jobName);
        Assert.That(isEnabled, Is.False, "Job should be disabled after toggling");

        // Cleanup - enable the job again to restore original state
        // Wait for the previous snackbar to disappear first
        await Expect(Page.Locator(".mud-snackbar")).Not.ToBeVisibleAsync(new() { Timeout = 10000 });
        await _settingsPage.ToggleJobAsync(jobName);
        // Just verify the toggle happened by checking the chip state, not the snackbar
        await Expect(Page.Locator($".mud-table tbody tr:has-text('{jobName}') .mud-chip:has-text('Enabled')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task BackgroundJobs_EditSchedule_UpdatesSchedule()
    {
        // Arrange - login as Owner and navigate to page
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToBackgroundJobsAsync();

        // Wait for table to load
        await Expect(Page.Locator(".mud-table tbody tr").First).ToBeVisibleAsync();

        // Use "Scheduled Backups" (display name per BackgroundJobConfigService)
        var jobName = "Scheduled Backups";

        // Get initial schedule
        var initialSchedule = await _settingsPage.GetJobScheduleAsync(jobName);

        // Act - open config dialog
        await _settingsPage.OpenJobConfigDialogAsync(jobName);

        // Assert - dialog is visible with job name in title
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByText("Configure Scheduled Backups")).ToBeVisibleAsync();

        // Assert - schedule field is visible
        await Expect(dialog.GetByLabel("Schedule")).ToBeVisibleAsync();

        // Update to a new schedule (use "every 6 hours" as a valid natural language schedule)
        var newSchedule = "every 6 hours";
        await _settingsPage.UpdateJobScheduleAsync(newSchedule);

        // Assert - snackbar confirms save
        await Expect(Page.Locator(".mud-snackbar").First).ToContainTextAsync("saved", new() { IgnoreCase = true });

        // Assert - schedule in table is updated
        var updatedSchedule = await _settingsPage.GetJobScheduleAsync(jobName);
        Assert.That(updatedSchedule.Trim(), Is.EqualTo(newSchedule),
            "Schedule should be updated to the new value");

        // Cleanup - restore original schedule if different
        if (!string.IsNullOrEmpty(initialSchedule) && initialSchedule.Trim() != newSchedule)
        {
            await _settingsPage.OpenJobConfigDialogAsync(jobName);
            await _settingsPage.UpdateJobScheduleAsync(initialSchedule.Trim());
        }
    }
}
