using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// Tests for TOTP setup flow during first login in the WASM UI.
/// When a user has TotpEnabled=true but no secret configured,
/// they are redirected to /login/setup-2fa after password login.
/// Uses WasmSharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmTotpSetupTests : WasmSharedE2ETestBase
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
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-totp-setup"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup() // TotpEnabled=true, no secret
            .AsOwner()
            .BuildAsync();

        // Act - login with password
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);

        // Assert - should redirect to TOTP setup page
        await _setupPage.WaitForPageAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/setup-2fa"));
    }

    [Test]
    public async Task TotpSetup_DisplaysQrCode()
    {
        // Arrange - create user requiring TOTP setup
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-qr-display"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _setupPage.WaitForPageAsync();

        // Assert - QR code should be visible
        Assert.That(await _setupPage.IsQrCodeVisibleAsync(), Is.True,
            "QR code should be displayed on setup page");

        // Verify QR code has a valid data URL (WASM uses GIF for efficiency)
        var qrCodeSrc = await _setupPage.GetQrCodeSrcAsync();
        Assert.That(qrCodeSrc, Does.StartWith("data:image/gif;base64,"),
            "QR code should be a base64-encoded GIF image");
    }

    [Test]
    public async Task TotpSetup_DisplaysManualEntryKey()
    {
        // Arrange - create user requiring TOTP setup
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-manual-key"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
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
    public async Task TotpSetup_WithValidCode_CompletesAndRedirects()
    {
        // Arrange - create user requiring TOTP setup
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-valid-setup"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _setupPage.WaitForPageAsync();

        // Get the manual key and generate a valid TOTP code
        var manualKey = await _setupPage.GetManualKeyAsync();
        Assert.That(manualKey, Is.Not.Null, "Manual key should be available");

        // Remove spaces from manual key for TOTP generation
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        // Act - verify with valid code
        await _setupPage.VerifyAsync(totpCode);

        // Assert - should redirect to home (no longer on setup page)
        await _setupPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task TotpSetup_WithInvalidCode_ShowsError()
    {
        // Arrange - create user requiring TOTP setup
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-invalid-setup"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _setupPage.WaitForPageAsync();

        // Act - verify with invalid code (all zeros)
        await _setupPage.VerifyAsync("000000");

        // Assert - should show error and stay on setup page
        // Use Expect() with auto-retry for WASM async UI updates
        await Expect(Page.Locator(".alert-error")).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/setup-2fa"));
    }

    [Test]
    public async Task TotpSetup_PageTitle_ShowsCorrectHeading()
    {
        // Arrange - create user requiring TOTP setup
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-page-title"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
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
        var password = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-steps-visible"))
            .WithPassword(password)
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Act - navigate through login to setup page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, password);
        await _setupPage.WaitForPageAsync();

        // Assert - all setup steps should be visible
        Assert.That(await _setupPage.AreSetupStepsVisibleAsync(), Is.True,
            "Setup steps should be visible once page loads");
    }
}
