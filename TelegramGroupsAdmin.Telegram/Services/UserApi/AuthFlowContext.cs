using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Holds the state for a single in-progress WTelegram authentication flow.
/// Created by TelegramAuthService.StartAuthAsync, stored in IAuthFlowStore.
/// </summary>
public sealed class AuthFlowContext : IAsyncDisposable
{
    public required IWTelegramApiClient Client { get; init; }
    public required string PhoneNumber { get; init; }
    public required Actor Executor { get; init; }

    // Cross-thread synchronization (ARM64 memory visibility)
    public readonly Lock Lock = new();
    public TaskCompletionSource<string>? PendingInput { get; set; }
    public AuthStep CurrentStep { get; set; } = AuthStep.CodeSent;
    public Task? LoginTask { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[] SessionData { get; set; } = [];

    /// <summary>Signal fired when the background login flow changes state (needs input, completes, fails).</summary>
    public TaskCompletionSource StepSignal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Auto-cancel after 5 minutes to prevent abandoned flows from leaking resources.</summary>
    public CancellationTokenSource FlowTimeout { get; } = new(TimeSpan.FromMinutes(5));

    public async ValueTask DisposeAsync()
    {
        PendingInput?.TrySetCanceled();
        FlowTimeout.Dispose();
        try { await Client.DisposeAsync(); }
        catch { /* best-effort */ }
    }
}
