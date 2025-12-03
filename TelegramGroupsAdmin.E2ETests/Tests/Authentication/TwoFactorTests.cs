using OtpNet;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;

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
        Assert.That(Page.Url, Does.Contain("/login/verify"),
            "Should redirect to 2FA verification page after password login");
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
        var totpCode = GenerateTotpCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);

        // Assert - should redirect to home after successful 2FA
        await _verifyPage.WaitForRedirectAsync();
        Assert.That(Page.Url, Does.Not.Contain("/login"),
            "Should not remain on login pages after successful 2FA");
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
        Assert.That(Page.Url, Does.Contain("/login/verify"),
            "Should remain on verification page after invalid code");
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
        Assert.That(Page.Url, Does.Not.Contain("/login/verify"),
            "Users without TOTP should skip verification page");
        Assert.That(Page.Url, Does.Not.Contain("/login"),
            "Should redirect to home after login");
    }

    /// <summary>
    /// Generates a valid TOTP code for the given Base32-encoded secret.
    /// Uses Otp.NET library with standard settings (6 digits, 30-second window).
    /// </summary>
    private static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp();
    }
}
