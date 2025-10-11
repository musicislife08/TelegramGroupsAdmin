using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Migration to remap old AuditEventType enum values to new consolidated values.
///
/// Old enum had duplicate entries (e.g., Login=0 and UserLogin=26 for the same event).
/// This migration maps all old values to their corresponding new values.
///
/// Old -> New mappings:
/// - Login (0) -> UserLogin (5)
/// - Logout (1) -> UserLogout (7)
/// - PasswordChange (2) -> UserPasswordChanged (16)
/// - TotpEnabled (3) -> UserTotpEnabled (19)
/// - TotpDisabled (4) -> UserTotpReset (18)
/// - UserCreated (5) -> UserRegistered (13)
/// - UserModified (6) -> UserStatusChanged (14)
/// - UserDeleted (7) -> UserDeleted (12)
/// - InviteCreated (8) -> UserInviteCreated (10)
/// - InviteUsed (9) -> No mapping (unused)
/// - InviteRevoked (10) -> UserInviteRevoked (11)
/// - PermissionChanged (11) -> UserPermissionChanged (17)
/// - FailedLogin (12) -> UserLoginFailed (6)
/// - PasswordReset (13) -> UserPasswordReset (8)
/// - UserInviteCreated (14) -> UserInviteCreated (10)
/// - UserInviteRevoked (15) -> UserInviteRevoked (11)
/// - UserPermissionChanged (16) -> UserPermissionChanged (17)
/// - UserStatusChanged (17) -> UserStatusChanged (14)
/// - UserTotpDisabled (18) -> UserTotpReset (18)
/// - DataExported (19) -> DataExported (0)
/// - UserTotpEnabled (20) -> UserTotpEnabled (19)
/// - UserEmailChanged (21) -> UserEmailChanged (15)
/// - UserPasswordChanged (22) -> UserPasswordChanged (16)
/// - UserLoginFailed (23) -> UserLoginFailed (6)
/// - UserLogout (24) -> UserLogout (7)
/// - MessageExported (25) -> MessageExported (1)
/// - UserLogin (26) -> UserLogin (5)
/// - UserRegistered (27) -> UserRegistered (13)
/// - UserPasswordReset (28) -> UserPasswordReset (8)
/// - UserPasswordResetRequested (29) -> UserPasswordResetRequested (9)
/// - UserEmailVerificationSent (30) -> UserEmailVerificationSent (3)
/// - SystemConfigChanged (31) -> SystemConfigChanged (2)
/// - UserEmailVerified (32) -> UserEmailVerified (4)
/// </summary>
[Migration(202601101)]
public class MigrateAuditEventTypes : Migration
{
    public override void Up()
    {
        // Update audit_log table to use new enum values
        // Process in order from highest to lowest to avoid conflicts during updates

        // First, move all values to temporary negative values to avoid conflicts
        Execute.Sql(@"
            -- Move existing values to temporary negative range
            UPDATE audit_log SET event_type = -1 WHERE event_type = 0;   -- Login -> temp
            UPDATE audit_log SET event_type = -2 WHERE event_type = 1;   -- Logout -> temp
            UPDATE audit_log SET event_type = -3 WHERE event_type = 2;   -- PasswordChange -> temp
            UPDATE audit_log SET event_type = -4 WHERE event_type = 3;   -- TotpEnabled -> temp
            UPDATE audit_log SET event_type = -5 WHERE event_type = 4;   -- TotpDisabled -> temp
            UPDATE audit_log SET event_type = -6 WHERE event_type = 5;   -- UserCreated -> temp
            UPDATE audit_log SET event_type = -7 WHERE event_type = 6;   -- UserModified -> temp
            UPDATE audit_log SET event_type = -8 WHERE event_type = 7;   -- UserDeleted -> temp
            UPDATE audit_log SET event_type = -9 WHERE event_type = 8;   -- InviteCreated -> temp
            UPDATE audit_log SET event_type = -10 WHERE event_type = 9;  -- InviteUsed -> temp (will map to UserInviteCreated)
            UPDATE audit_log SET event_type = -11 WHERE event_type = 10; -- InviteRevoked -> temp
            UPDATE audit_log SET event_type = -12 WHERE event_type = 11; -- PermissionChanged -> temp
            UPDATE audit_log SET event_type = -13 WHERE event_type = 12; -- FailedLogin -> temp
            UPDATE audit_log SET event_type = -14 WHERE event_type = 13; -- PasswordReset -> temp
            UPDATE audit_log SET event_type = -15 WHERE event_type = 14; -- UserInviteCreated -> temp
            UPDATE audit_log SET event_type = -16 WHERE event_type = 15; -- UserInviteRevoked -> temp
            UPDATE audit_log SET event_type = -17 WHERE event_type = 16; -- UserPermissionChanged -> temp
            UPDATE audit_log SET event_type = -18 WHERE event_type = 17; -- UserStatusChanged -> temp
            UPDATE audit_log SET event_type = -19 WHERE event_type = 18; -- UserTotpDisabled -> temp
            UPDATE audit_log SET event_type = -20 WHERE event_type = 19; -- DataExported -> temp
            UPDATE audit_log SET event_type = -21 WHERE event_type = 20; -- UserTotpEnabled -> temp
            UPDATE audit_log SET event_type = -22 WHERE event_type = 21; -- UserEmailChanged -> temp
            UPDATE audit_log SET event_type = -23 WHERE event_type = 22; -- UserPasswordChanged -> temp
            UPDATE audit_log SET event_type = -24 WHERE event_type = 23; -- UserLoginFailed -> temp
            UPDATE audit_log SET event_type = -25 WHERE event_type = 24; -- UserLogout -> temp
            UPDATE audit_log SET event_type = -26 WHERE event_type = 25; -- MessageExported -> temp
            UPDATE audit_log SET event_type = -27 WHERE event_type = 26; -- UserLogin -> temp
            UPDATE audit_log SET event_type = -28 WHERE event_type = 27; -- UserRegistered -> temp
            UPDATE audit_log SET event_type = -29 WHERE event_type = 28; -- UserPasswordReset -> temp
            UPDATE audit_log SET event_type = -30 WHERE event_type = 29; -- UserPasswordResetRequested -> temp
            UPDATE audit_log SET event_type = -31 WHERE event_type = 30; -- UserEmailVerificationSent -> temp
            UPDATE audit_log SET event_type = -32 WHERE event_type = 31; -- SystemConfigChanged -> temp
            UPDATE audit_log SET event_type = -33 WHERE event_type = 32; -- UserEmailVerified -> temp
        ");

        // Now map temporary values to new enum values
        Execute.Sql(@"
            -- Map old values to new consolidated enum values
            UPDATE audit_log SET event_type = 5 WHERE event_type = -1;   -- Login -> UserLogin (5)
            UPDATE audit_log SET event_type = 7 WHERE event_type = -2;   -- Logout -> UserLogout (7)
            UPDATE audit_log SET event_type = 16 WHERE event_type = -3;  -- PasswordChange -> UserPasswordChanged (16)
            UPDATE audit_log SET event_type = 19 WHERE event_type = -4;  -- TotpEnabled -> UserTotpEnabled (19)
            UPDATE audit_log SET event_type = 18 WHERE event_type = -5;  -- TotpDisabled -> UserTotpDisabled (18)
            UPDATE audit_log SET event_type = 13 WHERE event_type = -6;  -- UserCreated -> UserRegistered (13)
            UPDATE audit_log SET event_type = 14 WHERE event_type = -7;  -- UserModified -> UserStatusChanged (14)
            UPDATE audit_log SET event_type = 12 WHERE event_type = -8;  -- UserDeleted -> UserDeleted (12)
            UPDATE audit_log SET event_type = 10 WHERE event_type = -9;  -- InviteCreated -> UserInviteCreated (10)
            UPDATE audit_log SET event_type = 10 WHERE event_type = -10; -- InviteUsed -> UserInviteCreated (10)
            UPDATE audit_log SET event_type = 11 WHERE event_type = -11; -- InviteRevoked -> UserInviteRevoked (11)
            UPDATE audit_log SET event_type = 17 WHERE event_type = -12; -- PermissionChanged -> UserPermissionChanged (17)
            UPDATE audit_log SET event_type = 6 WHERE event_type = -13;  -- FailedLogin -> UserLoginFailed (6)
            UPDATE audit_log SET event_type = 8 WHERE event_type = -14;  -- PasswordReset -> UserPasswordReset (8)
            UPDATE audit_log SET event_type = 10 WHERE event_type = -15; -- UserInviteCreated -> UserInviteCreated (10)
            UPDATE audit_log SET event_type = 11 WHERE event_type = -16; -- UserInviteRevoked -> UserInviteRevoked (11)
            UPDATE audit_log SET event_type = 17 WHERE event_type = -17; -- UserPermissionChanged -> UserPermissionChanged (17)
            UPDATE audit_log SET event_type = 14 WHERE event_type = -18; -- UserStatusChanged -> UserStatusChanged (14)
            UPDATE audit_log SET event_type = 18 WHERE event_type = -19; -- UserTotpDisabled -> UserTotpDisabled (18)
            UPDATE audit_log SET event_type = 0 WHERE event_type = -20;  -- DataExported -> DataExported (0)
            UPDATE audit_log SET event_type = 19 WHERE event_type = -21; -- UserTotpEnabled -> UserTotpEnabled (19)
            UPDATE audit_log SET event_type = 15 WHERE event_type = -22; -- UserEmailChanged -> UserEmailChanged (15)
            UPDATE audit_log SET event_type = 16 WHERE event_type = -23; -- UserPasswordChanged -> UserPasswordChanged (16)
            UPDATE audit_log SET event_type = 6 WHERE event_type = -24;  -- UserLoginFailed -> UserLoginFailed (6)
            UPDATE audit_log SET event_type = 7 WHERE event_type = -25;  -- UserLogout -> UserLogout (7)
            UPDATE audit_log SET event_type = 1 WHERE event_type = -26;  -- MessageExported -> MessageExported (1)
            UPDATE audit_log SET event_type = 5 WHERE event_type = -27;  -- UserLogin -> UserLogin (5)
            UPDATE audit_log SET event_type = 13 WHERE event_type = -28; -- UserRegistered -> UserRegistered (13)
            UPDATE audit_log SET event_type = 8 WHERE event_type = -29;  -- UserPasswordReset -> UserPasswordReset (8)
            UPDATE audit_log SET event_type = 9 WHERE event_type = -30;  -- UserPasswordResetRequested -> UserPasswordResetRequested (9)
            UPDATE audit_log SET event_type = 3 WHERE event_type = -31;  -- UserEmailVerificationSent -> UserEmailVerificationSent (3)
            UPDATE audit_log SET event_type = 2 WHERE event_type = -32;  -- SystemConfigChanged -> SystemConfigChanged (2)
            UPDATE audit_log SET event_type = 4 WHERE event_type = -33;  -- UserEmailVerified -> UserEmailVerified (4)
        ");
    }

    public override void Down()
    {
        // Cannot reliably reverse this migration as multiple old values map to same new values
        // (e.g., Login (0) and UserLogin (26) both map to UserLogin (5))
        // Leave data as-is if rolling back
    }
}
