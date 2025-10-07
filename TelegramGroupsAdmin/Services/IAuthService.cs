using TelegramGroupsAdmin.Data.Models;

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
}

public record AuthResult(
    bool Success,
    string? UserId,
    string? Email,
    int? PermissionLevel,
    bool RequiresTotp,
    string? ErrorMessage
);

public record RegisterResult(
    bool Success,
    string? UserId,
    string? ErrorMessage
);

public record TotpSetupResult(
    string Secret,
    string QrCodeUri,
    string ManualEntryKey
);
