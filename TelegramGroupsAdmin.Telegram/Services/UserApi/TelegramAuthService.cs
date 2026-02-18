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
        public TaskCompletionSource<string>? PendingInput { get; set; }
        public AuthStep CurrentStep { get; set; } = AuthStep.CodeSent;
        public Task? LoginTask { get; set; }
        public string? ErrorMessage { get; set; }
        public byte[] SessionData { get; set; } = [];

        public async ValueTask DisposeAsync()
        {
            PendingInput?.TrySetCanceled();
            try { await Client.DisposeAsync(); }
            catch { /* best-effort */ }
        }
    }

    public async Task<AuthFlowState> StartAuthAsync(string webUserId, string phoneNumber, CancellationToken ct)
    {
        // Reject if another flow is already in progress for this user
        if (ActiveFlows.ContainsKey(webUserId))
            return new AuthFlowState(AuthStep.Failed, "An authentication flow is already in progress. Cancel it first.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        if (!await configRepo.HasUserApiCredentialsAsync(ct))
            return new AuthFlowState(AuthStep.Failed, "API credentials must be configured by an Owner in Settings first.");

        var config = await configRepo.GetUserApiConfigAsync(ct);
        var apiHash = await configRepo.GetUserApiHashAsync(ct);
        if (config.ApiId == 0 || string.IsNullOrEmpty(apiHash))
            return new AuthFlowState(AuthStep.Failed, "API credentials are incomplete.");

        var context = new AuthFlowContext
        {
            Client = clientFactory.Create(
                what => ConfigCallback(what, webUserId, config.ApiId, apiHash, phoneNumber),
                startSession: [],
                saveSession: data => CaptureSessionData(webUserId, data)),
            PhoneNumber = phoneNumber
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
                context.CurrentStep = AuthStep.Connected;
            }
            catch (TaskCanceledException)
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Authentication was cancelled.";
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PHONE_NUMBER_INVALID"))
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Invalid phone number format.";
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PHONE_CODE_INVALID"))
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Invalid verification code.";
            }
            catch (TL.RpcException ex) when (ex.Message.Contains("PASSWORD_HASH_INVALID"))
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Incorrect 2FA password.";
            }
            catch (ApplicationException ex) when (ex.Message.Contains("no Telegram account"))
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "This phone number does not have a Telegram account.";
            }
            catch (Exception ex)
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = $"Authentication failed: {ex.Message}";
                logger.LogError(ex, "WTelegram auth flow failed for web user {WebUserId}", webUserId);
            }
        }, CancellationToken.None);

        // Give the login flow a moment to request the phone and send the code
        await Task.Delay(2000, ct);

        if (context.CurrentStep == AuthStep.Failed)
        {
            ActiveFlows.TryRemove(webUserId, out _);
            var error = context.ErrorMessage;
            await context.DisposeAsync();
            return new AuthFlowState(AuthStep.Failed, error);
        }

        return new AuthFlowState(AuthStep.CodeSent);
    }

    public async Task<AuthFlowState> SubmitCodeAsync(string webUserId, string code, CancellationToken ct)
    {
        if (!ActiveFlows.TryGetValue(webUserId, out var context))
            return new AuthFlowState(AuthStep.Failed, "No authentication flow in progress.");

        if (context.PendingInput is null)
            return new AuthFlowState(AuthStep.Failed, "Not waiting for verification code.");

        // Resolve the TaskCompletionSource — LoginUserIfNeeded continues
        context.PendingInput.TrySetResult(code);
        context.PendingInput = null;

        // Wait for login to advance (it will either complete, request 2FA, or fail)
        await WaitForStepChangeAsync(context, ct);

        return await HandleStepResult(webUserId, context, ct);
    }

    public async Task<AuthFlowState> Submit2FAAsync(string webUserId, string password, CancellationToken ct)
    {
        if (!ActiveFlows.TryGetValue(webUserId, out var context))
            return new AuthFlowState(AuthStep.Failed, "No authentication flow in progress.");

        if (context.PendingInput is null)
            return new AuthFlowState(AuthStep.Failed, "Not waiting for 2FA password.");

        context.PendingInput.TrySetResult(password);
        context.PendingInput = null;

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
        switch (context.CurrentStep)
        {
            case AuthStep.Connected:
                await FinalizeConnectionAsync(webUserId, context, ct);
                return new AuthFlowState(AuthStep.Connected);

            case AuthStep.Requires2FA:
                return new AuthFlowState(AuthStep.Requires2FA);

            case AuthStep.Failed:
                ActiveFlows.TryRemove(webUserId, out _);
                var error = context.ErrorMessage;
                await context.DisposeAsync();
                return new AuthFlowState(AuthStep.Failed, error);

            default:
                return new AuthFlowState(context.CurrentStep);
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
            SessionData = context.SessionData,
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        await sessionRepo.CreateSessionAsync(session, ct);

        // Audit: account connected
        await auditService.LogEventAsync(
            AuditEventType.TelegramAccountConnected,
            Actor.FromWebUser(webUserId),
            value: $"Connected as {displayName} (ID: {telegramUserId})",
            cancellationToken: ct);

        logger.LogInformation("WTelegram auth completed for web user {WebUserId} as {DisplayName} (TG ID: {TelegramUserId})",
            webUserId, displayName, telegramUserId);

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
            AuditEventType.TelegramAccountConnected,
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

        context.CurrentStep = step;
        context.PendingInput = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Block the config callback thread until the UI submits the value
        return context.PendingInput.Task.GetAwaiter().GetResult();
    }

    private void CaptureSessionData(string webUserId, byte[] data)
    {
        if (ActiveFlows.TryGetValue(webUserId, out var context))
            context.SessionData = data;
    }

    private static async Task WaitForStepChangeAsync(AuthFlowContext context, CancellationToken ct)
    {
        // Wait for the login task to advance — either it completes, requests more input, or fails
        var timeout = Task.Delay(30_000, ct);
        while (context.LoginTask is not null
               && !context.LoginTask.IsCompleted
               && context.PendingInput is null
               && context.CurrentStep is not AuthStep.Failed)
        {
            var completed = await Task.WhenAny(context.LoginTask, timeout);
            if (completed == timeout)
            {
                context.CurrentStep = AuthStep.Failed;
                context.ErrorMessage = "Authentication timed out waiting for Telegram response.";
                break;
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
