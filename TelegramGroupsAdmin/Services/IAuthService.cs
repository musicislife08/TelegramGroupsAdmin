namespace TelegramGroupsAdmin.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyTotpAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken cancellationToken = default);
    Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);
    Task<TotpSetupResult> EnableTotpAsync(string userId, CancellationToken cancellationToken = default);
    Task<TotpVerificationResult> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task<bool> AdminDisableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<bool> AdminEnableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<bool> AdminResetTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    Task<AuthResult> UseRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task LogoutAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
