namespace TelegramGroupsAdmin.Services.Auth;

public interface ITotpService
{
    Task<TotpSetupResult> SetupTotpAsync(string userId, string userEmail, CancellationToken ct = default);
    Task<bool> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default);
    Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken ct = default);
    Task<bool> DisableTotpAsync(string userId, string password, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default);
    Task<bool> UseRecoveryCodeAsync(string userId, string code, CancellationToken ct = default);
}
