using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Telegram.Models;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Audit;

/// <summary>
/// Tests for the Audit Log page (/audit).
/// Verifies Web Admin Log and Telegram Moderation Log tabs, filtering, and permission-based access.
/// Note: This page requires GlobalAdmin or Owner role - Admin cannot access.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class AuditLogTests : SharedAuthenticatedTestBase
{
    private AuditLogPage _auditLogPage = null!;

    [SetUp]
    public void SetUp()
    {
        _auditLogPage = new AuditLogPage(Page);
    }

    #region Access Control Tests

    [Test]
    public async Task AuditLog_PageLoads_RequiresGlobalAdminOrOwner()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to audit page
        await _auditLogPage.NavigateAsync();

        // Assert - page loads successfully with title visible
        Assert.That(await _auditLogPage.IsPageTitleVisibleAsync(), Is.True,
            "Audit Log page title should be visible for GlobalAdmin");

        var pageTitle = await _auditLogPage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Audit Log"),
            "Page title should be 'Audit Log'");

        // Verify tabs are visible
        Assert.That(await _auditLogPage.IsTabsVisibleAsync(), Is.True,
            "Tab container should be visible");
    }

    [Test]
    public async Task AuditLog_Admin_CannotAccess()
    {
        // Arrange - login as Admin (not GlobalAdmin or Owner)
        await LoginAsAdminAsync();

        // Act - try to navigate to audit page
        await Page.GotoAsync("/audit");
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Assert - Admin should be blocked from accessing the page
        // The page should NOT show the audit log content
        var hasAuditLogTitle = await Page.Locator(".mud-typography-h4:has-text('Audit Log')").IsVisibleAsync();

        Assert.That(hasAuditLogTitle, Is.False,
            "Admin should not be able to access the Audit Log page - title should not be visible");

        // Check we're either redirected or see an access denied state
        // Blazor may show "Not Authorized" or redirect to another page
        var url = Page.Url;
        var notOnAuditPage = !url.Contains("/audit") ||
                             await Page.GetByText("Not Authorized").IsVisibleAsync() ||
                             await Page.GetByText("Access Denied").IsVisibleAsync() ||
                             await Page.GetByText("not authorized", new() { Exact = false }).IsVisibleAsync();

        Assert.That(notOnAuditPage || !hasAuditLogTitle, Is.True,
            "Admin should be blocked from audit page - either redirected or shown access denied");
    }

    #endregion

    #region Web Admin Log Tab Tests

    [Test]
    public async Task AuditLog_WebAdminLogTab_DisplaysLogs()
    {
        // Arrange - login as Owner and create audit log entries
        var owner = await LoginAsOwnerAsync();

        // Create some audit log entries
        await new TestAuditLogBuilder(SharedFactory.Services)
            .AsLoginEvent(owner.Id, owner.Email)
            .BuildAsync();

        await new TestAuditLogBuilder(SharedFactory.Services)
            .WithEventType(AuditEventType.UserRegistered)
            .WithWebUserActor(owner.Id, owner.Email)
            .BuildAsync();

        // Act - navigate to audit page
        await _auditLogPage.NavigateAsync();

        // Assert - Web Admin Log tab is active by default
        Assert.That(await _auditLogPage.IsWebAdminLogTabActiveAsync(), Is.True,
            "Web Admin Log tab should be active by default");

        // Verify filters are visible
        Assert.That(await _auditLogPage.IsEventTypeFilterVisibleAsync(), Is.True,
            "Event Type filter should be visible");
        Assert.That(await _auditLogPage.IsActorFilterVisibleAsync(), Is.True,
            "Actor filter should be visible");
        Assert.That(await _auditLogPage.IsTargetUserFilterVisibleAsync(), Is.True,
            "Target User filter should be visible");

        // Verify table headers - the Web Admin Log shows these columns
        var headers = await _auditLogPage.GetTableHeadersAsync();
        Assert.That(headers, Does.Contain("Timestamp"), "Should have Timestamp column");
        Assert.That(headers, Does.Contain("Event Type"), "Should have Event Type column");
        Assert.That(headers, Does.Contain("Actor"), "Should have Actor column");
        Assert.That(headers, Does.Contain("Target"), "Should have Target column");
        Assert.That(headers, Does.Contain("Details"), "Should have Details column");
    }

    [Test]
    public async Task AuditLog_WebAdminLogTab_FilterByEventType()
    {
        // Arrange - login as Owner
        var owner = await LoginAsOwnerAsync();

        // Create exactly one of each event type for deterministic filtering
        await new TestAuditLogBuilder(SharedFactory.Services)
            .AsLoginEvent(owner.Id, owner.Email)
            .BuildAsync();

        await new TestAuditLogBuilder(SharedFactory.Services)
            .WithEventType(AuditEventType.UserRegistered)
            .WithWebUserActor(owner.Id, owner.Email)
            .BuildAsync();

        // Act - navigate to audit page
        await _auditLogPage.NavigateAsync();

        // Wait for the table to load
        var tableRowOrEmpty = Page.Locator(".mud-table-container tr, .mud-table-container td:has-text('No records')");
        await Expect(tableRowOrEmpty.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        var initialRowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(initialRowCount, Is.EqualTo(2),
            "Should have exactly 2 audit log entries (one Login, one UserRegistered)");

        // Verify the Event Type filter dropdown exists
        Assert.That(await _auditLogPage.IsEventTypeFilterVisibleAsync(), Is.True,
            "Event Type filter should be visible");

        // Filter by Login event type
        await _auditLogPage.SelectEventTypeFilterAsync("Login");

        // Wait for the table to show exactly 1 row (the filtered Login event)
        var filteredRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(filteredRows).ToHaveCountAsync(1, new() { Timeout = 10000 });

        // Verify the event type chip shows Login
        var hasLoginEvent = await _auditLogPage.HasLogEntryWithEventTypeAsync("Login");
        Assert.That(hasLoginEvent, Is.True, "Should show Login event after filtering");

        // Clear filter and verify we see all events again
        await _auditLogPage.ClearEventTypeFilterAsync();

        // Wait for the table to show 2 rows (both Login and UserRegistered)
        var tableRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(tableRows).ToHaveCountAsync(2, new() { Timeout = 10000 });
    }

    [Test]
    public async Task AuditLog_WebAdminLogTab_FilterByActor()
    {
        // Arrange - login as Owner and create a GlobalAdmin for a second actor
        var owner = await LoginAsOwnerAsync();
        var globalAdmin = await CreateUserAsync(PermissionLevel.GlobalAdmin);

        // Create exactly one event per actor for deterministic filtering
        await new TestAuditLogBuilder(SharedFactory.Services)
            .AsLoginEvent(owner.Id, owner.Email)
            .BuildAsync();

        await new TestAuditLogBuilder(SharedFactory.Services)
            .AsLoginEvent(globalAdmin.Id, globalAdmin.Email)
            .BuildAsync();

        // Act - navigate to audit page
        await _auditLogPage.NavigateAsync();

        // Wait for the table to load
        var tableRowOrEmpty = Page.Locator(".mud-table-container tr, .mud-table-container td:has-text('No records')");
        await Expect(tableRowOrEmpty.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        var initialRowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(initialRowCount, Is.EqualTo(2),
            "Should have exactly 2 audit log entries (one per actor)");

        // Verify the Actor filter dropdown exists
        Assert.That(await _auditLogPage.IsActorFilterVisibleAsync(), Is.True,
            "Actor filter should be visible");

        // Open the Actor filter dropdown
        var actorSelect = Page.Locator(".mud-select").Filter(new() { HasText = "Actor (Who)" }).First;
        await actorSelect.ClickAsync();

        var popover = Page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // Verify the owner's email appears in the dropdown options
        var ownerOption = popover.Locator(".mud-list-item").Filter(new() { HasText = owner.Email });
        await Expect(ownerOption).ToBeVisibleAsync();

        // Select the owner's email
        await ownerOption.ClickAsync();
        await Expect(popover).Not.ToBeVisibleAsync();

        // Wait for the table to show exactly 1 row (the owner's entry)
        var filteredRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(filteredRows).ToHaveCountAsync(1, new() { Timeout = 10000 });

        // Verify the Actor column shows the owner's email
        var hasOwnerEntry = await _auditLogPage.HasLogEntryWithActorAsync(owner.Email);
        Assert.That(hasOwnerEntry, Is.True, "Should show entry from the selected actor");

        // Clear filter and verify we see all events again
        await _auditLogPage.ClearActorFilterAsync();

        // Wait for the table to show 2 rows (one from each actor)
        var tableRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(tableRows).ToHaveCountAsync(2, new() { Timeout = 10000 });
    }

    #endregion

    #region Telegram Moderation Log Tab Tests

    [Test]
    public async Task AuditLog_ModerationLogTab_DisplaysLogs()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Create a Telegram user for the moderation action
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(111111)
            .WithUsername("spammer")
            .WithName("Spam", "User")
            .BuildAsync();

        // Create moderation action entries
        await new TestUserActionBuilder(SharedFactory.Services)
            .AsBan(111111, "Spam detected by bot protection")
            .BuildAsync();

        // Act - navigate to audit page and switch to Telegram Moderation Log tab
        await _auditLogPage.NavigateAsync();
        await _auditLogPage.SelectTabAsync("Telegram Moderation Log");

        // Assert - Telegram Moderation Log tab is now active
        Assert.That(await _auditLogPage.IsModerationLogTabActiveAsync(), Is.True,
            "Telegram Moderation Log tab should be active after clicking");

        // Verify moderation log filters are visible
        Assert.That(await _auditLogPage.IsActionTypeFilterVisibleAsync(), Is.True,
            "Action Type filter should be visible");
        Assert.That(await _auditLogPage.IsTelegramUserIdFilterVisibleAsync(), Is.True,
            "Telegram User ID filter should be visible");
        Assert.That(await _auditLogPage.IsIssuedByFilterVisibleAsync(), Is.True,
            "Issued By filter should be visible");

        // Wait for the Moderation Log table to be fully loaded (6 columns: Timestamp, Action Type, Telegram User, Issued By, Reason, Expires At)
        // Using exact count prevents flaky behavior from tab transition where both panels might briefly match
        var tableHeaders = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-head th");
        await Expect(tableHeaders).ToHaveCountAsync(6, new() { Timeout = 10000 });

        // Verify table has the right headers for moderation log
        var headers = await _auditLogPage.GetTableHeadersAsync();
        Assert.That(headers, Does.Contain("Timestamp"), "Should have Timestamp column");
        Assert.That(headers, Does.Contain("Action Type"), "Should have Action Type column");
        Assert.That(headers, Does.Contain("Telegram User"), "Should have Telegram User column");
        Assert.That(headers, Does.Contain("Issued By"), "Should have Issued By column");
        Assert.That(headers, Does.Contain("Reason"), "Should have Reason column");
        Assert.That(headers, Does.Contain("Expires At"), "Should have Expires At column");
    }

    [Test]
    public async Task AuditLog_ModerationLogTab_FilterByActionType()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(222222)
            .WithUsername("banneduser")
            .WithName("Banned", "User")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(333333)
            .WithUsername("warneduser")
            .WithName("Warned", "User")
            .BuildAsync();

        // Create diverse moderation actions
        await new TestUserActionBuilder(SharedFactory.Services)
            .AsBan(222222, "Spam detected")
            .BuildAsync();

        await new TestUserActionBuilder(SharedFactory.Services)
            .AsWarn(333333, "First warning")
            .BuildAsync();

        // Act - navigate to moderation log tab
        await _auditLogPage.NavigateToTabAsync("telegram");

        // Wait for table to be visible (prevents flaky timing issues)
        var tableRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(tableRows.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Verify we have entries before filtering
        var initialRowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(initialRowCount, Is.GreaterThan(0),
            "Should have moderation log entries before filtering");

        // Filter by Ban action type
        await _auditLogPage.SelectActionTypeFilterAsync("Ban");

        // Assert - should only show Ban actions
        var filteredRowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(filteredRowCount, Is.GreaterThan(0),
            "Should have Ban actions after filtering");

        // All visible actions should be Ban type
        if (filteredRowCount > 0)
        {
            var hasBanEntry = await _auditLogPage.HasModerationEntryWithActionTypeAsync("Ban");
            Assert.That(hasBanEntry, Is.True, "Should show Ban entries after filtering");
        }

        // Clear filter and verify we see more entries
        await _auditLogPage.ClearActionTypeFilterAsync();
        var clearedRowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(clearedRowCount, Is.GreaterThanOrEqualTo(filteredRowCount),
            "Should show all actions when filter is cleared");
    }

    #endregion

    #region Log Entry Details Test

    [Test]
    public async Task AuditLog_LogEntry_ShowsDetails()
    {
        // Arrange - login as Owner and create audit log entry with details
        var owner = await LoginAsOwnerAsync();

        var detailsText = "Permission changed from Admin to GlobalAdmin";
        await new TestAuditLogBuilder(SharedFactory.Services)
            .WithEventType(AuditEventType.UserPermissionChanged)
            .WithWebUserActor(owner.Id, owner.Email)
            .WithWebUserTarget(owner.Id, owner.Email)
            .WithValue(detailsText)
            .BuildAsync();

        // Act - navigate to audit page
        await _auditLogPage.NavigateAsync();

        // Assert - verify entry details are shown
        var rowCount = await _auditLogPage.GetTableRowCountAsync();
        Assert.That(rowCount, Is.GreaterThan(0),
            "Should have audit log entries");

        // Check that the page displays the details
        // The details column shows the Value field
        await Expect(Page.Locator($"td[data-label='Details']:has-text('{detailsText}')")).ToBeVisibleAsync();

        // Also verify the event type chip is correct
        var hasPermissionChangedEvent = await _auditLogPage.HasLogEntryWithEventTypeAsync("Permission Changed");
        Assert.That(hasPermissionChangedEvent, Is.True,
            "Should show Permission Changed event type");

        // Verify actor column shows the user
        var hasActorEntry = await _auditLogPage.HasLogEntryWithActorAsync(owner.Email);
        Assert.That(hasActorEntry, Is.True,
            "Should show the actor's email in the Actor column");
    }

    #endregion
}
