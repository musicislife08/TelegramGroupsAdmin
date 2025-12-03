using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
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
            // SECURITY: Validate intermediate auth token to ensure password was verified first
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, request.UserId))
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            // Rate limiting (SECURITY-5) - use userId as identifier for TOTP verification
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.UserId, "totp_verify");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many TOTP verification attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(request.UserId, "totp_verify");

            var result = await authService.VerifyTotpAsync(request.UserId, request.Code);

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
            // SECURITY: Validate intermediate auth token to ensure password was verified first
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, request.UserId))
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            // Rate limiting (SECURITY-5) - use userId as identifier for recovery code verification
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.UserId, "recovery_code");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Json(new
                {
                    success = false,
                    error = $"Too many recovery code attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."
                });
            }

            // Record attempt for rate limiting
            await rateLimitService.RecordAttemptAsync(request.UserId, "recovery_code");

            var result = await authService.UseRecoveryCodeAsync(request.UserId, request.RecoveryCode);

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

        return endpoints;
    }
}
