using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

/// <summary>
/// Unit tests for AuthCookieService.
/// Tests cookie generation, claims building, and sign-in/sign-out operations.
/// </summary>
[TestFixture]
public class AuthCookieServiceTests
{
    private IOptionsMonitor<CookieAuthenticationOptions> _mockOptionsMonitor = null!;
    private CookieAuthenticationOptions _cookieOptions = null!;
    private ISecureDataFormat<AuthenticationTicket> _mockTicketDataFormat = null!;
    private AuthCookieService _service = null!;

    private const string TestUserId = "test-user-id-123";
    private const string TestEmail = "test@example.com";

    private static WebUserIdentity TestIdentity(PermissionLevel level = PermissionLevel.Admin) =>
        new(TestUserId, TestEmail, level);

    [SetUp]
    public void SetUp()
    {
        _mockTicketDataFormat = Substitute.For<ISecureDataFormat<AuthenticationTicket>>();
        _cookieOptions = new CookieAuthenticationOptions
        {
            TicketDataFormat = _mockTicketDataFormat
        };

        _mockOptionsMonitor = Substitute.For<IOptionsMonitor<CookieAuthenticationOptions>>();
        _mockOptionsMonitor.Get(CookieAuthenticationDefaults.AuthenticationScheme).Returns(_cookieOptions);

        _service = new AuthCookieService(_mockOptionsMonitor);
    }

    #region CookieName Property Tests

    [Test]
    public void CookieName_ReturnsTgSpamAuth()
    {
        // Assert
        Assert.That(_service.CookieName, Is.EqualTo("TgSpam.Auth"));
    }

    #endregion

    #region SignInAsync Tests

    [Test]
    public async Task SignInAsync_CallsContextSignInWithCorrectScheme()
    {
        // Arrange
        var mockAuthService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity());

