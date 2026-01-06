using TelegramGroupsAdmin.Helpers;

namespace TelegramGroupsAdmin.UnitTests.Helpers;

/// <summary>
/// Unit tests for UrlHelpers.
/// Tests URL building and validation for authentication flows.
/// </summary>
[TestFixture]
public class UrlHelpersTests
{
    private const string TestUserId = "abc-123-def";
    private const string TestToken = "base64+token/with=special";
    private const string TestReturnUrl = "/dashboard?tab=settings";

    #region IsLocalUrl Tests

    [Test]
    public void IsLocalUrl_ValidLocalPath_ReturnsTrue()
    {
        Assert.That(UrlHelpers.IsLocalUrl("/dashboard"), Is.True);
        Assert.That(UrlHelpers.IsLocalUrl("/login"), Is.True);
        Assert.That(UrlHelpers.IsLocalUrl("/"), Is.True);
        Assert.That(UrlHelpers.IsLocalUrl("/path/to/page"), Is.True);
        Assert.That(UrlHelpers.IsLocalUrl("/path?query=value"), Is.True);
    }

    [Test]
    public void IsLocalUrl_HttpUrl_ReturnsFalse()
    {
        Assert.That(UrlHelpers.IsLocalUrl("http://evil.com"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("http://localhost/page"), Is.False);
    }

    [Test]
    public void IsLocalUrl_HttpsUrl_ReturnsFalse()
    {
        Assert.That(UrlHelpers.IsLocalUrl("https://evil.com"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("https://localhost/page"), Is.False);
    }

    [Test]
    public void IsLocalUrl_ProtocolRelativeUrl_ReturnsFalse()
    {
        Assert.That(UrlHelpers.IsLocalUrl("//evil.com"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("//evil.com/path"), Is.False);
    }

    [Test]
    public void IsLocalUrl_NullOrEmpty_ReturnsFalse()
    {
        Assert.That(UrlHelpers.IsLocalUrl(null), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl(""), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("   "), Is.False);
    }

    [Test]
    public void IsLocalUrl_RelativeWithoutSlash_ReturnsFalse()
    {
        Assert.That(UrlHelpers.IsLocalUrl("dashboard"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("path/to/page"), Is.False);
    }

    [Test]
    public void IsLocalUrl_JavascriptProtocol_ReturnsFalse()
    {
        // Security: XSS protection - javascript: URLs must be blocked
        Assert.That(UrlHelpers.IsLocalUrl("javascript:alert(1)"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("javascript:void(0)"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("JAVASCRIPT:alert(1)"), Is.False); // Case insensitive
    }

    [Test]
    public void IsLocalUrl_DataProtocol_ReturnsFalse()
    {
        // Security: XSS protection - data: URLs must be blocked
        Assert.That(UrlHelpers.IsLocalUrl("data:text/html,<script>alert(1)</script>"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg=="), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("DATA:text/html,test"), Is.False); // Case insensitive
    }

    [Test]
    public void IsLocalUrl_VbscriptProtocol_ReturnsFalse()
    {
        // Security: XSS protection - vbscript: URLs must be blocked (IE legacy)
        Assert.That(UrlHelpers.IsLocalUrl("vbscript:msgbox(1)"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("VBSCRIPT:msgbox(1)"), Is.False); // Case insensitive
    }

    [Test]
    public void IsLocalUrl_FileProtocol_ReturnsFalse()
    {
        // Security: Block file: protocol to prevent local file access
        Assert.That(UrlHelpers.IsLocalUrl("file:///etc/passwd"), Is.False);
        Assert.That(UrlHelpers.IsLocalUrl("FILE:///C:/Windows/System32"), Is.False); // Case insensitive
    }

    #endregion

    #region GetSafeRedirectUrl Tests

    [Test]
    public void GetSafeRedirectUrl_ValidLocalUrl_ReturnsUrl()
    {
        var result = UrlHelpers.GetSafeRedirectUrl("/dashboard");
        Assert.That(result, Is.EqualTo("/dashboard"));
    }

    [Test]
    public void GetSafeRedirectUrl_InvalidUrl_ReturnsDefault()
    {
        var result = UrlHelpers.GetSafeRedirectUrl("https://evil.com");
        Assert.That(result, Is.EqualTo("/"));
    }

    [Test]
    public void GetSafeRedirectUrl_InvalidUrl_ReturnsCustomDefault()
    {
        var result = UrlHelpers.GetSafeRedirectUrl("https://evil.com", "/home");
        Assert.That(result, Is.EqualTo("/home"));
    }

    [Test]
    public void GetSafeRedirectUrl_NullUrl_ReturnsDefault()
    {
        var result = UrlHelpers.GetSafeRedirectUrl(null);
        Assert.That(result, Is.EqualTo("/"));
    }

    #endregion

    #region BuildVerifyUrl Tests

    [Test]
    public void BuildVerifyUrl_BasicParams_BuildsCorrectUrl()
    {
        var result = UrlHelpers.BuildVerifyUrl(TestUserId, TestToken);

        Assert.That(result, Does.StartWith("/login/verify?"));
        Assert.That(result, Does.Contain($"userId={TestUserId}"));
        Assert.That(result, Does.Contain("token="));
    }

    [Test]
    public void BuildVerifyUrl_WithReturnUrl_IncludesReturnUrl()
    {
        var result = UrlHelpers.BuildVerifyUrl(TestUserId, TestToken, TestReturnUrl);

        Assert.That(result, Does.Contain("returnUrl="));
        // Return URL should be escaped
        Assert.That(result, Does.Contain("%2Fdashboard"));
    }

    [Test]
    public void BuildVerifyUrl_WithUseRecovery_IncludesFlag()
    {
        var result = UrlHelpers.BuildVerifyUrl(TestUserId, TestToken, useRecovery: true);

        Assert.That(result, Does.Contain("useRecovery=true"));
    }

    [Test]
    public void BuildVerifyUrl_WithoutUseRecovery_OmitsFlag()
    {
        var result = UrlHelpers.BuildVerifyUrl(TestUserId, TestToken, useRecovery: false);

        Assert.That(result, Does.Not.Contain("useRecovery"));
    }

    [Test]
    public void BuildVerifyUrl_EscapesSpecialCharactersInToken()
    {
        var tokenWithSpecialChars = "abc+def/ghi=jkl";
        var result = UrlHelpers.BuildVerifyUrl(TestUserId, tokenWithSpecialChars);

        // + should be encoded as %2B, / as %2F, = as %3D
        Assert.That(result, Does.Not.Contain("token=abc+def/ghi=jkl"));
        Assert.That(result, Does.Contain("%2B")); // encoded +
        Assert.That(result, Does.Contain("%2F")); // encoded /
        Assert.That(result, Does.Contain("%3D")); // encoded =
    }

    #endregion

    #region BuildSetup2FAUrl Tests

    [Test]
    public void BuildSetup2FAUrl_BasicParams_BuildsCorrectUrl()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken);

        Assert.That(result, Does.StartWith("/login/setup-2fa?"));
        Assert.That(result, Does.Contain($"userId={TestUserId}"));
        Assert.That(result, Does.Contain("token="));
    }

    [Test]
    public void BuildSetup2FAUrl_WithReturnUrl_IncludesReturnUrl()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, TestReturnUrl);

        Assert.That(result, Does.Contain("returnUrl="));
        Assert.That(result, Does.Contain("%2Fdashboard"));
    }

    [Test]
    public void BuildSetup2FAUrl_WithStep_IncludesStep()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, step: "recovery");

        Assert.That(result, Does.Contain("step=recovery"));
    }

    [Test]
    public void BuildSetup2FAUrl_WithNullStep_OmitsStep()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, step: null);

        Assert.That(result, Does.Not.Contain("step="));
    }

    [Test]
    public void BuildSetup2FAUrl_WithEmptyStep_OmitsStep()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, step: "");

