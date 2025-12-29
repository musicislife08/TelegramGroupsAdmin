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
            IHostApplicationLifetime appLifetime,
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

            // Link request CT with app shutdown CT for fast shutdown
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, appLifetime.ApplicationStopping);

            try
            {
                // Send initial connection event
                await context.Response.WriteAsync($"event: connected\ndata: {{\"connectionId\":\"{connectionId}\"}}\n\n", linkedCts.Token);
                await context.Response.Body.FlushAsync(linkedCts.Token);

                // Keep connection open until client disconnects OR app shuts down
                await Task.Delay(Timeout.Infinite, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or app shutting down - expected behavior
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
