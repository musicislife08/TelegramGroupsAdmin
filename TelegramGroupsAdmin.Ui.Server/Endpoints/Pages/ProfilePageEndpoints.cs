using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Ui.Api;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Services;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;

/// <summary>
/// API endpoints for the Profile page.
/// Thin wrappers that delegate to IProfilePageService.
/// </summary>
public static class ProfilePageEndpoints
{
    public static IEndpointRouteBuilder MapProfilePageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Page data endpoint (aggregate for initial load)
        var pagesGroup = endpoints.MapGroup(Routes.Pages.Profile)
            .RequireAuthorization();

        // Action endpoints (mutations)
        var profileGroup = endpoints.MapGroup(Routes.Profile.Base)
            .RequireAuthorization();

        // GET /api/pages/profile - All profile page data
        pagesGroup.MapGet("", async (
            HttpContext context,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.GetProfilePageDataAsync(userId, cancellationToken));
        });

        // POST /api/profile/change-password - Change user password
        profileGroup.MapPost("/change-password", async (
            HttpContext context,
            [FromBody] ChangePasswordRequest request,
            [FromServices] IProfilePageService profileService,
            [FromServices] IRateLimitService rateLimitService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            // Rate limiting for password change attempts
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "password_change");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Ok(ChangePasswordResponse.Fail($"Too many password change attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."));
            }
            await rateLimitService.RecordAttemptAsync(userId, "password_change");

            return Results.Ok(await profileService.ChangePasswordAsync(
                userId,
                request.CurrentPassword,
                request.NewPassword,
                request.ConfirmPassword,
                cancellationToken));
        });

        // POST /api/profile/totp/setup - Start TOTP setup
        profileGroup.MapPost("/totp/setup", async (
            HttpContext context,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.SetupTotpAsync(userId, cancellationToken));
        });

        // POST /api/profile/totp/verify - Verify code and enable TOTP
        profileGroup.MapPost("/totp/verify", async (
            HttpContext context,
            [FromBody] ProfileTotpVerifyRequest request,
            [FromServices] IProfilePageService profileService,
            [FromServices] IRateLimitService rateLimitService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            // Rate limiting for TOTP verification attempts
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "profile_totp_verify");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Ok(ProfileTotpVerifyResponse.Fail($"Too many verification attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."));
            }
            await rateLimitService.RecordAttemptAsync(userId, "profile_totp_verify");

            return Results.Ok(await profileService.VerifyAndEnableTotpAsync(userId, request.Code, cancellationToken));
        });

        // POST /api/profile/totp/reset - Reset TOTP (requires password)
        profileGroup.MapPost("/totp/reset", async (
            HttpContext context,
            [FromBody] ProfileTotpResetRequest request,
            [FromServices] IProfilePageService profileService,
            [FromServices] IRateLimitService rateLimitService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            // Rate limiting for TOTP reset attempts (password validation)
            var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(userId, "profile_totp_reset");
            if (!rateLimitCheck.IsAllowed)
            {
                return Results.Ok(ProfileTotpSetupResponse.Fail($"Too many reset attempts. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes."));
            }
            await rateLimitService.RecordAttemptAsync(userId, "profile_totp_reset");

            return Results.Ok(await profileService.ResetTotpWithPasswordAsync(userId, request.Password, cancellationToken));
        });

        // POST /api/profile/telegram/generate-token - Generate Telegram link token
        profileGroup.MapPost("/telegram/generate-token", async (
            HttpContext context,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.GenerateLinkTokenAsync(userId, cancellationToken));
        });

        // POST /api/profile/telegram/unlink/{id} - Unlink Telegram account
        profileGroup.MapPost("/telegram/unlink/{id:long}", async (
            HttpContext context,
            long id,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.UnlinkTelegramAccountAsync(userId, id, cancellationToken));
        });

        // GET /api/profile/notifications - Get notification preferences
        profileGroup.MapGet("/notifications", async (
            HttpContext context,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.GetNotificationPreferencesAsync(userId, cancellationToken));
        });

        // POST /api/profile/notifications - Save notification preferences
        profileGroup.MapPost("/notifications", async (
            HttpContext context,
            [FromBody] NotificationPreferencesRequest request,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.SaveNotificationPreferencesAsync(userId, request.Channels, cancellationToken));
        });

        // GET /api/profile/webpush/vapid-key - Get VAPID public key
        profileGroup.MapGet("/webpush/vapid-key", async (
            [FromServices] IWebPushNotificationService webPushService,
            CancellationToken cancellationToken) =>
        {
            var vapidKey = await webPushService.GetVapidPublicKeyAsync(cancellationToken);
            return Results.Ok(string.IsNullOrEmpty(vapidKey)
                ? VapidKeyResponse.Fail("WebPush is not configured")
                : VapidKeyResponse.Ok(vapidKey));
        });

        // POST /api/profile/webpush/subscribe - Register push subscription
        profileGroup.MapPost("/webpush/subscribe", async (
            HttpContext context,
            [FromBody] PushSubscriptionRequest request,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.SubscribePushAsync(
                userId,
                request.Endpoint,
                request.P256dh,
                request.Auth,
                cancellationToken));
        });

        // DELETE /api/profile/webpush/unsubscribe - Remove all push subscriptions for user
        profileGroup.MapDelete("/webpush/unsubscribe", async (
            HttpContext context,
            [FromServices] IProfilePageService profileService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId == null) return Results.Unauthorized();

            return Results.Ok(await profileService.UnsubscribePushAsync(userId, cancellationToken));
        });

        return endpoints;
    }

    private static string? GetUserId(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