        Assert.That(result, Does.Not.Contain("step="));
    }

    [Test]
    public void BuildSetup2FAUrl_WithAllParams_IncludesAll()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, TestReturnUrl, "recovery");

        Assert.That(result, Does.StartWith("/login/setup-2fa?"));
        Assert.That(result, Does.Contain($"userId={TestUserId}"));
        Assert.That(result, Does.Contain("token="));
        Assert.That(result, Does.Contain("returnUrl="));
        Assert.That(result, Does.Contain("step=recovery"));
    }

    [Test]
    public void BuildSetup2FAUrl_EscapesStepParameter()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, step: "step with spaces");

        // Spaces should be encoded as %20
        Assert.That(result, Does.Contain("step=step%20with%20spaces"));
    }

    [Test]
    public void BuildSetup2FAUrl_EscapesSpecialCharactersInToken()
    {
        var tokenWithSpecialChars = "abc+def/ghi=jkl";
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, tokenWithSpecialChars);

        Assert.That(result, Does.Contain("%2B")); // encoded +
        Assert.That(result, Does.Contain("%2F")); // encoded /
        Assert.That(result, Does.Contain("%3D")); // encoded =
    }

    [Test]
    public void BuildSetup2FAUrl_NullToken_HandlesGracefully()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, null);

        Assert.That(result, Does.Contain("token="));
        Assert.That(result, Does.Not.Contain("token=null"));
    }

    [Test]
    public void BuildSetup2FAUrl_NullReturnUrl_HandlesGracefully()
    {
        var result = UrlHelpers.BuildSetup2FAUrl(TestUserId, TestToken, null);

        Assert.That(result, Does.Contain("returnUrl="));
        Assert.That(result, Does.Not.Contain("returnUrl=null"));
    }

    #endregion
}
