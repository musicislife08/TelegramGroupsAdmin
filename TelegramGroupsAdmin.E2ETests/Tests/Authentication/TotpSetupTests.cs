using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for TOTP setup flow during first login.
/// When a user has TotpEnabled=true but no secret configured,
/// they are redirected to /login/setup-2fa after password login.
/// Uses SharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class TotpSetupTests : SharedE2ETestBase
{
    private LoginPage _loginPage = null!;
    private TotpSetupPage _setupPage = null!;

    [SetUp]
    public void SetUp()
    {
        _loginPage = new LoginPage(Page);
        _setupPage = new TotpSetupPage(Page);
    }

    [Test]
    public async Task Login_WithTotpSetupRequired_RedirectsToSetupPage()
    {
        // Arrange - create user requiring TOTP setup (enabled but no secret)
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("totp-setup"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup() // TotpEnabled=true, no secret
            .AsOwner()
            .BuildAsync();

        // Act - login with password
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should redirect to TOTP setup page
        await _setupPage.WaitForPageAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/setup-2fa"));
    }

    [Test]
    public async Task TotpSetup_DisplaysQrCode()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("qr-display"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Assert - QR code should be visible
        Assert.That(await _setupPage.IsQrCodeVisibleAsync(), Is.True,
            "QR code should be displayed on setup page");

        // Verify QR code has a valid data URL
        var qrCodeSrc = await _setupPage.GetQrCodeSrcAsync();
        Assert.That(qrCodeSrc, Does.StartWith("data:image/png;base64,"),
            "QR code should be a base64-encoded PNG image");
    }

    [Test]
    public async Task TotpSetup_DisplaysManualEntryKey()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("manual-key"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Assert - manual key should be visible
        Assert.That(await _setupPage.IsManualKeyVisibleAsync(), Is.True,
            "Manual entry key should be displayed for users who can't scan QR");

        // Verify manual key has expected format (Base32 with spaces)
        var manualKey = await _setupPage.GetManualKeyAsync();
        Assert.That(manualKey, Is.Not.Null.And.Not.Empty,
            "Manual entry key should have content");
        Assert.That(manualKey, Does.Match(@"^[A-Z2-7\s]+$"),
            "Manual key should be Base32 format with possible spaces");
    }

    [Test]
    public async Task TotpSetup_WithValidCode_ShowsRecoveryCodes()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("valid-setup"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Get the manual key and generate a valid TOTP code
        var manualKey = await _setupPage.GetManualKeyAsync();
        Assert.That(manualKey, Is.Not.Null, "Manual key should be available");

        // Remove spaces from manual key for TOTP generation
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        // Act - verify with valid code
        await _setupPage.VerifyAsync(totpCode);

        // Assert - should show recovery codes section
        await _setupPage.WaitForRecoveryCodesAsync();
        Assert.That(await _setupPage.IsRecoveryCodesSectionVisibleAsync(), Is.True,
            "Recovery codes section should be visible after TOTP verification");

        // Should have recovery codes per AuthenticationConstants.RecoveryCodeCount
        var codes = await _setupPage.GetRecoveryCodesAsync();
        Assert.That(codes.Count, Is.EqualTo(AuthenticationConstants.RecoveryCodeCount),
            $"Should display {AuthenticationConstants.RecoveryCodeCount} recovery codes");

        // Each code should be a valid format (hex string per AuthenticationConstants.RecoveryCodeStringLength)
        var expectedPattern = $@"^[a-f0-9]{{{AuthenticationConstants.RecoveryCodeStringLength}}}$";
        foreach (var code in codes)
        {
            Assert.That(code, Does.Match(expectedPattern),
                $"Recovery code '{code}' should be a {AuthenticationConstants.RecoveryCodeStringLength}-character hex string");
        }
    }

    [Test]
    public async Task TotpSetup_RecoveryCodesConfirmation_RequiredToComplete()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("confirm-required"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Get the manual key and generate a valid TOTP code
        var manualKey = await _setupPage.GetManualKeyAsync();
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        // Verify with valid code to get to recovery codes
        await _setupPage.VerifyAsync(totpCode);
        await _setupPage.WaitForRecoveryCodesAsync();

        // Assert - confirmation checkbox should be visible and unchecked by default
        Assert.That(await _setupPage.IsConfirmCheckboxVisibleAsync(), Is.True,
            "Confirmation checkbox should be visible");
        Assert.That(await _setupPage.IsConfirmationCheckedAsync(), Is.False,
            "Confirmation checkbox should be unchecked by default");
    }

    [Test]
    public async Task TotpSetup_CompleteFlow_RedirectsToHomeAfterConfirmation()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("complete-flow"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Get the manual key and generate a valid TOTP code
        var manualKey = await _setupPage.GetManualKeyAsync();
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        // Act - complete full setup flow including recovery codes
        var recoveryCodes = await _setupPage.Complete2FASetupAsync(totpCode);

        // Assert - should have received recovery codes
        Assert.That(recoveryCodes.Count, Is.EqualTo(AuthenticationConstants.RecoveryCodeCount),
            $"Should have received {AuthenticationConstants.RecoveryCodeCount} recovery codes");

        // Should redirect to home (no longer on setup page)
        await _setupPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task TotpSetup_WithInvalidCode_ShowsError()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("invalid-setup"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Act - verify with invalid code (all zeros)
        await _setupPage.VerifyAsync("000000");

        // Assert - should show error and stay on setup page
        Assert.That(await _setupPage.HasErrorMessageAsync(), Is.True,
            "Should display error for invalid TOTP code");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/setup-2fa"));
    }

    [Test]
    public async Task TotpSetup_PageTitle_ShowsCorrectHeading()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("page-title"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Assert - page should have correct title
        var title = await _setupPage.GetPageTitleAsync();
        Assert.That(title, Does.Contain("Two-Factor Authentication").IgnoreCase,
            "Page title should indicate 2FA setup");
    }

    [Test]
    public async Task TotpSetup_SetupStepsVisible()
    {
        // Arrange - create user requiring TOTP setup
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("steps-visible"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _setupPage.WaitForPageAsync();

        // Assert - all setup steps should be visible
        Assert.That(await _setupPage.AreSetupStepsVisibleAsync(), Is.True,
            "Setup steps should be visible once page loads");
    }
}
