using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Mappings;

/// <summary>
/// Mapping extensions for User records
/// </summary>
public static class UserMappings
{
    extension(DataModels.UserRecordDto data)
    {
        public UserRecord ToModel() => new(
            WebUser: new WebUserIdentity(data.Id, data.Email, (PermissionLevel)data.PermissionLevel),
            NormalizedEmail: data.NormalizedEmail,
            PasswordHash: data.PasswordHash,
            SecurityStamp: data.SecurityStamp,
            InvitedBy: data.InvitedBy,
            IsActive: data.IsActive,
            TotpSecret: data.TotpSecret,
            TotpEnabled: data.TotpEnabled,
            TotpSetupStartedAt: data.TotpSetupStartedAt,
            CreatedAt: data.CreatedAt,
            LastLoginAt: data.LastLoginAt,
            Status: (UserStatus)data.Status,
            ModifiedBy: data.ModifiedBy,
            ModifiedAt: data.ModifiedAt,
            EmailVerified: data.EmailVerified,
            EmailVerificationToken: data.EmailVerificationToken,
            EmailVerificationTokenExpiresAt: data.EmailVerificationTokenExpiresAt,
            PasswordResetToken: data.PasswordResetToken,
            PasswordResetTokenExpiresAt: data.PasswordResetTokenExpiresAt,
            FailedLoginAttempts: data.FailedLoginAttempts,
            LockedUntil: data.LockedUntil
        );
    }

    extension(UserRecord ui)
    {
        public DataModels.UserRecordDto ToDto() => new()
        {
            Id = ui.WebUser.Id,
            Email = ui.WebUser.Email!,
            NormalizedEmail = ui.NormalizedEmail,
            PasswordHash = ui.PasswordHash,
            SecurityStamp = ui.SecurityStamp,
            PermissionLevel = (DataModels.PermissionLevel)(int)ui.WebUser.PermissionLevel,
            InvitedBy = ui.InvitedBy,
            IsActive = ui.IsActive,
            TotpSecret = ui.TotpSecret,
            TotpEnabled = ui.TotpEnabled,
            TotpSetupStartedAt = ui.TotpSetupStartedAt,
            CreatedAt = ui.CreatedAt,
            LastLoginAt = ui.LastLoginAt,
            Status = (DataModels.UserStatus)(int)ui.Status,
            ModifiedBy = ui.ModifiedBy,
            ModifiedAt = ui.ModifiedAt,
            EmailVerified = ui.EmailVerified,
            EmailVerificationToken = ui.EmailVerificationToken,
            EmailVerificationTokenExpiresAt = ui.EmailVerificationTokenExpiresAt,
            PasswordResetToken = ui.PasswordResetToken,
            PasswordResetTokenExpiresAt = ui.PasswordResetTokenExpiresAt,
            FailedLoginAttempts = ui.FailedLoginAttempts,
            LockedUntil = ui.LockedUntil
        };
    }
}
