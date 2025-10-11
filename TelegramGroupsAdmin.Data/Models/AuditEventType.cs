namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Types of auditable events in the system.
/// IMPORTANT: These values MUST match the database audit_log.event_type column.
/// Do not change existing values - only add new ones at the end.
/// </summary>
public enum AuditEventType
{
    // Data Operations
    DataExported = 0,
    MessageExported = 1,

    // System Events
    SystemConfigChanged = 2,

    // User Authentication
    UserEmailVerificationSent = 3,
    UserEmailVerified = 4,
    UserLogin = 5,
    UserLoginFailed = 6,
    UserLogout = 7,
    UserPasswordReset = 8,
    UserPasswordResetRequested = 9,

    // User Invites
    UserInviteCreated = 10,
    UserInviteRevoked = 11,

    // User Lifecycle
    UserDeleted = 12,
    UserRegistered = 13,
    UserStatusChanged = 14,

    // User Profile Changes
    UserEmailChanged = 15,
    UserPasswordChanged = 16,
    UserPermissionChanged = 17,
    UserTotpReset = 18,
    UserTotpEnabled = 19,

    // Settings Changes (20-29 reserved for settings)
    SpamDetectionConfigChanged = 20,
    GeneralSettingsChanged = 21,
    TelegramSettingsChanged = 22,
    NotificationSettingsChanged = 23,
    SecuritySettingsChanged = 24,
    IntegrationSettingsChanged = 25
}
