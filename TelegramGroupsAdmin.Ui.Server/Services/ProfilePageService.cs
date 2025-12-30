using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Service for profile page data aggregation and mutations.
/// Returns UI models directly - keeps API endpoints as thin wrappers.
/// </summary>
public class ProfilePageService : IProfilePageService
{
    private readonly ILogger<ProfilePageService> _logger;
    private readonly IUserRepository _userRepo;
    private readonly ITelegramUserMappingRepository _mappingRepo;
    private readonly INotificationPreferencesRepository _prefsRepo;
    private readonly IPushSubscriptionsRepository _pushRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;
    private readonly ITelegramLinkService _linkService;

    public ProfilePageService(
        ILogger<ProfilePageService> logger,
        IUserRepository userRepo,
        ITelegramUserMappingRepository mappingRepo,
        INotificationPreferencesRepository prefsRepo,
        IPushSubscriptionsRepository pushRepo,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        ITelegramLinkService linkService)
    {
        _logger = logger;
        _userRepo = userRepo;
        _mappingRepo = mappingRepo;
        _prefsRepo = prefsRepo;
        _pushRepo = pushRepo;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _linkService = linkService;
    }

    public async Task<ProfilePageResponse> GetProfilePageDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return ProfilePageResponse.Fail("User not found");
        }

        var linkedAccounts = await _mappingRepo.GetByUserIdAsync(userId, cancellationToken);
        var linkedAccountDtos = linkedAccounts
            .Where(a => a.IsActive)
            .Select(a => new LinkedTelegramAccountDto(
                a.Id,
                a.TelegramId,
                a.TelegramUsername,
                a.LinkedAt))
            .ToList();

        return ProfilePageResponse.Ok(
            email: user.Email,
            permissionLevel: user.PermissionLevelInt,
            createdAt: user.CreatedAt,
            lastLoginAt: user.LastLoginAt,
            totpEnabled: user.TotpEnabled,
            linkedAccounts: linkedAccountDtos);
    }

    public async Task<ChangePasswordResponse> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return ChangePasswordResponse.Fail("Please fill in all fields");
        }

        if (newPassword != confirmPassword)
        {
            return ChangePasswordResponse.Fail("New passwords do not match");
        }

        if (newPassword.Length < 8)
        {
            return ChangePasswordResponse.Fail("New password must be at least 8 characters");
        }

        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return ChangePasswordResponse.Fail("User not found");
        }

        // Verify current password
        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return ChangePasswordResponse.Fail("Current password is incorrect");
        }

        // Hash and update
        var newPasswordHash = _passwordHasher.HashPassword(newPassword);
        var updatedUser = user with
        {
            PasswordHash = newPasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ModifiedBy = userId,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await _userRepo.UpdateAsync(updatedUser, cancellationToken);
        return ChangePasswordResponse.Ok();
    }

    public async Task<ProfileTotpSetupResponse> SetupTotpAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                return ProfileTotpSetupResponse.Fail("User not found");
            }

            // If TOTP is already enabled, user must use Reset flow (requires password)
            if (user.TotpEnabled)
            {
                return ProfileTotpSetupResponse.Fail("Two-factor authentication is already enabled. Use Reset to reconfigure.");
            }

            var result = await _totpService.SetupTotpAsync(userId, user.Email, cancellationToken);
            return ProfileTotpSetupResponse.Ok(result.QrCodeUri, result.ManualEntryKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup TOTP for user {UserId}", userId);
            return ProfileTotpSetupResponse.Fail("Failed to setup two-factor authentication");
        }
    }

    public async Task<ProfileTotpVerifyResponse> VerifyAndEnableTotpAsync(
        string userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ProfileTotpVerifyResponse.Fail("Please enter the verification code");
        }

        var result = await _totpService.VerifyAndEnableTotpAsync(userId, code, cancellationToken);
        if (!result.Success)
        {
            return ProfileTotpVerifyResponse.Fail(result.ErrorMessage ?? "Invalid verification code");
        }

        // Generate recovery codes
        var recoveryCodes = await _totpService.GenerateRecoveryCodesAsync(userId, cancellationToken);
        return ProfileTotpVerifyResponse.Ok(recoveryCodes.ToList());
    }

    public async Task<ProfileTotpSetupResponse> ResetTotpWithPasswordAsync(
        string userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return ProfileTotpSetupResponse.Fail("Please enter your password");
        }

        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return ProfileTotpSetupResponse.Fail("User not found");
        }

        // Verify password
        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return ProfileTotpSetupResponse.Fail("Invalid password");
        }

        try
        {
            // Generate new TOTP secret
            var result = await _totpService.SetupTotpAsync(userId, user.Email, cancellationToken);
            return ProfileTotpSetupResponse.Ok(result.QrCodeUri, result.ManualEntryKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset TOTP for user {UserId}", userId);
            return ProfileTotpSetupResponse.Fail("Failed to reset two-factor authentication");
        }
    }

    public async Task<GenerateLinkTokenResponse> GenerateLinkTokenAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenRecord = await _linkService.GenerateLinkTokenAsync(userId, cancellationToken);
            return GenerateLinkTokenResponse.Ok(tokenRecord.Token, tokenRecord.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate link token for user {UserId}", userId);
            return GenerateLinkTokenResponse.Fail("Failed to generate link token");
        }
    }

    public async Task<UnlinkTelegramResponse> UnlinkTelegramAccountAsync(
        string userId,
        long mappingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _linkService.UnlinkAccountAsync(mappingId, userId, cancellationToken);
            return success
                ? UnlinkTelegramResponse.Ok()
                : UnlinkTelegramResponse.Fail("Account not found or could not be unlinked");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unlink Telegram account {MappingId} for user {UserId}", mappingId, userId);
            return UnlinkTelegramResponse.Fail("Failed to unlink account");
        }
    }

    public async Task<NotificationPreferencesResponse> GetNotificationPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _prefsRepo.GetOrCreateAsync(userId, cancellationToken);
            var mappings = await _mappingRepo.GetByUserIdAsync(userId, cancellationToken);
            // Only count active mappings - inactive ones have been unlinked
            var hasTelegramLinked = mappings.Any(m => m.IsActive);

            return NotificationPreferencesResponse.Ok(hasTelegramLinked, config.Channels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notification preferences for user {UserId}", userId);
            return NotificationPreferencesResponse.Fail("Failed to load notification preferences");
        }
    }

    public async Task<SaveNotificationPreferencesResponse> SaveNotificationPreferencesAsync(
        string userId,
        List<ChannelPreference> channels,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new NotificationConfig { Channels = channels };
            await _prefsRepo.SaveAsync(userId, config, cancellationToken);
            return SaveNotificationPreferencesResponse.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save notification preferences for user {UserId}", userId);
            return SaveNotificationPreferencesResponse.Fail("Failed to save notification preferences");
        }
    }

    public async Task<PushSubscriptionResponse> SubscribePushAsync(
        string userId,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(p256dh) ||
            string.IsNullOrWhiteSpace(auth))
        {
            return PushSubscriptionResponse.Fail("Invalid subscription data");
        }

        try
        {
            var subscription = new PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth
            };

            await _pushRepo.UpsertAsync(subscription, cancellationToken);
            return PushSubscriptionResponse.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register push subscription for user {UserId}", userId);
            return PushSubscriptionResponse.Fail("Failed to register push subscription");
        }
    }

    public async Task<PushSubscriptionResponse> UnsubscribePushAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _pushRepo.DeleteByUserIdAsync(userId, cancellationToken);
            return PushSubscriptionResponse.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove push subscriptions for user {UserId}", userId);
            return PushSubscriptionResponse.Fail("Failed to remove push subscriptions");
        }
    }
}
