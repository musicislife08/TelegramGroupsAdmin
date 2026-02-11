namespace TelegramGroupsAdmin.Services.Auth;

public interface ITotpService
{
    Task<TotpSetupResult> SetupTotpAsync(WebUserIdentity user, CancellationToken cancellationToken = default);
    Task<TotpVerificationResult> VerifyAndEnableTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
    Task<bool> VerifyTotpCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
    Task<bool> AdminDisableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<bool> AdminEnableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<bool> AdminResetTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(WebUserIdentity user, CancellationToken cancellationToken = default);
    Task<bool> UseRecoveryCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default);
}
