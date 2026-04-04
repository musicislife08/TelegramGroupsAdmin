using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for the name history panel in UserDetailDialog.razor.
/// Verifies conditional rendering, collapsed state text, data display, and null username handling.
/// </summary>
[TestFixture]
public class UserDetailDialogHistoryTests : MudBlazorTestContext
{
    private ITelegramUserManagementService _mockUserService = null!;
    private IBotModerationService _mockModerationService = null!;
    private IAdminNotesRepository _mockNotesRepo = null!;
    private IUserTagsRepository _mockTagsRepo = null!;
    private ITagDefinitionsRepository _mockTagDefinitionsRepo = null!;
    private IUserActionsRepository _mockActionsRepo = null!;
    private ISnackbar _mockSnackbar = null!;
    private IDialogService _dialogService = null!;

    private const long TestUserId = 123456789;

    public UserDetailDialogHistoryTests()
    {
        // Additional JSInterop setup not in MudBlazorTestContext
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();

        // Add WebUser cascading value (mirrors MainLayout where CascadingValue wraps MudDialogProvider)
        this.AddTestWebUser();

        // Cascade TimeZoneInfo so LocalTimestamp renders formatted dates instead of mdash
        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, TimeZoneInfo.Utc));

        // Create mocks for services used by the dialog
        _mockUserService = Substitute.For<ITelegramUserManagementService>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockSnackbar = Substitute.For<ISnackbar>();
        _mockTagDefinitionsRepo = Substitute.For<ITagDefinitionsRepository>();

        // These repositories satisfy constructor injection but are not directly called by the dialog
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
        Services.AddSingleton(_mockSnackbar);
        Services.AddSingleton(Substitute.For<IProfileScanService>());
        Services.AddSingleton(Substitute.For<ITelegramSessionManager>());
        Services.AddSingleton(Substitute.For<ITelegramUserRepository>());

        // Default setup for tag definitions
        _mockTagDefinitionsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<TagDefinition>()));

        // Default: no history entries
        _mockUserService.GetNameHistoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
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

    [SetUp]
    public async Task SetUp()
    {
        // Clear previously rendered components so each test starts with a fresh DOM.
        // Without this, provider.Markup accumulates dialogs from prior tests in the fixture.
        await DisposeComponentsAsync();

        // Reset mock returns to defaults (NSubstitute retains overrides across tests)
        _mockUserService.GetNameHistoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
    }

    #region Name History Panel Tests

    [Test]
    public void NameHistoryPanel_NotRendered_WhenNoHistory()
    {
        // Arrange — no history entries (default mock returns empty list)
        var userDetail = CreateUserDetail();
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert — expansion panel should not appear when there is no history
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Name History"));
        });

        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void NameHistoryPanel_RenderedCollapsed_WhenHistoryExists()
    {
        // Arrange — return two history records
        var userDetail = CreateUserDetail();
        var history = new List<UsernameHistoryRecord>
        {
            new(1, TestUserId, "olduser", "Old", "Name", DateTimeOffset.UtcNow.AddDays(-10)),
            new(2, TestUserId, "newuser", "New", "Name", DateTimeOffset.UtcNow.AddDays(-1))
        };

        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));
        _mockUserService.GetNameHistoryAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(history));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert — panel text should include the count
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name History (2)"));
        });

        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void NameHistoryPanel_ShowsCorrectData_WhenExpanded()
    {
        // Arrange — a single history entry with known values
        var userDetail = CreateUserDetail();
        var recordedAt = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var history = new List<UsernameHistoryRecord>
        {
            new(1, TestUserId, "prevuser", "Previous", "User", recordedAt)
        };

        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));
        _mockUserService.GetNameHistoryAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(history));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert — username, full name, and formatted date all appear in the markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("@prevuser"));
            Assert.That(provider.Markup, Does.Contain("Previous User"));
            // Date is rendered via entry.RecordedAt.LocalDateTime.ToString("MMM d, yyyy")
            Assert.That(provider.Markup, Does.Contain("Jun"));
        });

        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void NameHistoryPanel_HandlesNullUsername()
    {
        // Arrange — history entry with a null username
        var userDetail = CreateUserDetail();
        var history = new List<UsernameHistoryRecord>
        {
            new(1, TestUserId, null, "Some", "Person", DateTimeOffset.UtcNow.AddDays(-5))
        };

        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(userDetail));
        _mockUserService.GetNameHistoryAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(history));

        var provider = RenderDialogProvider();

        // Act
        var dialogTask = OpenDialogAsync(TestUserId);

        // Assert — null username should render as "(no username)"
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("(no username)"));
        });

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
            KickCount = 0,
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
