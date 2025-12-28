using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Extensions;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Actions;

/// <summary>
/// User-related API endpoints for the UserDetailDialog and user management.
/// Follows the aggregate pattern for reads (single call returns all data)
/// and focused endpoints for mutations.
/// </summary>
public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Aggregate page endpoint
        var pagesGroup = endpoints.MapGroup("/api/pages/users")
            .RequireAuthorization();

        // Action endpoints
        var usersGroup = endpoints.MapGroup("/api/users")
            .RequireAuthorization();

        // ============================================================================
        // Aggregate Endpoint - Single call for all UserDetailDialog data
        // ============================================================================

        // GET /api/pages/users/{userId} - Full data for UserDetailDialog
        pagesGroup.MapGet("/{userId:long}", async (
            long userId,
            [FromServices] ITelegramUserRepository userRepo,
            [FromServices] ITagDefinitionsRepository tagDefRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            // Fetch user detail and tag colors in parallel
            var userDetailTask = userRepo.GetUserDetailAsync(userId, ct);
            var tagDefsTask = tagDefRepo.GetAllAsync(ct);

            await Task.WhenAll(userDetailTask, tagDefsTask);

            var userDetail = await userDetailTask;
            var tagDefs = await tagDefsTask;

            var tagColors = tagDefs.ToDictionary(t => t.TagName, t => t.Color);

            return Results.Ok(new UserDetailDialogResponse(userDetail, tagColors));
        });

        // ============================================================================
        // Trust Actions
        // ============================================================================

        // POST /api/users/{userId}/trust - Toggle trust status
        usersGroup.MapPost("/{userId:long}/trust", async (
            long userId,
            [FromServices] TelegramUserManagementService userService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var executor = Actor.FromWebUser(webUserId);
            var success = await userService.ToggleTrustAsync(userId, executor, ct);

            return success
                ? Results.Ok(UserActionResponse.Ok())
                : Results.BadRequest(UserActionResponse.Fail("Failed to toggle trust status"));
        });

        // ============================================================================
        // Ban Actions
        // ============================================================================

        // POST /api/users/{userId}/unban - Unban user
        usersGroup.MapPost("/{userId:long}/unban", async (
            long userId,
            [FromServices] ModerationOrchestrator moderationService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var executor = Actor.FromWebUser(webUserId);
            var result = await moderationService.UnbanUserAsync(
                userId,
                executor,
                reason: "Unbanned from user detail dialog",
                restoreTrust: false,
                ct);

            return result.Success
                ? Results.Ok(UserActionResponse.Ok(chatsAffected: result.ChatsAffected, trustRestored: result.TrustRestored))
                : Results.BadRequest(UserActionResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/users/{userId}/temp-ban - Temporarily ban user
        usersGroup.MapPost("/{userId:long}/temp-ban", async (
            long userId,
            [FromBody] UserTempBanRequest request,
            [FromServices] ModerationOrchestrator moderationService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var executor = Actor.FromWebUser(webUserId);
            var result = await moderationService.TempBanUserAsync(
                userId: userId,
                messageId: null,
                executor: executor,
                reason: request.Reason ?? "Temporarily banned from user detail dialog",
                duration: request.Duration,
                ct);

            return result.Success
                ? Results.Ok(UserActionResponse.Ok(chatsAffected: result.ChatsAffected, bannedUntil: DateTimeOffset.UtcNow.Add(request.Duration)))
                : Results.BadRequest(UserActionResponse.Fail(result.ErrorMessage!));
        });

        // ============================================================================
        // Notes Actions
        // ============================================================================

        // POST /api/users/{userId}/notes - Add a note
        usersGroup.MapPost("/{userId:long}/notes", async (
            long userId,
            [FromBody] AddNoteRequest request,
            [FromServices] IAdminNotesRepository notesRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.NoteText))
                return Results.BadRequest(ApiResponse.Fail("Note text is required"));

            var actor = Actor.FromWebUser(webUserId);
            var note = new AdminNote
            {
                TelegramUserId = userId,
                NoteText = request.NoteText.Trim(),
                CreatedBy = actor,
                CreatedAt = DateTimeOffset.UtcNow,
                IsPinned = false
            };

            await notesRepo.AddNoteAsync(note, ct);
            return Results.Ok(ApiResponse.Ok());
        });

        // DELETE /api/users/{userId}/notes/{noteId} - Delete a note
        usersGroup.MapDelete("/{userId:long}/notes/{noteId:long}", async (
            long userId,
            long noteId,
            [FromServices] IAdminNotesRepository notesRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var actor = Actor.FromWebUser(webUserId);
            await notesRepo.DeleteNoteAsync(noteId, actor, ct);
            return Results.Ok(ApiResponse.Ok());
        });

        // POST /api/users/{userId}/notes/{noteId}/pin - Toggle note pin
        usersGroup.MapPost("/{userId:long}/notes/{noteId:long}/pin", async (
            long userId,
            long noteId,
            [FromServices] IAdminNotesRepository notesRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var actor = Actor.FromWebUser(webUserId);
            await notesRepo.TogglePinAsync(noteId, actor, ct);
            return Results.Ok(ApiResponse.Ok());
        });

        // ============================================================================
        // Tags Actions
        // ============================================================================

        // POST /api/users/{userId}/tags - Add tags
        usersGroup.MapPost("/{userId:long}/tags", async (
            long userId,
            [FromBody] AddTagsRequest request,
            [FromServices] IUserTagsRepository tagsRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            if (request.TagNames == null || request.TagNames.Count == 0)
                return Results.BadRequest(ApiResponse.Fail("At least one tag is required"));

            var actor = Actor.FromWebUser(webUserId);

            foreach (var tagName in request.TagNames)
            {
                var tag = new UserTag
                {
                    TelegramUserId = userId,
                    TagName = tagName,
                    AddedBy = actor,
                    AddedAt = DateTimeOffset.UtcNow
                };
                await tagsRepo.AddTagAsync(tag, ct);
            }

            return Results.Ok(ApiResponse.Ok());
        });

        // DELETE /api/users/{userId}/tags/{tagId} - Delete a tag
        usersGroup.MapDelete("/{userId:long}/tags/{tagId:long}", async (
            long userId,
            long tagId,
            [FromServices] IUserTagsRepository tagsRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var actor = Actor.FromWebUser(webUserId);
            await tagsRepo.DeleteTagAsync(tagId, actor, ct);
            return Results.Ok(ApiResponse.Ok());
        });

        // ============================================================================
        // Warning Actions
        // ============================================================================

        // POST /api/users/{userId}/actions/{actionId}/expire - Expire a warning
        usersGroup.MapPost("/{userId:long}/actions/{actionId:long}/expire", async (
            long userId,
            long actionId,
            [FromServices] IUserActionsRepository actionsRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var webUserId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (webUserId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var actor = Actor.FromWebUser(webUserId);
            await actionsRepo.ExpireActionAsync(actionId, actor, ct);
            return Results.Ok(ApiResponse.Ok());
        });

        return endpoints;
    }
}
