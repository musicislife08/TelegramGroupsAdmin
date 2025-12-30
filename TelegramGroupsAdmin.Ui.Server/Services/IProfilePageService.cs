using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Ui.Models;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Service for profile page data aggregation and mutations.
/// Returns UI models directly - keeps API endpoints as thin wrappers.
/// </summary>
public interface IProfilePageService
{
    /// <summary>
    /// Gets all profile page data for the specified user.
    /// Aggregates user info, linked Telegram accounts, and TOTP status.
    /// </summary>
    Task<ProfilePageResponse> GetProfilePageDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the user's password after validating current password.
    /// </summary>
    Task<ChangePasswordResponse> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts TOTP setup for the user. Returns QR code URI for client-side generation.
    /// </summary>
    Task<ProfileTotpSetupResponse> SetupTotpAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies TOTP code and enables 2FA. Returns recovery codes on success.
    /// </summary>
    Task<ProfileTotpVerifyResponse> VerifyAndEnableTotpAsync(
        string userId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets TOTP after verifying password. Returns new QR code URI.
    /// </summary>
    Task<ProfileTotpSetupResponse> ResetTotpWithPasswordAsync(
        string userId,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a Telegram link token for the specified user.
    /// </summary>
    Task<GenerateLinkTokenResponse> GenerateLinkTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlinks a Telegram account from the specified user.
    /// </summary>
    Task<UnlinkTelegramResponse> UnlinkTelegramAccountAsync(string userId, long mappingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets notification preferences for the specified user.
    /// Includes whether user has Telegram accounts linked.
    /// </summary>
    Task<NotificationPreferencesResponse> GetNotificationPreferencesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves notification preferences for the specified user.
    /// </summary>
    Task<SaveNotificationPreferencesResponse> SaveNotificationPreferencesAsync(
        string userId,
        List<ChannelPreference> channels,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a WebPush subscription for the specified user.
    /// </summary>
    Task<PushSubscriptionResponse> SubscribePushAsync(
        string userId,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all WebPush subscriptions for the specified user (all browsers/devices).
    /// Global disable for UX consistency with other notification channels.
    /// </summary>
    Task<PushSubscriptionResponse> UnsubscribePushAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
