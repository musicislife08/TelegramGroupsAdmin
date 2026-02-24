using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Manages interactive WTelegram authentication flows.
///
/// Design: WTelegram.Client.LoginUserIfNeeded() calls a config callback repeatedly,
/// requesting "api_id", "api_hash", "phone_number", "verification_code", "password" etc.
/// We bridge this to the web UI using TaskCompletionSource — the callback awaits
/// a TCS that gets resolved when the user submits code/password via the UI.
///
/// Auth contexts are stored in a static ConcurrentDictionary because the flow spans
/// multiple HTTP requests (phone submit → code submit → optional 2FA submit) and
/// the service is scoped per request.
/// </summary>
public sealed class TelegramAuthService(
    IServiceScopeFactory scopeFactory,
    IWTelegramClientFactory clientFactory,
    ILogger<TelegramAuthService> logger) : ITelegramAuthService
{
    // Static: auth flows span multiple scoped service lifetimes
    private static readonly ConcurrentDictionary<string, AuthFlowContext> ActiveFlows = new();

    private sealed class AuthFlowContext : IAsyncDisposable
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

    public async Task<AuthFlowState> StartAuthAsync(string webUserId, string phoneNumber, Actor executor, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        var config = await configRepo.GetUserApiConfigAsync(ct);
        var apiHash = await configRepo.GetUserApiHashAsync(ct);
        if (config.ApiId == 0 || string.IsNullOrEmpty(apiHash))
            return new AuthFlowState(AuthStep.Failed, "API credentials must be configured by an Owner in Settings first.");

        var context = new AuthFlowContext
        {
            Client = clientFactory.Create(
                what => ConfigCallback(what, webUserId, config.ApiId, apiHash, phoneNumber),
                startSession: [],
                saveSession: data => CaptureSessionData(webUserId, data)),
            PhoneNumber = phoneNumber,
            Executor = executor
        };

        if (!ActiveFlows.TryAdd(webUserId, context))
        {
            await context.DisposeAsync();
            return new AuthFlowState(AuthStep.Failed, "An authentication flow is already in progress.");
        }

        // Start LoginUserIfNeeded on a background thread — it will block on the
        // config callback awaiting TaskCompletionSource values from SubmitCode/Submit2FA
        context.LoginTask = Task.Run(async () =>
        {
            try
            {
                await context.Client.LoginUserIfNeeded();
                using (context.Lock.EnterScope())
                    context.CurrentStep = AuthStep.Connected;
            }
            catch (TaskCanceledException)
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "Authentication was cancelled.";
                }
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PHONE_NUMBER_INVALID"))
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "Invalid phone number format.";
                }
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PHONE_CODE_INVALID"))
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "Invalid verification code.";
                }
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PASSWORD_HASH_INVALID"))
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "Incorrect 2FA password.";
                }
            }
            catch (ApplicationException ex) when (ex.Message.Contains("no Telegram account"))
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "This phone number does not have a Telegram account.";
                }
            }
            catch (Exception ex)
            {
                using (context.Lock.EnterScope())
                {
                    context.CurrentStep = AuthStep.Failed;
                    context.ErrorMessage = "Authentication failed due to an unexpected error. Check server logs for details.";
                }
                logger.LogError(ex, "WTelegram auth flow failed for web user {WebUserId}", webUserId);
            }
            finally
            {
                // Signal completion so WaitForStepChangeAsync can exit
                context.StepSignal.TrySetResult();
            }
        }, CancellationToken.None); // WTelegram.Client has internal network timeouts; FlowTimeout covers user-input waits only

        // Wait for the login flow to either request verification code or fail
        await WaitForStepChangeAsync(context, ct);

        AuthStep step;
        string? error;
        using (context.Lock.EnterScope())
        {
            step = context.CurrentStep;
            error = context.ErrorMessage;
        }

        if (step == AuthStep.Failed)
        {
            ActiveFlows.TryRemove(webUserId, out _);
            await context.DisposeAsync();
            return new AuthFlowState(AuthStep.Failed, error);
        }

        return new AuthFlowState(AuthStep.CodeSent);
    }

    public async Task<AuthFlowState> SubmitCodeAsync(string webUserId, string code, CancellationToken ct)
    {
        if (!ActiveFlows.TryGetValue(webUserId, out var context))
            return new AuthFlowState(AuthStep.Failed, "No authentication flow in progress.");

        TaskCompletionSource<string>? tcs;
        using (context.Lock.EnterScope())
        {
            tcs = context.PendingInput;
            if (tcs is null)
                return new AuthFlowState(AuthStep.Failed, "Not waiting for verification code.");
            context.PendingInput = null;
        }

        // Resolve the TaskCompletionSource — LoginUserIfNeeded continues
        tcs.TrySetResult(code);

        // Wait for login to advance (it will either complete, request 2FA, or fail)
        await WaitForStepChangeAsync(context, ct);

        return await HandleStepResult(webUserId, context, ct);
    }

    public async Task<AuthFlowState> Submit2FAAsync(string webUserId, string password, CancellationToken ct)
    {
        if (!ActiveFlows.TryGetValue(webUserId, out var context))
            return new AuthFlowState(AuthStep.Failed, "No authentication flow in progress.");

        TaskCompletionSource<string>? tcs;
        using (context.Lock.EnterScope())
        {
            tcs = context.PendingInput;
            if (tcs is null)
                return new AuthFlowState(AuthStep.Failed, "Not waiting for 2FA password.");
            context.PendingInput = null;
        }

        tcs.TrySetResult(password);

        await WaitForStepChangeAsync(context, ct);

        return await HandleStepResult(webUserId, context, ct);
    }

    public async Task CancelAuthAsync(string webUserId)
    {
        if (ActiveFlows.TryRemove(webUserId, out var context))
        {
            await context.DisposeAsync();
            logger.LogInformation("Cancelled WTelegram auth flow for web user {WebUserId}", webUserId);
        }
    }

    public async Task<ConnectionStatus> GetStatusAsync(string webUserId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var session = await sessionRepo.GetActiveSessionAsync(webUserId, ct);

        if (session is null)
            return new ConnectionStatus(false, null, null, null);

        return new ConnectionStatus(
            true,
            session.DisplayName,
            session.TelegramUserId,
            session.ConnectedAt);
    }

    private async Task<AuthFlowState> HandleStepResult(string webUserId, AuthFlowContext context, CancellationToken ct)
    {
        AuthStep step;
        string? error;
        using (context.Lock.EnterScope())
        {
            step = context.CurrentStep;
            error = context.ErrorMessage;
        }

        switch (step)
        {
            case AuthStep.Connected:
                await FinalizeConnectionAsync(webUserId, context, ct);
                return new AuthFlowState(AuthStep.Connected);

            case AuthStep.Requires2FA:
                return new AuthFlowState(AuthStep.Requires2FA);

            case AuthStep.Failed:
                ActiveFlows.TryRemove(webUserId, out _);
                await context.DisposeAsync();
                return new AuthFlowState(AuthStep.Failed, error);

            default:
                return new AuthFlowState(step);
        }
    }

    private async Task FinalizeConnectionAsync(string webUserId, AuthFlowContext context, CancellationToken ct)
    {
        ActiveFlows.TryRemove(webUserId, out _);

        var telegramUser = context.Client.User;
        var telegramUserId = context.Client.UserId;
        var displayName = BuildDisplayName(telegramUser);

        await using var scope = scopeFactory.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ITelegramSessionRepository>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var mappingRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();

        // Deactivate any existing session for this user (enforced by partial unique index too)
        var existingSession = await sessionRepo.GetActiveSessionAsync(webUserId, ct);
        if (existingSession is not null)
            await sessionRepo.DeactivateSessionAsync(existingSession.Id, ct);

        // Create new session
        var session = new TelegramSession
        {
            WebUserId = webUserId,
            TelegramUserId = telegramUserId,
            DisplayName = displayName,
            PhoneNumber = context.PhoneNumber,
            SessionData = context.SessionData,
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        await sessionRepo.CreateSessionAsync(session, ct);

        // Audit: account connected
        await auditService.LogEventAsync(
            AuditEventType.TelegramAccountConnected,
            context.Executor,
            value: $"Connected as {displayName} (ID: {telegramUserId})",
            cancellationToken: ct);

        logger.LogInformation("WTelegram auth completed for {Executor} as {DisplayName} (TG ID: {TelegramUserId})",
            context.Executor.GetDisplayText(), displayName, telegramUserId);

        // Auto-link Telegram account mapping if not already linked
        await TryAutoLinkAccountAsync(webUserId, telegramUserId, telegramUser, mappingRepo, auditService, ct);

        // Dispose the auth client — session manager will create its own on next GetClientAsync
        await context.DisposeAsync();
    }

    private async Task TryAutoLinkAccountAsync(
        string webUserId,
        long telegramUserId,
        TL.User? telegramUser,
        ITelegramUserMappingRepository mappingRepo,
        IAuditService auditService,
        CancellationToken ct)
    {
        if (await mappingRepo.IsTelegramIdLinkedAsync(telegramUserId, ct))
        {
            // Check if linked to a different user — log warning but don't overwrite
            var existingUserId = await mappingRepo.GetUserIdByTelegramIdAsync(telegramUserId, ct);
            if (existingUserId is not null && existingUserId != webUserId)
            {
                logger.LogWarning(
                    "Telegram ID {TelegramUserId} is already linked to a different web user {ExistingUserId} — skipping auto-link for {WebUserId}",
                    telegramUserId, existingUserId, webUserId);
            }

            return;
        }

        var mapping = new TelegramUserMappingRecord(
            Id: 0,
            TelegramId: telegramUserId,
            TelegramUsername: telegramUser?.username,
            UserId: webUserId,
            LinkedAt: DateTimeOffset.UtcNow,
            IsActive: true);

        await mappingRepo.InsertAsync(mapping, ct);

        await auditService.LogEventAsync(
            AuditEventType.TelegramAccountLinked,
            Actor.FromWebUser(webUserId),
            value: $"Auto-linked Telegram account (ID: {telegramUserId})",
            cancellationToken: ct);

        logger.LogInformation("Auto-linked Telegram ID {TelegramUserId} to web user {WebUserId}", telegramUserId, webUserId);
    }

    private string? ConfigCallback(string what, string webUserId, int apiId, string apiHash, string phoneNumber)
    {
        return what switch
        {
            "api_id" => apiId.ToString(),
            "api_hash" => apiHash,
            "phone_number" => phoneNumber,
            "verification_code" => WaitForUserInput(webUserId, AuthStep.CodeSent),
            "password" => WaitForUserInput(webUserId, AuthStep.Requires2FA),
            "first_name" or "last_name" =>
                throw new ApplicationException("This phone number does not have a Telegram account — no Telegram account will be created."),
            _ => null
        };
    }

    private string WaitForUserInput(string webUserId, AuthStep step)
    {
        if (!ActiveFlows.TryGetValue(webUserId, out var context))
            throw new OperationCanceledException("Auth flow was cancelled.");

        TaskCompletionSource<string> tcs;
        using (context.Lock.EnterScope())
        {
            context.CurrentStep = step;
            tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.PendingInput = tcs;

            // Signal that the flow needs user input — WaitForStepChangeAsync is watching
            context.StepSignal.TrySetResult();
            context.StepSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Block the config callback thread until the UI submits the value
        // Use the flow timeout to prevent indefinite hangs if user navigates away
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.FlowTimeout.Token);
        try
        {
            return tcs.Task.WaitAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Auth flow timed out waiting for user input.");
        }
    }

    private void CaptureSessionData(string webUserId, byte[] data)
    {
        if (ActiveFlows.TryGetValue(webUserId, out var context))
        {
            using (context.Lock.EnterScope())
                context.SessionData = data;
        }
    }

    private static async Task WaitForStepChangeAsync(AuthFlowContext context, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            while (true)
            {
                // Read state under lock for ARM64 visibility
                bool loginCompleted;
                bool hasPendingInput;
                AuthStep step;
                Task stepSignalTask;
                Task? loginTask;

                using (context.Lock.EnterScope())
                {
                    loginTask = context.LoginTask;
                    loginCompleted = loginTask?.IsCompleted ?? true;
                    hasPendingInput = context.PendingInput is not null;
                    step = context.CurrentStep;
                    stepSignalTask = context.StepSignal.Task;
                }

                if (loginCompleted || hasPendingInput || step is AuthStep.Failed or AuthStep.Connected)
                    break;

                // Wait for: login task completion, step signal (input needed), or timeout
                await Task.WhenAny(loginTask!, stepSignalTask, Task.Delay(Timeout.Infinite, cts.Token));

                // Task.WhenAny doesn't throw on cancellation — check explicitly to avoid busy-spin
                cts.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            using (context.Lock.EnterScope())
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Authentication timed out waiting for Telegram response.";
            }
        }
    }

    private static string? BuildDisplayName(TL.User? user)
    {
        if (user is null) return null;
        var name = $"{user.first_name} {user.last_name}".Trim();
        return string.IsNullOrEmpty(name) ? user.username : name;
    }
}
