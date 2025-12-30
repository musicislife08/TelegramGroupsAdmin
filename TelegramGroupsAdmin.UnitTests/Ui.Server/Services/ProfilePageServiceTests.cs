using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Server.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Ui.Server.Services;

/// <summary>
/// Unit tests for ProfilePageService.
/// Tests business logic for profile page operations with mocked dependencies.
/// </summary>
[TestFixture]
public class ProfilePageServiceTests
{
    private ILogger<ProfilePageService> _mockLogger = null!;
    private IUserRepository _mockUserRepo = null!;
    private ITelegramUserMappingRepository _mockMappingRepo = null!;
    private INotificationPreferencesRepository _mockPrefsRepo = null!;
    private IPushSubscriptionsRepository _mockPushRepo = null!;
    private IPasswordHasher _mockPasswordHasher = null!;
    private ITotpService _mockTotpService = null!;
    private ITelegramLinkService _mockLinkService = null!;
    private ProfilePageService _service = null!;

    private const string TestUserId = "test-user-123";
    private const string TestEmail = "test@example.com";
    private const string TestPasswordHash = "hashed-password";

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<ProfilePageService>>();
        _mockUserRepo = Substitute.For<IUserRepository>();
        _mockMappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _mockPrefsRepo = Substitute.For<INotificationPreferencesRepository>();
        _mockPushRepo = Substitute.For<IPushSubscriptionsRepository>();
        _mockPasswordHasher = Substitute.For<IPasswordHasher>();
        _mockTotpService = Substitute.For<ITotpService>();
        _mockLinkService = Substitute.For<ITelegramLinkService>();

