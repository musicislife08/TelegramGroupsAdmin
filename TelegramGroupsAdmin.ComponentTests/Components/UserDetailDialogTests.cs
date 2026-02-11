using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Helpers;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for UserDetailDialog.razor
/// Tests loading states, user profile display, quick action buttons visibility.
/// Uses ITelegramUserManagementService and IBotModerationService interfaces
/// (enabled by Issue #127 interface extraction).
/// </summary>
/// <remarks>
/// MudDialog components require MudDialogProvider to render properly.
/// Tests use the DialogService.ShowAsync pattern and check markup on the provider.
/// </remarks>
[TestFixture]
public class UserDetailDialogTests : MudBlazorTestContext
{
    private ITelegramUserManagementService _mockUserService = null!;
    private IBotModerationService _mockModerationService = null!;
    private IAdminNotesRepository _mockNotesRepo = null!;
    private IUserTagsRepository _mockTagsRepo = null!;
    private ITagDefinitionsRepository _mockTagDefinitionsRepo = null!;
    private IUserActionsRepository _mockActionsRepo = null!;
    private IBlazorAuthHelper _mockAuthHelper = null!;
    private ISnackbar _mockSnackbar = null!;
    private IDialogService _dialogService = null!;

    private const long TestUserId = 123456789;

    public UserDetailDialogTests()
    {
        // Additional JSInterop setup not in MudBlazorTestContext
        // MudBlazor popover components require these handlers to return void results
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();

        // Create mocks for services used by the dialog
        _mockUserService = Substitute.For<ITelegramUserManagementService>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockAuthHelper = Substitute.For<IBlazorAuthHelper>();
        _mockSnackbar = Substitute.For<ISnackbar>();
        _mockTagDefinitionsRepo = Substitute.For<ITagDefinitionsRepository>();

        // These repositories are registered for DI but intentionally unconfigured.
        // The dialog reads Notes/Tags/Actions from TelegramUserDetail (returned by user service),
        // not directly from these repos. These mocks only exist to satisfy constructor injection.
        _mockNotesRepo = Substitute.For<IAdminNotesRepository>();
        _mockTagsRepo = Substitute.For<IUserTagsRepository>();
        _mockActionsRepo = Substitute.For<IUserActionsRepository>();

        // Register services
        Services.AddSingleton(_mockUserService);
        Services.AddSingleton(_mockModerationService);
        Services.AddSingleton(_mockNotesRepo);
        Services.AddSingleton(_mockTagsRepo);
        Services.AddSingleton(_mockTagDefinitionsRepo);
        Services.AddSingleton(_mockActionsRepo);
        Services.AddSingleton(_mockAuthHelper);
        Services.AddSingleton(_mockSnackbar);

        // Default setup for tag definitions
        _mockTagDefinitionsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<TagDefinition>()));
    }

    private IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        _dialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }

    private Task<IDialogReference> OpenDialogAsync(long userId)
    {
        var parameters = new DialogParameters<UserDetailDialog>
        {
            { x => x.UserId, userId }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Large,
            FullWidth = true
        };

        return _dialogService.ShowAsync<UserDetailDialog>("User Details", parameters, options);
    }

    #region Loading State Tests

    [Test]
    public void ShowsLoadingIndicator_WhenInitializing()
    {
        // Arrange - delay user service response to keep loading state
        var tcs = new TaskCompletionSource<TelegramUserDetail?>();
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert - should show progress indicator
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-progress-circular"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ShowsUserNotFound_WhenUserIsNull()
    {
        // Arrange
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(null));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("User not found"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion

    #region User Profile Display Tests

    [Test]
    public void DisplaysUserProfile_WhenLoaded()
    {
        // Arrange
        var userDetail = CreateUserDetail();
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain(userDetail.DisplayName));
            Assert.That(provider.Markup, Does.Contain(TestUserId.ToString()));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void DisplaysUsername_WhenPresent()
    {
        // Arrange
        var userDetail = CreateUserDetail(username: "testuser");
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("@testuser"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion

    #region Quick Action Visibility Tests

    [Test]
    public void ShowsBanButtons_WhenUserNotBanned()
    {
        // Arrange
        var userDetail = CreateUserDetail(isBanned: false);
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert - check for Quick Actions buttons (the specific button text rendering varies)
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Quick Actions"));
            Assert.That(provider.Markup, Does.Contain("Temp Ban"));
            Assert.That(provider.Markup, Does.Contain("Warn"));
            Assert.That(provider.Markup, Does.Not.Contain("Unban"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ShowsUnbanButton_WhenUserIsBanned()
    {
        // Arrange
        var userDetail = CreateUserDetail(isBanned: true);
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert - check Unban visible, Temp Ban/Warn not visible
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Unban"));
            Assert.That(provider.Markup, Does.Not.Contain("Temp Ban"));
            Assert.That(provider.Markup, Does.Not.Contain("Warn</span>"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ShowsTrustButton_WhenUserNotTrusted()
    {
        // Arrange
        var userDetail = CreateUserDetail(isTrusted: false);
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Trust User"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ShowsRemoveTrustButton_WhenUserIsTrusted()
    {
        // Arrange
        var userDetail = CreateUserDetail(isTrusted: true);
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Remove Trust"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion

    #region Admin Notes Display Tests

    [Test]
    public void ShowsNoNotesMessage_WhenEmpty()
    {
        // Arrange
        var userDetail = CreateUserDetail();
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No notes yet"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void DisplaysNotes_WhenPresent()
    {
        // Arrange
        var userDetail = CreateUserDetail();
        userDetail.Notes.Add(new AdminNote
        {
            Id = 1,
            TelegramUserId = TestUserId,
            NoteText = "Test note content",
            CreatedBy = Actor.FromSystem("Admin"),
            CreatedAt = DateTimeOffset.UtcNow
        });
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Test note content"));
        });

        // Verify no exception was thrown during dialog open
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public void ShowsErrorSnackbar_WhenServiceThrowsException()
    {
        // Arrange
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database connection failed"));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert - snackbar receives error message (may be called multiple times due to re-renders)
        provider.WaitForAssertion(() =>
        {
            _mockSnackbar.Received().Add(
                Arg.Is<string>(s => s.Contains("Error loading user detail")),
                Severity.Error);
        });

        // Verify no exception was thrown during dialog open (exception is caught internally)
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion

    #region Helper Methods

    private static TelegramUserDetail CreateUserDetail(
        string? username = null,
        bool isTrusted = false,
        bool isBanned = false)
    {
        return new TelegramUserDetail
        {
            User = new UserIdentity(TestUserId, "Test", "User", username),
            IsTrusted = isTrusted,
            IsBanned = isBanned,
            BotDmEnabled = false,
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastSeenAt = DateTimeOffset.UtcNow.AddHours(-1),
            ChatMemberships = [],
            Actions = [],
            DetectionHistory = [],
            Notes = [],
            Tags = [],
            Warnings = []
        };
    }

    #endregion
}
