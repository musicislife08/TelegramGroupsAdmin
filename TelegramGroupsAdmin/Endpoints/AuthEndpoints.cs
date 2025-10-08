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
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.UserId!),
                new(ClaimTypes.Email, result.Email!),
                new(ClaimTypes.Role, GetRoleName(result.PermissionLevel!.Value)),
                new(CustomClaimTypes.PermissionLevel, result.PermissionLevel.Value.ToString())
            };

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
            HttpContext httpContext) =>
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
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, loginResult.UserId!),
                    new(ClaimTypes.Email, loginResult.Email!),
                    new(ClaimTypes.Role, GetRoleName(loginResult.PermissionLevel!.Value)),
                    new(CustomClaimTypes.PermissionLevel, loginResult.PermissionLevel.Value.ToString())
                };

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

                return Results.Json(new { success = true });
            }

            return Results.Json(new { success = false, error = "Login failed after registration" });
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
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.UserId!),
                new(ClaimTypes.Email, result.Email!),
                new(ClaimTypes.Role, GetRoleName(result.PermissionLevel!.Value)),
                new(CustomClaimTypes.PermissionLevel, result.PermissionLevel.Value.ToString())
            };

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
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.UserId!),
                new(ClaimTypes.Email, result.Email!),
                new(ClaimTypes.Role, GetRoleName(result.PermissionLevel!.Value)),
                new(CustomClaimTypes.PermissionLevel, result.PermissionLevel.Value.ToString())
            };

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
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "ReadOnly"
    };
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string? InviteToken);
public record VerifyTotpRequest(string UserId, string Code, string IntermediateToken);
public record VerifyRecoveryCodeRequest(string UserId, string RecoveryCode, string IntermediateToken);