        _service = new ProfilePageService(
            _mockLogger,
            _mockUserRepo,
            _mockMappingRepo,
            _mockPrefsRepo,
            _mockPushRepo,
            _mockPasswordHasher,
            _mockTotpService,
            _mockLinkService);
    }

    private static UserRecord CreateTestUser(
        string id = TestUserId,
        string email = TestEmail,
        string passwordHash = TestPasswordHash,
        bool totpEnabled = false,
        int permissionLevel = 0) => new(
            Id: id,
            Email: email,
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: passwordHash,
            SecurityStamp: "stamp",
            PermissionLevel: (PermissionLevel)permissionLevel,
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: totpEnabled,
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            LastLoginAt: DateTimeOffset.UtcNow.AddHours(-1),
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: true,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null);

    #region GetProfilePageDataAsync Tests

    [Test]
    public async Task GetProfilePageDataAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((UserRecord?)null);

        // Act
        var result = await _service.GetProfilePageDataAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("User not found"));
    }

    [Test]
    public async Task GetProfilePageDataAsync_Success_ReturnsUserData()
    {
        // Arrange
        var user = CreateTestUser(permissionLevel: 1);
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<TelegramUserMappingRecord>());

        // Act
        var result = await _service.GetProfilePageDataAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Email, Is.EqualTo(TestEmail));
        Assert.That(result.PermissionLevel, Is.EqualTo(1));
    }

    [Test]
    public async Task GetProfilePageDataAsync_FiltersInactiveAccounts()
    {
        // Arrange
        var user = CreateTestUser();
        var mappings = new List<TelegramUserMappingRecord>
        {
            new(Id: 1, TelegramId: 111, TelegramUsername: "active1", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: true),
            new(Id: 2, TelegramId: 222, TelegramUsername: "inactive", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: false),
            new(Id: 3, TelegramId: 333, TelegramUsername: "active2", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: true)
        };

        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(mappings);

        // Act
        var result = await _service.GetProfilePageDataAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.LinkedAccounts, Has.Count.EqualTo(2));
        Assert.That(result.LinkedAccounts.Select(a => a.TelegramUsername),
            Is.EquivalentTo(new[] { "active1", "active2" }));
    }

    #endregion

    #region ChangePasswordAsync Tests

    [Test]
    public async Task ChangePasswordAsync_EmptyCurrentPassword_ReturnsError()
    {
        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "", "newpass123", "newpass123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Please fill in all fields"));
    }

    [Test]
    public async Task ChangePasswordAsync_EmptyNewPassword_ReturnsError()
    {
        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "current", "", "");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Please fill in all fields"));
    }

    [Test]
    public async Task ChangePasswordAsync_PasswordMismatch_ReturnsError()
    {
        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "current", "newpass123", "different123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("New passwords do not match"));
    }

    [Test]
    public async Task ChangePasswordAsync_PasswordTooShort_ReturnsError()
    {
        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "current", "short", "short");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("New password must be at least 8 characters"));
    }

    [Test]
    public async Task ChangePasswordAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((UserRecord?)null);

        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "current", "newpass123", "newpass123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("User not found"));
    }

    [Test]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockPasswordHasher.VerifyPassword("wrongpassword", TestPasswordHash)
            .Returns(false);

        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "wrongpassword", "newpass123", "newpass123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Current password is incorrect"));
    }

    [Test]
    public async Task ChangePasswordAsync_Success_UpdatesPassword()
    {
        // Arrange
        var user = CreateTestUser();
        var originalSecurityStamp = user.SecurityStamp;
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockPasswordHasher.VerifyPassword("currentpass", TestPasswordHash)
            .Returns(true);
        _mockPasswordHasher.HashPassword("newpass123")
            .Returns("new-hashed-password");

        UserRecord? capturedUser = null;
        _mockUserRepo.UpdateAsync(Arg.Do<UserRecord>(u => capturedUser = u), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "currentpass", "newpass123", "newpass123");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(capturedUser, Is.Not.Null);
        Assert.That(capturedUser!.PasswordHash, Is.EqualTo("new-hashed-password"));
        Assert.That(capturedUser.ModifiedBy, Is.EqualTo(TestUserId));
        // Fix 5: Verify SecurityStamp is updated (forces re-authentication)
        Assert.That(capturedUser.SecurityStamp, Is.Not.EqualTo(originalSecurityStamp));
        Assert.That(Guid.TryParse(capturedUser.SecurityStamp, out _), Is.True);
    }

    [TestCase("1234567", false, Description = "7 chars - just below minimum")]
    [TestCase("12345678", true, Description = "8 chars - exactly minimum")]
    [TestCase("123456789", true, Description = "9 chars - above minimum")]
    public async Task ChangePasswordAsync_PasswordLengthBoundary(string newPassword, bool shouldSucceed)
    {
        // Arrange - only mock dependencies if validation should pass
        if (shouldSucceed)
        {
            var user = CreateTestUser();
            _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
                .Returns(user);
            _mockPasswordHasher.VerifyPassword("currentpass", TestPasswordHash)
                .Returns(true);
            _mockPasswordHasher.HashPassword(newPassword)
                .Returns("new-hashed-password");
            _mockUserRepo.UpdateAsync(Arg.Any<UserRecord>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        // Act
        var result = await _service.ChangePasswordAsync(TestUserId, "currentpass", newPassword, newPassword);

        // Assert
        Assert.That(result.Success, Is.EqualTo(shouldSucceed));
        if (!shouldSucceed)
        {
            Assert.That(result.Error, Is.EqualTo("New password must be at least 8 characters"));
        }
    }

    #endregion

    #region SetupTotpAsync Tests

    [Test]
    public async Task SetupTotpAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((UserRecord?)null);

        // Act
        var result = await _service.SetupTotpAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("User not found"));
    }

    [Test]
    public async Task SetupTotpAsync_TotpServiceThrows_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockTotpService.SetupTotpAsync(TestUserId, TestEmail, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("TOTP setup failed"));

        // Act
        var result = await _service.SetupTotpAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to setup two-factor authentication"));
    }

    [Test]
    public async Task SetupTotpAsync_Success_ReturnsQrCodeAndManualKey()
    {
        // Arrange
        var user = CreateTestUser();
        var totpResult = new TotpSetupResult("secret123", "otpauth://totp/test", "ABCD-EFGH-1234");

        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockTotpService.SetupTotpAsync(TestUserId, TestEmail, Arg.Any<CancellationToken>())
            .Returns(totpResult);

        // Act
        var result = await _service.SetupTotpAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.QrCodeUri, Is.EqualTo("otpauth://totp/test"));
        Assert.That(result.ManualEntryKey, Is.EqualTo("ABCD-EFGH-1234"));
    }

    [Test]
    public async Task SetupTotpAsync_WhenAlreadyEnabled_ReturnsError()
    {
        // Arrange - user already has TOTP enabled
        // Fix 8: Edge case - SetupTotpAsync should reject if TOTP already enabled
        // User must use ResetTotpWithPasswordAsync flow instead (requires password verification)
        var user = CreateTestUser(totpEnabled: true);

        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        var result = await _service.SetupTotpAsync(TestUserId);

        // Assert - setup fails when TOTP already enabled, must use Reset flow
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("already enabled"));

        // Verify TOTP service was NOT called (early exit)
        await _mockTotpService.DidNotReceive()
            .SetupTotpAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region VerifyAndEnableTotpAsync Tests

    [Test]
    public async Task VerifyAndEnableTotpAsync_EmptyCode_ReturnsError()
    {
        // Act
        var result = await _service.VerifyAndEnableTotpAsync(TestUserId, "");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Please enter the verification code"));
    }

    [Test]
    public async Task VerifyAndEnableTotpAsync_InvalidCode_ReturnsError()
    {
        // Arrange
        var verifyResult = new TotpVerificationResult(false, false, "Invalid code");
        _mockTotpService.VerifyAndEnableTotpAsync(TestUserId, "123456", Arg.Any<CancellationToken>())
            .Returns(verifyResult);

        // Act
        var result = await _service.VerifyAndEnableTotpAsync(TestUserId, "123456");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Invalid code"));
    }

    [Test]
    public async Task VerifyAndEnableTotpAsync_Success_ReturnsRecoveryCodes()
    {
        // Arrange
        var verifyResult = new TotpVerificationResult(true, false, null);
        var recoveryCodes = new List<string> { "CODE1", "CODE2", "CODE3" };

        _mockTotpService.VerifyAndEnableTotpAsync(TestUserId, "123456", Arg.Any<CancellationToken>())
            .Returns(verifyResult);
        _mockTotpService.GenerateRecoveryCodesAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(recoveryCodes);

        // Act
        var result = await _service.VerifyAndEnableTotpAsync(TestUserId, "123456");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RecoveryCodes, Has.Count.EqualTo(3));
        Assert.That(result.RecoveryCodes, Is.EquivalentTo(recoveryCodes));
    }

    #endregion

    #region ResetTotpWithPasswordAsync Tests

    [Test]
    public async Task ResetTotpWithPasswordAsync_EmptyPassword_ReturnsError()
    {
        // Act
        var result = await _service.ResetTotpWithPasswordAsync(TestUserId, "");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Please enter your password"));
    }

    [Test]
    public async Task ResetTotpWithPasswordAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((UserRecord?)null);

        // Act
        var result = await _service.ResetTotpWithPasswordAsync(TestUserId, "password123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("User not found"));
    }

    [Test]
    public async Task ResetTotpWithPasswordAsync_WrongPassword_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockPasswordHasher.VerifyPassword("wrongpassword", TestPasswordHash)
            .Returns(false);

        // Act
        var result = await _service.ResetTotpWithPasswordAsync(TestUserId, "wrongpassword");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Invalid password"));
    }

    [Test]
    public async Task ResetTotpWithPasswordAsync_Success_ReturnsNewTotpSetup()
    {
        // Arrange
        var user = CreateTestUser();
        var totpResult = new TotpSetupResult("newsecret", "otpauth://totp/new", "NEW-MANUAL-KEY");

        _mockUserRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockPasswordHasher.VerifyPassword("correctpass", TestPasswordHash)
            .Returns(true);
        _mockTotpService.SetupTotpAsync(TestUserId, TestEmail, Arg.Any<CancellationToken>())
            .Returns(totpResult);

        // Act
        var result = await _service.ResetTotpWithPasswordAsync(TestUserId, "correctpass");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.QrCodeUri, Is.EqualTo("otpauth://totp/new"));
        Assert.That(result.ManualEntryKey, Is.EqualTo("NEW-MANUAL-KEY"));
    }

    #endregion

    #region SubscribePushAsync Tests

    [Test]
    public async Task SubscribePushAsync_EmptyEndpoint_ReturnsError()
    {
        // Act
        var result = await _service.SubscribePushAsync(TestUserId, "", "p256dh", "auth");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Invalid subscription data"));
    }

    [Test]
    public async Task SubscribePushAsync_EmptyP256dh_ReturnsError()
    {
        // Act
        var result = await _service.SubscribePushAsync(TestUserId, "endpoint", "", "auth");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Invalid subscription data"));
    }

    [Test]
    public async Task SubscribePushAsync_EmptyAuth_ReturnsError()
    {
        // Act
        var result = await _service.SubscribePushAsync(TestUserId, "endpoint", "p256dh", "");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Invalid subscription data"));
    }

    [Test]
    public async Task SubscribePushAsync_Success_CallsUpsert()
    {
        // Arrange
        PushSubscription? capturedSub = null;
        _mockPushRepo.UpsertAsync(Arg.Do<PushSubscription>(s => capturedSub = s), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<PushSubscription>()); // Return the captured subscription

        // Act
        var result = await _service.SubscribePushAsync(TestUserId, "https://endpoint.com", "p256dh-key", "auth-key");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(capturedSub, Is.Not.Null);
        Assert.That(capturedSub!.UserId, Is.EqualTo(TestUserId));
        Assert.That(capturedSub.Endpoint, Is.EqualTo("https://endpoint.com"));
        Assert.That(capturedSub.P256dh, Is.EqualTo("p256dh-key"));
        Assert.That(capturedSub.Auth, Is.EqualTo("auth-key"));
    }

    #endregion

    #region UnsubscribePushAsync Tests

    [Test]
    public async Task UnsubscribePushAsync_Success_DeletesAllUserSubscriptions()
    {
        // Arrange
        _mockPushRepo.DeleteByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(3); // Deleted 3 subscriptions

        // Act
        var result = await _service.UnsubscribePushAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        await _mockPushRepo.Received(1).DeleteByUserIdAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsubscribePushAsync_NoSubscriptions_StillSucceeds()
    {
        // Arrange - user has no subscriptions, but that's fine
        _mockPushRepo.DeleteByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);

        // Act
        var result = await _service.UnsubscribePushAsync(TestUserId);

        // Assert - success even if nothing was deleted (idempotent)
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SubscribePushAsync_RepositoryThrows_ReturnsError()
    {
        // Arrange
        _mockPushRepo.UpsertAsync(Arg.Any<PushSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.SubscribePushAsync(TestUserId, "https://endpoint.com", "p256dh", "auth");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to register push subscription"));
    }

    [Test]
    public async Task UnsubscribePushAsync_RepositoryThrows_ReturnsError()
    {
        // Arrange
        _mockPushRepo.DeleteByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.UnsubscribePushAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to remove push subscriptions"));
    }

    #endregion

    #region GenerateLinkTokenAsync Tests

    [Test]
    public async Task GenerateLinkTokenAsync_Success_ReturnsTokenAndExpiry()
    {
        // Arrange
        var expectedToken = "abc123token";
        var now = DateTimeOffset.UtcNow;
        var expectedExpiry = now.AddMinutes(15);
        var tokenRecord = new TelegramLinkTokenRecord(
            Token: expectedToken,
            UserId: TestUserId,
            CreatedAt: now,
            ExpiresAt: expectedExpiry,
            UsedAt: null,
            UsedByTelegramId: null);

        _mockLinkService.GenerateLinkTokenAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(tokenRecord);

        // Act
        var result = await _service.GenerateLinkTokenAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Token, Is.EqualTo(expectedToken));
        Assert.That(result.ExpiresAt, Is.EqualTo(expectedExpiry));
    }

    [Test]
    public async Task GenerateLinkTokenAsync_ServiceThrows_ReturnsError()
    {
        // Arrange
        _mockLinkService.GenerateLinkTokenAsync(TestUserId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Token generation failed"));

        // Act
        var result = await _service.GenerateLinkTokenAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to generate link token"));
    }

    #endregion

    #region UnlinkTelegramAccountAsync Tests

    [Test]
    public async Task UnlinkTelegramAccountAsync_Success_ReturnsOk()
    {
        // Arrange
        const long mappingId = 123;
        _mockLinkService.UnlinkAccountAsync(mappingId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.UnlinkTelegramAccountAsync(TestUserId, mappingId);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task UnlinkTelegramAccountAsync_NotFound_ReturnsError()
    {
        // Arrange
        const long mappingId = 999;
        _mockLinkService.UnlinkAccountAsync(mappingId, TestUserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _service.UnlinkTelegramAccountAsync(TestUserId, mappingId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Account not found or could not be unlinked"));
    }

    [Test]
    public async Task UnlinkTelegramAccountAsync_ServiceThrows_ReturnsError()
    {
        // Arrange
        const long mappingId = 123;
        _mockLinkService.UnlinkAccountAsync(mappingId, TestUserId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.UnlinkTelegramAccountAsync(TestUserId, mappingId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to unlink account"));
    }

    #endregion

    #region GetNotificationPreferencesAsync Tests

    [Test]
    public async Task GetNotificationPreferencesAsync_Success_ReturnsPreferences()
    {
        // Arrange
        var channels = new List<ChannelPreference>
        {
            new() { Channel = NotificationChannel.Email, EnabledEvents = [NotificationEventType.SpamDetected] }
        };
        var config = new NotificationConfig { Channels = channels };

        _mockPrefsRepo.GetOrCreateAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(config);
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<TelegramUserMappingRecord>());

        // Act
        var result = await _service.GetNotificationPreferencesAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.HasTelegramLinked, Is.False);
        Assert.That(result.Channels, Has.Count.EqualTo(1));
        Assert.That(result.Channels[0].Channel, Is.EqualTo(NotificationChannel.Email));
    }

    [Test]
    public async Task GetNotificationPreferencesAsync_WithActiveTelegramLinked_ReturnsTrueFlag()
    {
        // Arrange
        var config = new NotificationConfig { Channels = [] };
        var mappings = new List<TelegramUserMappingRecord>
        {
            new(Id: 1, TelegramId: 111, TelegramUsername: "user", UserId: TestUserId,
                LinkedAt: DateTimeOffset.UtcNow, IsActive: true)
        };

        _mockPrefsRepo.GetOrCreateAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(config);
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(mappings);

        // Act
        var result = await _service.GetNotificationPreferencesAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.HasTelegramLinked, Is.True);
    }

    [Test]
    public async Task GetNotificationPreferencesAsync_WithOnlyInactiveMappings_ReturnsFalseFlag()
    {
        // Arrange - user has a mapping but it's inactive (previously unlinked)
        var config = new NotificationConfig { Channels = [] };
        var mappings = new List<TelegramUserMappingRecord>
        {
            new(Id: 1, TelegramId: 111, TelegramUsername: "user", UserId: TestUserId,
                LinkedAt: DateTimeOffset.UtcNow, IsActive: false)
        };

        _mockPrefsRepo.GetOrCreateAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(config);
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(mappings);

        // Act
        var result = await _service.GetNotificationPreferencesAsync(TestUserId);

        // Assert - inactive mappings should not count as "linked"
        Assert.That(result.Success, Is.True);
        Assert.That(result.HasTelegramLinked, Is.False);
    }

    [Test]
    public async Task GetNotificationPreferencesAsync_RepoThrows_ReturnsError()
    {
        // Arrange
        _mockPrefsRepo.GetOrCreateAsync(TestUserId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.GetNotificationPreferencesAsync(TestUserId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to load notification preferences"));
    }

    #endregion

    #region SaveNotificationPreferencesAsync Tests

    [Test]
    public async Task SaveNotificationPreferencesAsync_Success_ReturnsOk()
    {
        // Arrange
        var channels = new List<ChannelPreference>
        {
            new() { Channel = NotificationChannel.Email, EnabledEvents = [NotificationEventType.SpamDetected] }
        };

        NotificationConfig? capturedConfig = null;
        _mockPrefsRepo.SaveAsync(TestUserId, Arg.Do<NotificationConfig>(c => capturedConfig = c), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.SaveNotificationPreferencesAsync(TestUserId, channels);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(capturedConfig, Is.Not.Null);
        Assert.That(capturedConfig!.Channels, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveNotificationPreferencesAsync_RepoThrows_ReturnsError()
    {
        // Arrange
        var channels = new List<ChannelPreference>();
        _mockPrefsRepo.SaveAsync(TestUserId, Arg.Any<NotificationConfig>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.SaveNotificationPreferencesAsync(TestUserId, channels);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Failed to save notification preferences"));
    }

    #endregion

    #region CancellationToken Propagation Tests

    [Test]
    public async Task GetProfilePageDataAsync_PropagatesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        CancellationToken capturedToken = default;

        _mockUserRepo.GetByIdAsync(Arg.Any<string>(), Arg.Do<CancellationToken>(ct => capturedToken = ct))
            .Returns(CreateTestUser());
        _mockMappingRepo.GetByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<TelegramUserMappingRecord>());

        // Act
        await _service.GetProfilePageDataAsync(TestUserId, token);

        // Assert
        Assert.That(capturedToken, Is.EqualTo(token));
    }

    #endregion
}
