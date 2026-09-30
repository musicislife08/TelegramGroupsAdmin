using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for login functionality.
/// These tests validate the TestUserBuilder infrastructure and login flows.
/// Uses SharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class LoginTests : SharedE2ETestBase
{
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetUp()
    {
        _loginPage = new LoginPage(Page);
    }

    [Test]
    public async Task Login_WithValidCredentials_RedirectsToHome()
    {
        // Arrange - create a verified user who can login
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("login"))
            .WithStandardPassword()
            .WithEmailVerified()
            .AsOwner() // Owner so they have full access
            .BuildAsync();

        // Act - navigate to login and submit credentials
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should redirect to home page after successful login
        await _loginPage.WaitForRedirectAsync();

        // Verify we're on the home page (not login or error) using Playwright's auto-retry assertion
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/login"));
    }

    [Test]
    public async Task Login_WithInvalidPassword_ShowsError()
    {
        // Arrange - create a verified user
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("invalid"))
            .WithStandardPassword()
            .WithEmailVerified()
            .BuildAsync();

        // Act - try to login with wrong password
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, "WrongPassword123!");

        // Assert - should show error message and stay on login page
        await Expect(_loginPage.ErrorAlert).ToBeVisibleAsync();
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithUnverifiedEmail_ShowsVerificationError()
    {
        // Arrange - create user WITHOUT email verification
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("unverified"))
            .WithStandardPassword()
            .WithEmailVerified(false) // Email not verified
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should show verification required message
        await Expect(_loginPage.ErrorAlert).ToContainTextAsync(new Regex("verify"), new() { IgnoreCase = true });
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithLockedAccount_ShowsLockedMessage()
    {
        // Arrange - create a locked user
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("locked"))
            .WithStandardPassword()
            .WithEmailVerified()
            .LockedFor(TimeSpan.FromMinutes(30))
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should show locked account message
        await Expect(_loginPage.ErrorAlert).ToContainTextAsync(new Regex("locked"), new() { IgnoreCase = true });
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithDisabledAccount_ShowsDisabledMessage()
    {
        // Arrange - create a disabled user
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("disabled"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithStatus(Core.Models.UserStatus.Disabled)
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should show disabled account message
        await Expect(_loginPage.ErrorAlert).ToContainTextAsync(new Regex("disabled"), new() { IgnoreCase = true });
        await AssertUrlAsync("/login");
    }
}
