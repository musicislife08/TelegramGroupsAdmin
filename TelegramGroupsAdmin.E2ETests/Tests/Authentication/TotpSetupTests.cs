using System.Text.RegularExpressions;
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
        await Expect(_setupPage.QrCode).ToBeVisibleAsync();

        // Verify QR code has a valid data URL (base64-encoded PNG image)
        await Expect(_setupPage.QrCode).ToHaveAttributeAsync("src", new Regex("^data:image/png;base64,"));
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
        await Expect(_setupPage.ManualKey).ToBeVisibleAsync();

        // Verify manual key is non-empty and has expected format (Base32 with spaces)
        await Expect(_setupPage.ManualKey).ToHaveTextAsync(new Regex(@"^[A-Z2-7\s]+$"));
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
        await Expect(_setupPage.RecoveryCodesSection).ToBeVisibleAsync();

        // Should have recovery codes per AuthenticationConstants.RecoveryCodeCount
        await Expect(_setupPage.RecoveryCodes).ToHaveCountAsync(AuthenticationConstants.RecoveryCodeCount);

        // Each code should be a valid format (hex string per AuthenticationConstants.RecoveryCodeStringLength).
        // Surrounding whitespace is allowed because regex matching sees the raw (untrimmed) text.
        var expectedPattern = new Regex($@"^\s*[a-f0-9]{{{AuthenticationConstants.RecoveryCodeStringLength}}}\s*$");
        await Expect(_setupPage.RecoveryCodes).ToHaveTextAsync(
            Enumerable.Repeat(expectedPattern, AuthenticationConstants.RecoveryCodeCount));
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
        await Expect(_setupPage.ConfirmCheckbox).ToBeVisibleAsync();
        await Expect(_setupPage.ConfirmCheckbox).Not.ToBeCheckedAsync();

        // Act - submit without confirming. The confirm form is a static SSR post with a
        // [Range(true, true)] on Confirmed, so the server re-renders the page with a validation
        // message instead of signing the user in.
        await _setupPage.ClickCompleteSetupAsync();

        // Assert - still on the setup page, validation message shown, form still available
        await Expect(_setupPage.ConfirmValidationMessage)
            .ToHaveTextAsync("You must confirm you have saved your recovery codes");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/login/setup-2fa"));
        await Expect(_setupPage.ConfirmCheckbox).Not.ToBeCheckedAsync();
        await Expect(_setupPage.CompleteSetupButtonLocator).ToBeVisibleAsync();
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
        await Expect(_setupPage.ErrorAlert).ToBeVisibleAsync();
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
        await Expect(_setupPage.PageTitle).ToContainTextAsync("Two-Factor Authentication", new() { IgnoreCase = true });
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
        await Expect(_setupPage.SetupSteps).ToBeVisibleAsync();
    }
}
