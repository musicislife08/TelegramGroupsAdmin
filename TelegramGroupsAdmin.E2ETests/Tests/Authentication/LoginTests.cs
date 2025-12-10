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
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("login"))
            .WithPassword(password)
            .WithEmailVerified()
            .AsOwner() // Owner so they have full access
            .BuildAsync();

        // Act - navigate to login and submit credentials
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should redirect to home page after successful login
        await _loginPage.WaitForRedirectAsync();

        // Verify we're on the home page (not login or error) using Playwright's auto-retry assertion
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task Login_WithInvalidPassword_ShowsError()
    {
        // Arrange - create a verified user
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("invalid"))
            .WithPassword(TestCredentials.GeneratePassword())
            .WithEmailVerified()
            .BuildAsync();

        // Act - try to login with wrong password
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, "WrongPassword123!");

        // Assert - should show error message and stay on login page
        Assert.That(await _loginPage.HasErrorMessageAsync(), Is.True,
            "Should display error message for invalid credentials");
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithUnverifiedEmail_ShowsVerificationError()
    {
        // Arrange - create user WITHOUT email verification
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("unverified"))
            .WithPassword(password)
            .WithEmailVerified(false) // Email not verified
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should show verification required message
        var errorMessage = await _loginPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null, "Should display error message");
        Assert.That(errorMessage, Does.Contain("verify").IgnoreCase,
            "Error message should mention email verification");
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithLockedAccount_ShowsLockedMessage()
    {
        // Arrange - create a locked user
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("locked"))
            .WithPassword(password)
            .WithEmailVerified()
            .LockedFor(TimeSpan.FromMinutes(30))
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should show locked account message
        var errorMessage = await _loginPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null, "Should display error message");
        Assert.That(errorMessage, Does.Contain("locked").IgnoreCase,
            "Error message should mention account being locked");
        await AssertUrlAsync("/login");
    }

    [Test]
    public async Task Login_WithDisabledAccount_ShowsDisabledMessage()
    {
        // Arrange - create a disabled user
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("disabled"))
            .WithPassword(password)
            .WithEmailVerified()
            .WithStatus(Telegram.Models.UserStatus.Disabled)
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should show disabled account message
        var errorMessage = await _loginPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null, "Should display error message");
        Assert.That(errorMessage, Does.Contain("disabled").IgnoreCase,
            "Error message should mention account being disabled");
        await AssertUrlAsync("/login");
    }
}
