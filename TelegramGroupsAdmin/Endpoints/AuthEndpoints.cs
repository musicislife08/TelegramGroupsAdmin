using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Auth;
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
            HttpContext httpContext) =>
        {
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

            // Sign in the user with cookie authentication
            await SignInUserAsync(httpContext, result.UserId!, result.Email!, result.PermissionLevel!.Value);

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
            [FromServices] ILogger<Program> logger,
            HttpContext httpContext) =>
        {
            try
            {
                var result = await authService.RegisterAsync(request.Email, request.Password, request.InviteToken);

                if (!result.Success)
                {
                    return Results.Json(new { success = false, error = result.ErrorMessage });
                }

                // Auto-login after successful registration
                var loginResult = await authService.LoginAsync(request.Email, request.Password);

                if (loginResult.Success && !loginResult.RequiresTotp)
                {
                    // Sign in the user with cookie authentication
                    await SignInUserAsync(httpContext, loginResult.UserId!, loginResult.Email!, loginResult.PermissionLevel!.Value);

                    return Results.Json(new { success = true });
                }

                // Login failed - check if it's due to email verification
                if (loginResult.ErrorMessage?.Contains("verify your email", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogInformation("Registration succeeded for {Email}, email verification required", request.Email);
                    return Results.Json(new { success = true, requiresEmailVerification = true, message = "Account created! Please check your email to verify your account before logging in." });
                }

                // Other login failure (TOTP setup, etc.)
                var errorMsg = loginResult.ErrorMessage ?? "Login failed after registration";
                logger.LogWarning("Registration succeeded but auto-login failed for {Email}: {Error}", request.Email, errorMsg);
                return Results.Json(new { success = false, error = errorMsg });
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
            HttpContext httpContext) =>
        {
            // SECURITY: Validate intermediate auth token to ensure password was verified first
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, request.UserId))
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            var result = await authService.VerifyTotpAsync(request.UserId, request.Code);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage });
            }

            // Sign in the user with cookie authentication
            await SignInUserAsync(httpContext, result.UserId!, result.Email!, result.PermissionLevel!.Value);

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
            HttpContext httpContext) =>
        {
            // SECURITY: Validate intermediate auth token to ensure password was verified first
            if (!intermediateAuthService.ValidateAndConsumeToken(request.IntermediateToken, request.UserId))
            {
                return Results.Json(new { success = false, error = "Invalid or expired authentication session" });
            }

            var result = await authService.UseRecoveryCodeAsync(request.UserId, request.RecoveryCode);

            if (!result.Success)
            {
                return Results.Json(new { success = false, error = result.ErrorMessage });
            }

            // Sign in the user with cookie authentication
            await SignInUserAsync(httpContext, result.UserId!, result.Email!, result.PermissionLevel!.Value);

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

    private static string GetRoleName(int permissionLevel) => permissionLevel switch
    {
        0 => "Admin",
        1 => "GlobalAdmin",
        2 => "Owner",
        _ => "Admin" // Default to lowest permission level
    };

    private static async Task SignInUserAsync(HttpContext httpContext, string userId, string email, int permissionLevel)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, GetRoleName(permissionLevel)),
            new(CustomClaimTypes.PermissionLevel, permissionLevel.ToString())
        ];

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            claimsPrincipal,
            authProperties);
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string? InviteToken);
public record VerifyTotpRequest(string UserId, string Code, string IntermediateToken);
public record VerifyRecoveryCodeRequest(string UserId, string RecoveryCode, string IntermediateToken);
