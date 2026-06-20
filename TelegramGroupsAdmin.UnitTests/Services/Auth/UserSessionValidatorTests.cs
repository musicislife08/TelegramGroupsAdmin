using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

[TestFixture]
public class UserSessionValidatorTests
{
    private IUserRepository _userRepository = null!;
    private UserSessionValidator _validator = null!;

    private const string UserId = "user-1";
    private const string Stamp = "stamp-1";

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _validator = new UserSessionValidator(_userRepository, NullLogger<UserSessionValidator>.Instance);
    }

    private static ClaimsPrincipal Principal(string? userId, string? stamp)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (stamp is not null) claims.Add(new Claim(CustomClaimTypes.SecurityStamp, stamp));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static UserRecord UserWith(UserStatus status, string stamp) => new(
        WebUser: new WebUserIdentity(UserId, "u@example.com", PermissionLevel.Admin),
        NormalizedEmail: "U@EXAMPLE.COM",
        PasswordHash: "x",
        SecurityStamp: stamp,
        InvitedBy: null,
        IsActive: status == UserStatus.Active,
        TotpSecret: null,
        TotpEnabled: false,
        TotpSetupStartedAt: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        LastLoginAt: null,
        Status: status,
        ModifiedBy: null,
        ModifiedAt: null,
        EmailVerified: true,
        EmailVerificationToken: null,
        EmailVerificationTokenExpiresAt: null,
        PasswordResetToken: null,
        PasswordResetTokenExpiresAt: null,
        FailedLoginAttempts: 0,
        LockedUntil: null);

    [Test]
    public async Task ValidUser_ReturnsTrue()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(UserStatus.Active, Stamp));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.True);
    }

    [Test]
    public async Task MissingUserIdClaim_ReturnsFalse()
        => Assert.That(await _validator.IsStillValidAsync(Principal(null, Stamp)), Is.False);

    [Test]
    public async Task MissingStampClaim_ReturnsFalse()
        => Assert.That(await _validator.IsStillValidAsync(Principal(UserId, null)), Is.False);

    [Test]
    public async Task UserNotFound_ReturnsFalse()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [TestCase(UserStatus.Disabled)]
    [TestCase(UserStatus.Deleted)]
    [TestCase(UserStatus.Pending)]
    public async Task NonActiveStatus_ReturnsFalse(UserStatus status)
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(status, Stamp));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [Test]
    public async Task StampMismatch_ReturnsFalse()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(UserStatus.Active, "different-stamp"));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [Test]
    public async Task RepositoryThrows_FailsClosed()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns<UserRecord?>(_ => throw new InvalidOperationException("db down"));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }
}