        // Assert
        await mockAuthService.Received(1).SignInAsync(
            httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<AuthenticationProperties>());
    }

    [Test]
    public async Task SignInAsync_IncludesNameIdentifierClaim()
    {
        // Arrange
        ClaimsPrincipal? capturedPrincipal = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>()).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity());

        // Assert
        Assert.That(capturedPrincipal, Is.Not.Null);
        var nameIdClaim = capturedPrincipal!.FindFirst(ClaimTypes.NameIdentifier);
        Assert.That(nameIdClaim, Is.Not.Null);
        Assert.That(nameIdClaim!.Value, Is.EqualTo(TestUserId));
    }

    [Test]
    public async Task SignInAsync_IncludesEmailClaim()
    {
        // Arrange
        ClaimsPrincipal? capturedPrincipal = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>()).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity());

        // Assert
        Assert.That(capturedPrincipal, Is.Not.Null);
        var emailClaim = capturedPrincipal!.FindFirst(ClaimTypes.Email);
        Assert.That(emailClaim, Is.Not.Null);
        Assert.That(emailClaim!.Value, Is.EqualTo(TestEmail));
    }

    [Test]
    public async Task SignInAsync_IncludesPermissionLevelClaim()
    {
        // Arrange
        ClaimsPrincipal? capturedPrincipal = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>()).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity(PermissionLevel.GlobalAdmin));

        // Assert
        Assert.That(capturedPrincipal, Is.Not.Null);
        var permissionClaim = capturedPrincipal!.FindFirst(CustomClaimTypes.PermissionLevel);
        Assert.That(permissionClaim, Is.Not.Null);
        Assert.That(permissionClaim!.Value, Is.EqualTo("1")); // GlobalAdmin = 1
    }

    [TestCase(PermissionLevel.Admin, "Admin")]
    [TestCase(PermissionLevel.GlobalAdmin, "GlobalAdmin")]
    [TestCase(PermissionLevel.Owner, "Owner")]
    public async Task SignInAsync_SetsCorrectRoleForPermissionLevel(PermissionLevel level, string expectedRole)
    {
        // Arrange
        ClaimsPrincipal? capturedPrincipal = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>()).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity(level));

        // Assert
        Assert.That(capturedPrincipal, Is.Not.Null);
        var roleClaim = capturedPrincipal!.FindFirst(ClaimTypes.Role);
        Assert.That(roleClaim, Is.Not.Null);
        Assert.That(roleClaim!.Value, Is.EqualTo(expectedRole));
    }

    [Test]
    public async Task SignInAsync_SetsIsPersistentTrue()
    {
        // Arrange
        AuthenticationProperties? capturedProps = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Any<ClaimsPrincipal>(),
            Arg.Do<AuthenticationProperties>(p => capturedProps = p)).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        // Act
        await _service.SignInAsync(httpContext, TestIdentity());

        // Assert
        Assert.That(capturedProps, Is.Not.Null);
        Assert.That(capturedProps!.IsPersistent, Is.True);
    }

    [Test]
    public async Task SignInAsync_SetsExpiresUtc()
    {
        // Arrange
        AuthenticationProperties? capturedProps = null;
        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Any<ClaimsPrincipal>(),
            Arg.Do<AuthenticationProperties>(p => capturedProps = p)).Returns(Task.CompletedTask);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var beforeCall = DateTimeOffset.UtcNow;

        // Act
        await _service.SignInAsync(httpContext, TestIdentity());

        var afterCall = DateTimeOffset.UtcNow;

        // Assert
        Assert.That(capturedProps, Is.Not.Null);
        Assert.That(capturedProps!.ExpiresUtc, Is.Not.Null);

        // Verify expiration is approximately 30 days from now (AuthenticationConstants.CookieExpiration)
        var expectedExpiration = beforeCall.AddDays(30);
        var actualExpiration = capturedProps.ExpiresUtc!.Value;

        // Allow 1 second tolerance for test execution time
        Assert.That(actualExpiration, Is.GreaterThanOrEqualTo(beforeCall.AddDays(30).AddSeconds(-1)));
        Assert.That(actualExpiration, Is.LessThanOrEqualTo(afterCall.AddDays(30).AddSeconds(1)));
    }

    #endregion

    #region SignOutAsync Tests

    [Test]
    public async Task SignOutAsync_CallsContextSignOutWithCorrectScheme()
    {
        // Arrange
        var mockAuthService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(mockAuthService);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        // Act
        await _service.SignOutAsync(httpContext);

        // Assert
        await mockAuthService.Received(1).SignOutAsync(
            httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<AuthenticationProperties>());
    }

    #endregion

    #region GenerateCookieValue Tests

    [Test]
    public void GenerateCookieValue_CallsTicketDataFormatProtect()
    {
        // Arrange
        _mockTicketDataFormat.Protect(Arg.Any<AuthenticationTicket>()).Returns("encrypted-cookie-value");

        // Act
        var result = _service.GenerateCookieValue(TestIdentity());

        // Assert
        _mockTicketDataFormat.Received(1).Protect(Arg.Any<AuthenticationTicket>());
    }

    [Test]
    public void GenerateCookieValue_ReturnsEncryptedValue()
    {
        // Arrange
        const string expectedEncryptedValue = "encrypted-auth-ticket-abc123";
        _mockTicketDataFormat.Protect(Arg.Any<AuthenticationTicket>()).Returns(expectedEncryptedValue);

        // Act
        var result = _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(result, Is.EqualTo(expectedEncryptedValue));
    }

    [Test]
    public void GenerateCookieValue_IncludesCorrectUserId()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var nameIdClaim = capturedTicket!.Principal.FindFirst(ClaimTypes.NameIdentifier);
        Assert.That(nameIdClaim, Is.Not.Null);
        Assert.That(nameIdClaim!.Value, Is.EqualTo(TestUserId));
    }

    [Test]
    public void GenerateCookieValue_IncludesCorrectEmail()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var emailClaim = capturedTicket!.Principal.FindFirst(ClaimTypes.Email);
        Assert.That(emailClaim, Is.Not.Null);
        Assert.That(emailClaim!.Value, Is.EqualTo(TestEmail));
    }

    [TestCase(PermissionLevel.Admin, "Admin")]
    [TestCase(PermissionLevel.GlobalAdmin, "GlobalAdmin")]
    [TestCase(PermissionLevel.Owner, "Owner")]
    public void GenerateCookieValue_IncludesCorrectRole(PermissionLevel level, string expectedRole)
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity(level));

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var roleClaim = capturedTicket!.Principal.FindFirst(ClaimTypes.Role);
        Assert.That(roleClaim, Is.Not.Null);
        Assert.That(roleClaim!.Value, Is.EqualTo(expectedRole));
    }

    [TestCase(PermissionLevel.Admin, "0")]
    [TestCase(PermissionLevel.GlobalAdmin, "1")]
    [TestCase(PermissionLevel.Owner, "2")]
    public void GenerateCookieValue_IncludesCorrectPermissionLevel(PermissionLevel level, string expectedValue)
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity(level));

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var permissionClaim = capturedTicket!.Principal.FindFirst(CustomClaimTypes.PermissionLevel);
        Assert.That(permissionClaim, Is.Not.Null);
        Assert.That(permissionClaim!.Value, Is.EqualTo(expectedValue));
    }

    [Test]
    public void GenerateCookieValue_UsesCorrectAuthenticationScheme()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        Assert.That(capturedTicket!.AuthenticationScheme, Is.EqualTo(CookieAuthenticationDefaults.AuthenticationScheme));
    }

    [Test]
    public void GenerateCookieValue_SetsIsPersistent()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        Assert.That(capturedTicket!.Properties.IsPersistent, Is.True);
    }

    [Test]
    public void GenerateCookieValue_SetsIssuedUtc()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        var beforeCall = DateTimeOffset.UtcNow;

        // Act
        _service.GenerateCookieValue(TestIdentity());

        var afterCall = DateTimeOffset.UtcNow;

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedTicket!.Properties.IssuedUtc, Is.Not.Null);
            Assert.That(capturedTicket.Properties.IssuedUtc!.Value, Is.GreaterThanOrEqualTo(beforeCall.AddSeconds(-1)));
        }
        Assert.That(capturedTicket.Properties.IssuedUtc!.Value, Is.LessThanOrEqualTo(afterCall.AddSeconds(1)));
    }

    [Test]
    public void GenerateCookieValue_SetsExpiresUtc()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        var beforeCall = DateTimeOffset.UtcNow;

        // Act
        _service.GenerateCookieValue(TestIdentity());

        var afterCall = DateTimeOffset.UtcNow;

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedTicket!.Properties.ExpiresUtc, Is.Not.Null);

            // Verify expiration is approximately 30 days from now
            Assert.That(capturedTicket.Properties.ExpiresUtc!.Value, Is.GreaterThanOrEqualTo(beforeCall.AddDays(30).AddSeconds(-1)));
        }
        Assert.That(capturedTicket.Properties.ExpiresUtc!.Value, Is.LessThanOrEqualTo(afterCall.AddDays(30).AddSeconds(1)));
    }

    [Test]
    public void GenerateCookieValue_IdentityHasCorrectAuthenticationType()
    {
        // Arrange
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(TestIdentity());

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedTicket!.Principal.Identity, Is.Not.Null);
            Assert.That(capturedTicket.Principal.Identity!.AuthenticationType, Is.EqualTo(CookieAuthenticationDefaults.AuthenticationScheme));
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public void GenerateCookieValue_WithSpecialCharactersInEmail_HandlesCorrectly()
    {
        // Arrange
        const string emailWithSpecialChars = "user+tag@sub.example.com";
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(new WebUserIdentity(TestUserId, emailWithSpecialChars, PermissionLevel.Admin));

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var emailClaim = capturedTicket!.Principal.FindFirst(ClaimTypes.Email);
        Assert.That(emailClaim!.Value, Is.EqualTo(emailWithSpecialChars));
    }

    [Test]
    public void GenerateCookieValue_WithGuidUserId_HandlesCorrectly()
    {
        // Arrange
        var guidUserId = Guid.NewGuid().ToString();
        AuthenticationTicket? capturedTicket = null;
        _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => capturedTicket = t)).Returns("cookie");

        // Act
        _service.GenerateCookieValue(new WebUserIdentity(guidUserId, TestEmail, PermissionLevel.Owner));

        // Assert
        Assert.That(capturedTicket, Is.Not.Null);
        var nameIdClaim = capturedTicket!.Principal.FindFirst(ClaimTypes.NameIdentifier);
        Assert.That(nameIdClaim!.Value, Is.EqualTo(guidUserId));
    }

    #endregion
}
