using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken cancellationToken = default);
    Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);
    Task<TotpSetupResult> EnableTotpAsync(WebUserIdentity user, CancellationToken cancellationToken = default);
    Task<TotpVerificationResult> VerifyAndEnableTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
    Task<bool> AdminDisableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<bool> AdminEnableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<bool> AdminResetTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(WebUserIdentity user, CancellationToken cancellationToken = default);
    Task<AuthResult> UseRecoveryCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
    Task AuditLogoutAsync(WebUserIdentity user, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(WebUserIdentity user, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
