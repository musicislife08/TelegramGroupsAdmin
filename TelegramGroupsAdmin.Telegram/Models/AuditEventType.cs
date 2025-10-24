namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Audit log event types for tracking system and user activities
/// </summary>
public enum AuditEventType
{
    // Data Operations
    /// <summary>User exported data from the system</summary>
    DataExported = 0,

    /// <summary>User exported message history</summary>
    MessageExported = 1,

    // System Events
    /// <summary>System configuration was modified</summary>
    SystemConfigChanged = 2,

    // User Authentication
    /// <summary>Email verification sent to user</summary>
    UserEmailVerificationSent = 3,

    /// <summary>User verified their email address</summary>
    UserEmailVerified = 4,

    /// <summary>User logged in successfully</summary>
    UserLogin = 5,

    /// <summary>User login attempt failed</summary>
    UserLoginFailed = 6,

    /// <summary>User logged out</summary>
    UserLogout = 7,

    /// <summary>User password was reset</summary>
    UserPasswordReset = 8,

    /// <summary>User requested password reset</summary>
    UserPasswordResetRequested = 9,

    // User Invites
    /// <summary>New invite token created</summary>
    UserInviteCreated = 10,

    /// <summary>Invite token revoked</summary>
    UserInviteRevoked = 11,

    // User Lifecycle
    /// <summary>User account deleted</summary>
    UserDeleted = 12,

    /// <summary>New user registered</summary>
    UserRegistered = 13,

    /// <summary>User status changed</summary>
    UserStatusChanged = 14,

    // User Profile Changes
    /// <summary>User email address changed</summary>
    UserEmailChanged = 15,

    /// <summary>User password changed</summary>
    UserPasswordChanged = 16,

    /// <summary>User permission level changed</summary>
    UserPermissionChanged = 17,

    /// <summary>User TOTP/2FA reset</summary>
    UserTotpReset = 18,

    /// <summary>User enabled TOTP/2FA</summary>
    UserTotpEnabled = 19,

    /// <summary>User automatically whitelisted</summary>
    UserAutoWhitelisted = 26,

    // Settings Changes (20-29 reserved for settings)
    /// <summary>Spam detection configuration changed</summary>
    SpamDetectionConfigChanged = 20,

    /// <summary>General system settings changed</summary>
    GeneralSettingsChanged = 21,

    /// <summary>Telegram bot settings changed</summary>
    TelegramSettingsChanged = 22,

    /// <summary>Notification settings changed</summary>
    NotificationSettingsChanged = 23,

    /// <summary>Security settings changed</summary>
    SecuritySettingsChanged = 24,

    /// <summary>Integration settings changed</summary>
    IntegrationSettingsChanged = 25,

    /// <summary>System configuration changed (spam detection, file scanning, logging, etc.)</summary>
    ConfigurationChanged = 28,

    /// <summary>Report reviewed and actioned (spam/ban/warn/dismiss)</summary>
    ReportReviewed = 27
}
