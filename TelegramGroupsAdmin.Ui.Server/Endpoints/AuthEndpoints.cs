using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Services;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/auth/first-run - Returns whether this is first run (no users exist)
        endpoints.MapGet("/api/auth/first-run", async ([FromServices] IAuthService authService) =>
        {
            var isFirstRun = await authService.IsFirstRunAsync();
            return Results.Json(new { isFirstRun });
        }).AllowAnonymous();

        // GET /api/auth/me - Returns current user info for WASM auth state
        endpoints.MapGet("/api/auth/me", (HttpContext httpContext) =>
        {
            var user = httpContext.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity.Name;
            var permissionLevelClaim = user.FindFirst("permission_level")?.Value;

            if (userId == null || email == null)
            {
                return Results.Unauthorized();
            }

            var permissionLevel = 0;
            if (permissionLevelClaim != null && int.TryParse(permissionLevelClaim, out var level))
            {
                permissionLevel = level;
            }

            return Results.Ok(new AuthMeResponse(
                UserId: userId,
                Email: email,
                PermissionLevel: permissionLevel
            ));
        }).RequireAuthorization();

        endpoints.MapPost("/api/auth/login", async (
            [FromBody] LoginRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IIntermediateAuthService intermediateAuthService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] IAuthCookieService authCookieService,
            HttpContext httpContext) =>
        {
            // Rate limiting (SECURITY-5)
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.Email, "login");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many login attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(request.Email, "login");

            var result = await authService.LoginAsync(request.Email, request.Password);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage });
            }

            if (result.RequiresTotp)
            {
                // Generate intermediate authentication token (valid for 5 minutes)
                var intermediateToken = intermediateAuthService.CreateToken(result.UserId!);

                return Results.Json(new
                {
                    success = true,
                    requiresTotp = true,
                    userId = result.UserId,
                    intermediateToken = intermediateToken
                });
            }

            // Check if user needs to set up TOTP (TotpEnabled=true but no secret yet)
            if (result.TotpEnabled)
            {
                // Generate intermediate authentication token for TOTP setup flow
                var intermediateToken = intermediateAuthService.CreateToken(result.UserId!);

                return Results.Json(new
                {
                    success = true,
                    requiresTotpSetup = true,
                    userId = result.UserId,
                    intermediateToken = intermediateToken
                });
            }

            // Sign in the user with cookie authentication (TOTP disabled by owner)
            await authCookieService.SignInAsync(httpContext, result.UserId!, result.Email!, (PermissionLevel)result.PermissionLevel!.Value);

            // Check if this is a browser request (has Accept: text/html)
            var acceptHeader = httpContext.Request.Headers.Accept.ToString();
            if (acceptHeader.Contains("text/html"))
            {
                // Browser request - redirect to homepage
                return Results.Redirect("/");
            }

            return Results.Json(new { success = true });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/register", async (
            [FromBody] RegisterRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] ILogger<Program> logger,
            HttpContext httpContext) =>
        {
            // Rate limiting (SECURITY-5)
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.Email, "register");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many registration attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(request.Email, "register");

            try
            {
                var result = await authService.RegisterAsync(request.Email, request.Password, request.InviteToken);

                if (!result.Success)
                {
                    return Results.Json(new { success = false, error = result.ErrorMessage });
                }

                // Registration successful - always redirect to login page
                // The login flow handles: email verification, TOTP setup, TOTP verification
                // No cookies are set here - authentication only happens through the login flow
                logger.LogInformation("Registration succeeded for {Email}, redirecting to login", request.Email);
                return Results.Json(new { success = true, message = "Account created successfully! Please log in." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during registration for {Email}", request.Email);
                return Results.Json(new { success = false, error = "An unexpected error occurred during registration" });
            }
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Json(new { success = true });
        }).RequireAuthorization();

        endpoints.MapPost("/api/auth/verify-totp", async (
            [FromBody] VerifyTotpRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IIntermediateAuthService intermediateAuthService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] IAuthCookieService authCookieService,
            HttpContext httpContext) =>
        {
            // SECURITY: Validate intermediate auth token and get userId from token (not from client)
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, out var userId) || userId == null)
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            // Rate limiting (SECURITY-5) - use userId as identifier for TOTP verification
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "totp_verify");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many TOTP verification attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(userId, "totp_verify");

            var result = await authService.VerifyTotpAsync(userId, request.Code);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage });
            }

            // Sign in the user with cookie authentication
            await authCookieService.SignInAsync(httpContext, result.UserId!, result.Email!, (PermissionLevel)result.PermissionLevel!.Value);

            // Check if this is a browser request (has Accept: text/html)
            var acceptHeader = httpContext.Request.Headers.Accept.ToString();
            if (acceptHeader.Contains("text/html"))
            {
                // Browser request - redirect to homepage
                return Results.Redirect("/");
            }

            return Results.Json(new { success = true });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/verify-recovery-code", async (
            [FromBody] VerifyRecoveryCodeRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IIntermediateAuthService intermediateAuthService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] IAuthCookieService authCookieService,
            HttpContext httpContext) =>
        {
            // SECURITY: Validate intermediate auth token and get userId from token (not from client)
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, out var userId) || userId == null)
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            // Rate limiting (SECURITY-5) - use userId as identifier for recovery code verification
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "recovery_code");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many recovery code attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(userId, "recovery_code");

            var result = await authService.UseRecoveryCodeAsync(userId, request.RecoveryCode);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage });
            }

            // Sign in the user with cookie authentication
            await authCookieService.SignInAsync(httpContext, result.UserId!, result.Email!, (PermissionLevel)result.PermissionLevel!.Value);

            // Check if this is a browser request (has Accept: text/html)
            var acceptHeader = httpContext.Request.Headers.Accept.ToString();
            if (acceptHeader.Contains("text/html"))
            {
                // Browser request - redirect to homepage
                return Results.Redirect("/");
            }

            return Results.Json(new { success = true });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/setup-totp", async (
            [FromBody] SetupTotpRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IIntermediateAuthService intermediateAuthService) =>
        {
            // SECURITY: Validate intermediate auth token and get userId from token (not from client)
            if (!intermediateAuthService.TryGetUserId(request.IntermediateToken, out var userId) || userId == null)
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            try
            {
                var result = await authService.EnableTotpAsync(userId);

                return Results.Json(new
                {
                    success = true,
                    qrCodeUri = result.QrCodeUri,
                    manualEntryKey = result.ManualEntryKey
                });
            }
            catch (Exception)
            {
                return Results.Json(new { success = false, error = "Failed to setup two-factor authentication" });
            }
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/verify-setup-totp", async (
            [FromBody] VerifySetupTotpRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IIntermediateAuthService intermediateAuthService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] IAuthCookieService authCookieService,
            HttpContext httpContext) =>
        {
            // SECURITY: Validate intermediate auth token and get userId from token (not from client)
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, out var userId) || userId == null)
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            // Rate limiting
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "totp_setup");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many TOTP setup attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            await rateLimitService.RecordAttemptAsync(userId, "totp_setup");

            var result = await authService.VerifyAndEnableTotpAsync(userId, request.Code);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage ?? "Invalid verification code" });
            }

            // Generate recovery codes for the user
            var recoveryCodes = await authService.GenerateRecoveryCodesAsync(userId);

            // Sign in the user with cookie authentication
            await authCookieService.SignInAsync(httpContext, result.UserId!, result.Email!, (PermissionLevel)result.PermissionLevel!.Value);

            return Results.Json(new
            {
                success = true,
                recoveryCodes = recoveryCodes
            });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/forgot-password", async (
            [FromBody] ForgotPasswordRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IRateLimitService rateLimitService) =>
        {
            // Rate limiting
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.Email, "forgot_password");
            if (!rateLimitCheck.IsAllowed)
            {
                // Still return success to prevent email enumeration
                return Results.Json(new { success = true });
            }

            await rateLimitService.RecordAttemptAsync(request.Email, "forgot_password");

            // Always return success to prevent email enumeration
            await authService.RequestPasswordResetAsync(request.Email);

            return Results.Json(new { success = true });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/reset-password", async (
            [FromBody] ResetPasswordRequest request,
            [FromServices] IAuthService authService,
            [FromServices] IRateLimitService rateLimitService) =>
        {
            // Rate limiting based on token to prevent brute-force attacks
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.Token, "reset_password");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many reset attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            await rateLimitService.RecordAttemptAsync(request.Token, "reset_password");

            // Validate password length
            if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8)
            {
                return Results.Json(new { success = false, error = "Password must be at least 8 characters" });
            }

            var success = await authService.ResetPasswordAsync(request.Token, request.NewPassword);

            if (!success)
            {
                return Results.Json(new { success = false, error = "Invalid or expired reset token. Please request a new password reset link." });
            }

            return Results.Json(new { success = true });
        }).AllowAnonymous();

        return endpoints;
    }
}
