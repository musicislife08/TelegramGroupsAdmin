using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Auth;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Endpoints;

public static class EmailVerificationEndpoints
{
    public static void MapEmailVerificationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/verify-email", VerifyEmail).AllowAnonymous();
        routes.MapPost("/resend-verification", ResendVerification).AllowAnonymous();
    }

    private static async Task<IResult> VerifyEmail(
        string token,
        IVerificationTokenRepository verificationTokenRepository,
        IUserRepository userRepository,
        IAuditService auditLog,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest("Verification token is required");
        }

        // Find verification token
        var verificationToken = await verificationTokenRepository.GetValidTokenAsync(token, (DataModels.TokenType)TokenType.EmailVerification, cancellationToken);

        if (verificationToken == null)
        {
            logger.LogWarning("Invalid or expired verification token attempted: {Token}", token);
            return Results.BadRequest("Invalid or expired verification token. Please request a new one.");
        }

        // Get user
        var user = await userRepository.GetByIdAsync(verificationToken.UserId, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("Verification token {Token} references non-existent user {UserId}", token, verificationToken.UserId);
            return Results.BadRequest("Invalid verification token");
        }

        // Check if already verified
        if (user.EmailVerified)
        {
            logger.LogInformation("User {UserId} already verified, but token matched", user.WebUser.Id);
            return Results.Redirect("/login?verified=already");
        }

        // Mark email as verified
        var updatedUser = user with
        {
            EmailVerified = true,
            ModifiedBy = user.WebUser.Id,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, cancellationToken);

        // Mark token as used
        await verificationTokenRepository.MarkAsUsedAsync(token, cancellationToken);

        logger.LogInformation("Email verified for user {UserId}", user.WebUser.Id);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserEmailVerified,
            actor: Actor.FromWebUser(user.WebUser.Id),
            target: Actor.FromWebUser(user.WebUser.Id),
            value: user.WebUser.Email,
            cancellationToken: cancellationToken);

        return Results.Redirect("/login?verified=success");
    }

    private static async Task<IResult> ResendVerification(
        ResendVerificationRequest request,
        IAuthService authService,
        IRateLimitService rateLimitService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("Email is required");
        }

        // Rate limiting (SECURITY-5)
        var rateLimitCheck = await rateLimitService.CheckRateLimitAsync(request.Email, "resend_verification", cancellationToken);
        if (!rateLimitCheck.IsAllowed)
        {
            return Results.BadRequest($"Too many verification email requests. Please try again in {rateLimitCheck.RetryAfter?.TotalMinutes:F0} minutes.");
        }

        // Record attempt for rate limiting
        await rateLimitService.RecordAttemptAsync(request.Email, "resend_verification", cancellationToken);

        var success = await authService.ResendVerificationEmailAsync(request.Email, cancellationToken);

        if (success)
        {
            logger.LogInformation("Verification email resent to {Email}", request.Email);
            return Results.Ok(new { message = "Verification email sent. Please check your inbox." });
        }

        // Don't reveal whether email exists or is already verified for security
        return Results.BadRequest("Unable to send verification email. Email may already be verified or invalid.");
    }
}

public record ResendVerificationRequest(string Email);
