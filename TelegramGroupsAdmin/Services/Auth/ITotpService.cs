namespace TelegramGroupsAdmin.Services.Auth;

public interface ITotpService
{
    Task<TotpSetupResult> SetupTotpAsync(string userId, string userEmail, CancellationToken cancellationToken = default);
    Task<TotpVerificationResult> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task<bool> AdminDisableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<bool> AdminEnableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<bool> AdminResetTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> UseRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default);
}
