namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Manages the interactive WTelegram authentication flow for web admins.
/// Each admin connects their own Telegram account via phone → code → optional 2FA.
///
/// Scoped service — holds in-progress auth contexts in a static ConcurrentDictionary
/// (auth flows span multiple HTTP requests but must survive across scopes).
/// </summary>
public interface ITelegramAuthService
{
    /// <summary>Start auth flow: send verification code to phone.</summary>
    Task<AuthFlowState> StartAuthAsync(string webUserId, string phoneNumber, CancellationToken ct);

    /// <summary>Submit verification code.</summary>
    Task<AuthFlowState> SubmitCodeAsync(string webUserId, string code, CancellationToken ct);

    /// <summary>Submit 2FA password (if required).</summary>
    Task<AuthFlowState> Submit2FAAsync(string webUserId, string password, CancellationToken ct);

    /// <summary>Cancel an in-progress auth flow (user navigated away or wants to retry).</summary>
    Task CancelAuthAsync(string webUserId);

    /// <summary>Disconnect an existing session (deactivate + audit log).</summary>
    Task DisconnectAsync(string webUserId, CancellationToken ct);

    /// <summary>Get current connection status for a user.</summary>
    Task<ConnectionStatus> GetStatusAsync(string webUserId, CancellationToken ct);
}
