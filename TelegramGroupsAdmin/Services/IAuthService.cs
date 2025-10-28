namespace TelegramGroupsAdmin.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<AuthResult> VerifyTotpAsync(string userId, string code, CancellationToken ct = default);
    Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken ct = default);
    Task<bool> IsFirstRunAsync(CancellationToken ct = default);
    Task<TotpSetupResult> EnableTotpAsync(string userId, CancellationToken ct = default);
    Task<bool> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default);
    Task<bool> DisableTotpAsync(string userId, string password, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default);
    Task<AuthResult> UseRecoveryCodeAsync(string userId, string code, CancellationToken ct = default);
    Task LogoutAsync(string userId, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<bool> ResendVerificationEmailAsync(string email, CancellationToken ct = default);
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken ct = default);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default);
}
