namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Types of auditable events in the system.
/// </summary>
public enum AuditEventType
{
    // User Lifecycle Events
    UserInviteCreated = 100,
    UserInviteRevoked = 101,
    UserRegistered = 102,
    UserStatusChanged = 103,
    UserDeleted = 104,

    // User Profile Changes
    UserPermissionChanged = 200,
    UserEmailChanged = 201,
    UserPasswordChanged = 202,
    UserTotpEnabled = 203,
    UserTotpDisabled = 204,
    UserEmailVerificationSent = 205,
    UserEmailVerified = 206,

    // Authentication Events
    UserLogin = 300,
    UserLoginFailed = 301,
    UserLogout = 302,
    UserPasswordReset = 303,
    UserPasswordResetRequested = 304,

    // Data Operations
    MessageExported = 400,
    DataExported = 401,

    // System Events
    SystemConfigChanged = 500,

    // Catch-all for extensibility
    Other = 999
}
