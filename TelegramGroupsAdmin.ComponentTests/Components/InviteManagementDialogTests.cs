using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using System.Security.Claims;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for InviteManagementDialog tests.
/// Registers mocked IInviteService and AuthenticationStateProvider.
/// </summary>
public class InviteManagementDialogTestContext : BunitContext
{
    protected IInviteService InviteService { get; }
    protected AuthenticationStateProvider AuthStateProvider { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected InviteManagementDialogTestContext()
    {
        // Create mocks
        InviteService = Substitute.For<IInviteService>();
        AuthStateProvider = Substitute.For<AuthenticationStateProvider>();

        // Setup default auth state with authenticated user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Name, "testuser@example.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(principal);
        AuthStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        // Default: return empty invite list
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns([]);

        // Register mocks
        Services.AddSingleton(InviteService);
        Services.AddSingleton(AuthStateProvider);

        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Setup JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for InviteManagementDialog.razor
/// Tests the dialog for managing system invites.
/// </summary>
[TestFixture]
public class InviteManagementDialogTests : InviteManagementDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        InviteService.ClearReceivedCalls();
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns([]);
    }

    #region Helper Methods

    private async Task<IDialogReference> OpenDialogAsync(PermissionLevel currentUserPermissionLevel = PermissionLevel.Owner)
    {
        var parameters = new DialogParameters<InviteManagementDialog>
        {
            { x => x.CurrentUserPermissionLevel, currentUserPermissionLevel }
        };
        return await DialogService.ShowAsync<InviteManagementDialog>("Manage Invites", parameters);
    }

    private static InviteWithCreator CreateTestInvite(
        string token = "test-token-123",
        InviteStatus status = InviteStatus.Pending,
        PermissionLevel permissionLevel = PermissionLevel.Admin)
    {
        return new InviteWithCreator(
            Token: token,
            CreatedBy: "creator-id",
            CreatedByEmail: "creator@example.com",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(6),
            UsedBy: null,
            UsedByEmail: null,
            PermissionLevel: permissionLevel,
            Status: status,
            ModifiedAt: DateTimeOffset.UtcNow
        );
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-content"));
        });
    }

    [Test]
    public void HasDialogActions()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-actions"));
        });
    }

    [Test]
    public void HasCloseButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });
    }

    [Test]
    public void DisplaysTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Manage Invites"));
        });
    }

    #endregion

    #region Filter Tests

    [Test]
    public void HasFilterDropdown()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Filter"));
        });
    }

    #endregion

    #region Empty State Tests

    [Test]
    public void ShowsEmptyAlert_WhenNoInvites()
    {
        // Arrange
        var provider = RenderDialogProvider();
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns([]);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No invites found"));
        });
    }

    #endregion

    #region Table Tests

    [Test]
    public void DisplaysTable_WhenInvitesExist()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite() };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-table"));
        });
    }

    [Test]
    public void DisplaysTableHeaders()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite() };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Created By"));
            Assert.That(provider.Markup, Does.Contain("Created"));
            Assert.That(provider.Markup, Does.Contain("Permission"));
            Assert.That(provider.Markup, Does.Contain("Expires"));
            Assert.That(provider.Markup, Does.Contain("Status"));
            Assert.That(provider.Markup, Does.Contain("Actions"));
        });
    }

    [Test]
    public void DisplaysCreatorEmail()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite() };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("creator@example.com"));
        });
    }

    #endregion

    #region Status Display Tests

    [Test]
    public void DisplaysActiveStatus_ForPendingInvite()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(status: InviteStatus.Pending) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Active"));
        });
    }

    [Test]
    public void DisplaysUsedStatus_ForUsedInvite()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(status: InviteStatus.Used) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Used"));
        });
    }

    [Test]
    public void DisplaysRevokedStatus_ForRevokedInvite()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(status: InviteStatus.Revoked) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Revoked"));
        });
    }

    #endregion

    #region Permission Level Tests

    [Test]
    public void DisplaysAdminPermission()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Admin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Admin"));
        });
    }

    [Test]
    public void DisplaysOwnerPermission()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Owner) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Owner"));
        });
    }

    [Test]
    public void DisplaysGlobalAdminPermission()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.GlobalAdmin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GlobalAdmin"));
        });
    }

    #endregion

    #region Permission-Based Button State Tests

    [Test]
    public void AdminUser_CanManageAdminInvite()
    {
        // Arrange - Admin user viewing an Admin-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Admin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.Admin);

        // Assert - Buttons should be enabled (not disabled)
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            // None should be disabled
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons, Is.Empty, "Admin should be able to manage Admin-level invites");
        });
    }

    [Test]
    public void AdminUser_CannotManageGlobalAdminInvite()
    {
        // Arrange - Admin user viewing a GlobalAdmin-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.GlobalAdmin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.Admin);

        // Assert - Buttons should be disabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            // All should be disabled
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons.Count, Is.EqualTo(iconButtons.Count),
                "Admin should not be able to manage GlobalAdmin-level invites");
        });
    }

    [Test]
    public void AdminUser_CannotManageOwnerInvite()
    {
        // Arrange - Admin user viewing an Owner-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Owner) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.Admin);

        // Assert - Buttons should be disabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            // All should be disabled
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons.Count, Is.EqualTo(iconButtons.Count),
                "Admin should not be able to manage Owner-level invites");
        });
    }

    [Test]
    public void GlobalAdminUser_CanManageAdminInvite()
    {
        // Arrange - GlobalAdmin user viewing an Admin-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Admin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.GlobalAdmin);

        // Assert - Buttons should be enabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons, Is.Empty, "GlobalAdmin should be able to manage Admin-level invites");
        });
    }

    [Test]
    public void GlobalAdminUser_CanManageGlobalAdminInvite()
    {
        // Arrange - GlobalAdmin user viewing a GlobalAdmin-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.GlobalAdmin) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.GlobalAdmin);

        // Assert - Buttons should be enabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons, Is.Empty, "GlobalAdmin should be able to manage GlobalAdmin-level invites");
        });
    }

    [Test]
    public void GlobalAdminUser_CannotManageOwnerInvite()
    {
        // Arrange - GlobalAdmin user viewing an Owner-level invite
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(permissionLevel: PermissionLevel.Owner) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.GlobalAdmin);

        // Assert - Buttons should be disabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            Assert.That(iconButtons.Count, Is.GreaterThanOrEqualTo(2), "Should have copy and revoke buttons");
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons.Count, Is.EqualTo(iconButtons.Count),
                "GlobalAdmin should not be able to manage Owner-level invites");
        });
    }

    [Test]
    public void OwnerUser_CanManageAllInvites()
    {
        // Arrange - Owner user viewing invites at all permission levels
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator>
        {
            CreateTestInvite(token: "admin-invite", permissionLevel: PermissionLevel.Admin),
            CreateTestInvite(token: "global-invite", permissionLevel: PermissionLevel.GlobalAdmin),
            CreateTestInvite(token: "owner-invite", permissionLevel: PermissionLevel.Owner)
        };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.Owner);

        // Assert - All buttons should be enabled
        provider.WaitForAssertion(() =>
        {
            var iconButtons = provider.FindAll(".mud-icon-button");
            // 3 invites x 2 buttons each = 6 buttons
            Assert.That(iconButtons.Count, Is.EqualTo(6), "Should have 6 buttons (copy + revoke for each invite)");
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons, Is.Empty, "Owner should be able to manage all invites");
        });
    }

    [Test]
    public void AdminUser_SeesAllInvitesButCanOnlyManageOwnLevel()
    {
        // Arrange - Admin user viewing invites at all permission levels
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator>
        {
            CreateTestInvite(token: "admin-invite", permissionLevel: PermissionLevel.Admin),
            CreateTestInvite(token: "global-invite", permissionLevel: PermissionLevel.GlobalAdmin),
            CreateTestInvite(token: "owner-invite", permissionLevel: PermissionLevel.Owner)
        };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync(PermissionLevel.Admin);

        // Assert - All invites visible but only Admin-level buttons enabled
        provider.WaitForAssertion(() =>
        {
            // All invites should be visible
            Assert.That(provider.Markup, Does.Contain("Admin"));
            Assert.That(provider.Markup, Does.Contain("GlobalAdmin"));
            Assert.That(provider.Markup, Does.Contain("Owner"));

            var iconButtons = provider.FindAll(".mud-icon-button");
            // 3 invites x 2 buttons each = 6 buttons
            Assert.That(iconButtons.Count, Is.EqualTo(6), "Should have 6 buttons (copy + revoke for each invite)");

            // Only 2 buttons (for Admin invite) should be enabled, 4 should be disabled
            var disabledButtons = iconButtons.Where(b => b.HasAttribute("disabled")).ToList();
            Assert.That(disabledButtons.Count, Is.EqualTo(4),
                "Admin can only manage Admin-level invites (2 enabled, 4 disabled)");
        });
    }

    #endregion

    #region Action Buttons Tests

    [Test]
    public void ShowsCopyButton_ForPendingInvite()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var invites = new List<InviteWithCreator> { CreateTestInvite(status: InviteStatus.Pending) };
        InviteService.GetAllInvitesAsync(Arg.Any<string>()).Returns(invites);

        // Act
        _ = OpenDialogAsync();

        // Assert - Look for ContentCopy icon button
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-icon-button"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void CloseButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });

        // Act - Click close button
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Close");
        closeButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
