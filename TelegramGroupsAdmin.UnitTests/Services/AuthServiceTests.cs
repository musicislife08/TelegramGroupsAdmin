using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Services.Email;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for AuthService.RegisterAsync: the invite path and the first-run owner path, each driven by the
/// strict email-configuration gate (Enabled / Disabled / Indeterminate).
/// Uses NSubstitute for every collaborator - no database required.
/// </summary>
[TestFixture]
public class AuthServiceTests
{
    private const string Email = "invitee@unit.test";
    private const string Password = "Passw0rd!";
    private const string PasswordHash = "hashed";
    private const string InviteToken = "invite-token";
    private const string InviterId = "inviter-id";
    private const string NewUserId = "new-user-id";

    private IUserRepository _users = null!;
    private IVerificationTokenRepository _verificationTokens = null!;
    private IAuditService _auditLog = null!;
    private IEmailService _emailService = null!;
    private IFeatureAvailabilityService _features = null!;
    private AuthService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IUserRepository>();
        _verificationTokens = Substitute.For<IVerificationTokenRepository>();
        _auditLog = Substitute.For<IAuditService>();
        _emailService = Substitute.For<IEmailService>();
        _features = Substitute.For<IFeatureAvailabilityService>();

        var passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.HashPassword(Password).Returns(PasswordHash);

        _service = new AuthService(
            _users,
            _verificationTokens,
            _auditLog,
            Substitute.For<ITotpService>(),
            passwordHasher,
            _emailService,
            Substitute.For<IAccountLockoutService>(),
            _features,
            Options.Create(new AppOptions { BaseUrl = "https://app.unit.test" }),
            Substitute.For<ILogger<AuthService>>());

        // Invite path by default: not first run, a pending unexpired invite, no existing account for the email.
        _users.AnyUsersExistAsync(Arg.Any<CancellationToken>()).Returns(true);
        _users.GetInviteByTokenAsync(InviteToken, Arg.Any<CancellationToken>())
            .Returns(new InviteRecord(
                Token: InviteToken,
                CreatedBy: InviterId,
                CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(6),
                UsedBy: null,
                PermissionLevel: PermissionLevel.GlobalAdmin,
                Status: InviteStatus.Pending,
                ModifiedAt: null));
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);
        _users.RegisterUserWithInviteAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PermissionLevel>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(NewUserId);
    }

    private void FirstRun() => _users.AnyUsersExistAsync(Arg.Any<CancellationToken>()).Returns(false);

    private void EmailState(EmailConfigurationState state) => _features.GetEmailConfigurationStateAsync().Returns(state);

    #region Invite path - email verification gate

    [TestCase(EmailConfigurationState.Enabled, false, Description = "email service configured: account starts unverified")]
    [TestCase(EmailConfigurationState.Disabled, true, Description = "no email service: account starts verified (login would otherwise be impossible)")]
    public async Task RegisterAsync_WithInvite_EmailVerifiedFollowsTheGate(EmailConfigurationState state, bool expectedEmailVerified)
    {
        // Arrange
        EmailState(state);

        // Act
        var result = await _service.RegisterAsync(Email, Password, InviteToken);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.UserId, Is.EqualTo(NewUserId));
        }

        await _users.Received(1).RegisterUserWithInviteAsync(
            Email,
            PasswordHash,
            PermissionLevel.GlobalAdmin,
            InviterId,
            InviteToken,
            emailVerified: expectedEmailVerified,
            Arg.Any<CancellationToken>());
    }

    [TestCase(EmailConfigurationState.Enabled, 1)]
    [TestCase(EmailConfigurationState.Disabled, 0)]
    public async Task RegisterAsync_WithInvite_IssuesTheVerificationTokenAndEmailOnlyWhenEnabled(EmailConfigurationState state, int expectedSends)
    {
        // Arrange
        EmailState(state);

        // Act
        await _service.RegisterAsync(Email, Password, InviteToken);

        // Assert
        await _verificationTokens.Received(expectedSends).CreateAsync(
            Arg.Is<DataModels.VerificationTokenDto>(t =>
                t!.UserId == NewUserId && t.TokenType == DataModels.TokenType.EmailVerification),
            Arg.Any<CancellationToken>());
        await _emailService.Received(expectedSends).SendTemplatedEmailAsync(
            Email,
            EmailTemplate.EmailVerification,
            Arg.Is<Dictionary<string, string>>(p => p!.ContainsKey("VerificationToken")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_WithInvite_WhenTheGateIsIndeterminate_FailsClosedWithoutWriting()
    {
        // Arrange
        EmailState(EmailConfigurationState.Indeterminate);

        // Act
        var result = await _service.RegisterAsync(Email, Password, InviteToken);

        // Assert - a clear, retryable error; no account created or reactivated, the invite not consumed, nothing sent
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.UserId, Is.Null);
            Assert.That(result.ErrorMessage, Does.Contain("temporarily unavailable").And.Contain("try again"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(InviteToken).And.Not.Contain(Email), "the error must not leak request details");
        }

        await _users.DidNotReceive().RegisterUserWithInviteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PermissionLevel>(), Arg.Any<string?>(),
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _users.DidNotReceive().CreateAsync(Arg.Any<UserRecord>(), Arg.Any<CancellationToken>());
        await _verificationTokens.DidNotReceive().CreateAsync(Arg.Any<DataModels.VerificationTokenDto>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendTemplatedEmailAsync(
            Arg.Any<string>(), Arg.Any<EmailTemplate>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region First-run owner path - email verification gate

    [TestCase(EmailConfigurationState.Enabled, false)]
    [TestCase(EmailConfigurationState.Disabled, true)]
    public async Task RegisterAsync_FirstRun_OwnerEmailVerifiedFollowsTheGate(EmailConfigurationState state, bool expectedEmailVerified)
    {
        // Arrange
        FirstRun();
        EmailState(state);

        // Act
        var result = await _service.RegisterAsync(Email, Password, inviteToken: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.UserId, Is.Not.Null);
        }

        await _users.Received(1).CreateAsync(
            Arg.Is<UserRecord>(u =>
                u!.WebUser.Email == Email
                && u.WebUser.PermissionLevel == PermissionLevel.Owner
                && u.PasswordHash == PasswordHash
                && u.EmailVerified == expectedEmailVerified),
            Arg.Any<CancellationToken>());
        await _users.DidNotReceive().RegisterUserWithInviteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PermissionLevel>(), Arg.Any<string?>(),
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_FirstRun_WhenTheGateIsIndeterminate_FailsClosedWithoutCreatingTheOwner()
    {
        // Arrange
        FirstRun();
        EmailState(EmailConfigurationState.Indeterminate);

        // Act
        var result = await _service.RegisterAsync(Email, Password, inviteToken: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.UserId, Is.Null);
            Assert.That(result.ErrorMessage, Does.Contain("temporarily unavailable").And.Contain("try again"));
        }

        await _users.DidNotReceive().CreateAsync(Arg.Any<UserRecord>(), Arg.Any<CancellationToken>());
        await _users.DidNotReceive().RegisterUserWithInviteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PermissionLevel>(), Arg.Any<string?>(),
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
