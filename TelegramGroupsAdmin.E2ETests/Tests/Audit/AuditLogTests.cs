using System.Text.RegularExpressions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
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
        await Expect(_auditLogPage.PageTitle).ToBeVisibleAsync();
        await Expect(_auditLogPage.PageTitle).ToHaveTextAsync("Audit Log");

        // Verify tabs are visible
        await Expect(_auditLogPage.TabsContainer).ToBeVisibleAsync();
    }

    [Test]
    public async Task AuditLog_Admin_CannotAccess()
    {
        // Arrange - login as Admin (not GlobalAdmin or Owner)
        await LoginAsAdminAsync();

        // Act - try to navigate to audit page
        await Page.GotoAsync("/audit");

        // Assert - Admin should be blocked from accessing the page.
        // Admin is redirected away from /audit (server-side forbid -> /access-denied?ReturnUrl=%2Faudit,
        // or the router's NotAuthorized -> /login). Wait for that navigation to land first so the
        // absence check below cannot pass before the redirect/render completes.
        await Expect(Page).Not.ToHaveURLAsync(new Regex(@"/audit(?:[?#]|$)"));

        // The page should NOT show the audit log content
        await Expect(Page.Locator(".mud-typography-h4:has-text('Audit Log')")).Not.ToBeVisibleAsync();
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
        await Expect(_auditLogPage.WebAdminLogTab).ToHaveAttributeAsync("aria-selected", "true");

        // Verify filters are visible
        await Expect(_auditLogPage.EventTypeFilter).ToBeVisibleAsync();
        await Expect(_auditLogPage.ActorFilter).ToBeVisibleAsync();
        await Expect(_auditLogPage.TargetUserFilter).ToBeVisibleAsync();

        // Verify table headers - the Web Admin Log shows these columns
        await Expect(_auditLogPage.TableHeader("Timestamp")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Event Type")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Actor")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Target")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Details")).ToHaveCountAsync(1);
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

        // Should have exactly 2 audit log entries (one Login, one UserRegistered)
        await Expect(_auditLogPage.TableRows).ToHaveCountAsync(2);

        // Verify the Event Type filter dropdown exists
        await Expect(_auditLogPage.EventTypeFilter).ToBeVisibleAsync();

        // Filter by Login event type
        await _auditLogPage.SelectEventTypeFilterAsync("Login");

        // Wait for the table to show exactly 1 row (the filtered Login event)
        var filteredRows = Page.Locator(".mud-tab-panel:not([hidden]) .mud-table-body tr");
        await Expect(filteredRows).ToHaveCountAsync(1, new() { Timeout = 10000 });

        // Verify the event type chip shows Login
        await Expect(_auditLogPage.LogEntryWithEventType("Login")).ToBeVisibleAsync();

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

        // Should have exactly 2 audit log entries (one per actor)
        await Expect(_auditLogPage.TableRows).ToHaveCountAsync(2);

        // Verify the Actor filter dropdown exists
        await Expect(_auditLogPage.ActorFilter).ToBeVisibleAsync();

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
        await Expect(_auditLogPage.LogEntryWithActor(owner.Email)).ToBeVisibleAsync();

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
        await Expect(_auditLogPage.ModerationLogTab).ToHaveAttributeAsync("aria-selected", "true");

        // Verify moderation log filters are visible
        await Expect(_auditLogPage.ActionTypeFilter).ToBeVisibleAsync();
        await Expect(_auditLogPage.TelegramUserIdFilter).ToBeVisibleAsync();
        await Expect(_auditLogPage.IssuedByFilter).ToBeVisibleAsync();

        // Wait for the Moderation Log table to be fully loaded (6 columns: Timestamp, Action Type, Telegram User, Issued By, Reason, Expires At)
        // Using exact count prevents flaky behavior from tab transition where both panels might briefly match
        await Expect(_auditLogPage.TableHeaders).ToHaveCountAsync(6, new() { Timeout = 10000 });

        // Verify table has the right headers for moderation log
        await Expect(_auditLogPage.TableHeader("Timestamp")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Action Type")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Telegram User")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Issued By")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Reason")).ToHaveCountAsync(1);
        await Expect(_auditLogPage.TableHeader("Expires At")).ToHaveCountAsync(1);
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

        // Verify we have entries before filtering (the Expect above already proved at least one row).
        // The initial count is read (not merely asserted) because the cleared-filter check below
        // waits for the table to return to it.
#pragma warning disable RS0030 // Value is reused below as the cleared-filter expectation; synced by the Expect above
        var initialRowCount = await _auditLogPage.TableRows.CountAsync();
#pragma warning restore RS0030

        // Filter by Ban action type
        await _auditLogPage.SelectActionTypeFilterAsync("Ban");

        // Assert - should only show Ban actions (at least one row, and a Ban entry is shown)
        await Expect(_auditLogPage.TableRows.First).ToBeVisibleAsync();
        await Expect(_auditLogPage.ModerationEntryWithActionType("Ban")).ToBeVisibleAsync();

#pragma warning disable RS0030 // Compared against the cleared count below (not expressible as an Expect); synced by the Expects above
        var filteredRowCount = await _auditLogPage.TableRows.CountAsync();
#pragma warning restore RS0030

        // Clear filter and verify we see more entries
        await _auditLogPage.ClearActionTypeFilterAsync();

        // Wait for table to show at least the initial row count (Expect retries until condition met)
        await Expect(tableRows).ToHaveCountAsync(initialRowCount, new() { Timeout = 5000 });

        // The ToHaveCountAsync above proved the cleared table shows initialRowCount rows,
        // so that is the cleared row count compared here.
        var clearedRowCount = initialRowCount;
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
        // Should have audit log entries
        await Expect(_auditLogPage.TableRows.First).ToBeVisibleAsync();

        // Check that the page displays the details
        // The details column shows the Value field
        await Expect(Page.Locator($"td[data-label='Details']:has-text('{detailsText}')")).ToBeVisibleAsync();

        // Also verify the event type chip is correct
        await Expect(_auditLogPage.LogEntryWithEventType("Permission Changed")).ToBeVisibleAsync();

        // Verify actor column shows the user
        await Expect(_auditLogPage.LogEntryWithActor(owner.Email)).ToBeVisibleAsync();
    }

    #endregion
}
