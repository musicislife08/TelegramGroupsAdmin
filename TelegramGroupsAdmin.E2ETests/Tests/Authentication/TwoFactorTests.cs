using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for two-factor authentication (TOTP) login flow.
/// </summary>
[TestFixture]
public class TwoFactorTests : E2ETestBase
{
    private LoginPage _loginPage = null!;
    private LoginVerifyPage _verifyPage = null!;

    [SetUp]
    public void SetUp()
    {
        _loginPage = new LoginPage(Page);
        _verifyPage = new LoginVerifyPage(Page);
    }

    [Test]
    public async Task Login_WithTotpEnabled_RequiresSecondFactor()
    {
        // Arrange - create user with TOTP enabled
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("totp"))
            .WithPassword(password)
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        // Act - login with password
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should redirect to TOTP verification page
        await _verifyPage.WaitForPageAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
    }

    [Test]
    public async Task Login_WithValidTotpCode_RedirectsToHome()
    {
        // Arrange - create user with TOTP enabled
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("totp-valid"))
            .WithPassword(password)
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        // Act - complete full 2FA login flow
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _verifyPage.WaitForPageAsync();

        // Generate valid TOTP code using the same secret
        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);

        // Assert - should redirect to home after successful 2FA
        await _verifyPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task Login_WithInvalidTotpCode_ShowsError()
    {
        // Arrange - create user with TOTP enabled
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("totp-invalid"))
            .WithPassword(password)
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        // Act - login and submit wrong TOTP code
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _verifyPage.WaitForPageAsync();

        // Submit an invalid code (all zeros)
        await _verifyPage.VerifyAsync("000000");

        // Assert - should show error and stay on verify page
        Assert.That(await _verifyPage.HasErrorMessageAsync(), Is.True,
            "Should display error for invalid TOTP code");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
    }

    [Test]
    public async Task Login_WithoutTotp_SkipsVerification()
    {
        // Arrange - create user WITHOUT TOTP (regular login)
        var password = TestCredentials.GeneratePassword();
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("no-totp"))
            .WithPassword(password)
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Act - login with password only
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should go directly to home (no 2FA prompt)
        await _loginPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }
}
