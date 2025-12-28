using System.Security.Claims;
using TelegramGroupsAdmin.Ui.Server.Services;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public static class SseEndpoints
{
    public static IEndpointRouteBuilder MapSseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/events/stream", async (
            HttpContext context,
            SseConnectionManager sseManager,
            CancellationToken ct) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            // Set SSE headers
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            // Register client connection
            var connectionId = sseManager.AddClient(userId, context.Response);

            try
            {
                // Send initial connection event
                await context.Response.WriteAsync($"event: connected\ndata: {{\"connectionId\":\"{connectionId}\"}}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                // Keep connection open until client disconnects
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected - expected behavior
            }
            finally
            {
                sseManager.RemoveClient(connectionId);
            }

            return Results.Empty;
        }).RequireAuthorization();

        return endpoints;
    }
}
