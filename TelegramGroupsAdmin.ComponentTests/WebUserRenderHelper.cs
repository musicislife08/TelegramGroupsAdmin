using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests;

/// <summary>
/// Extension methods for providing WebUserIdentity as a cascading value in dialog tests.
/// Mirrors MainLayout where CascadingValue wraps MudDialogProvider so dialog content
/// receives the WebUser cascading parameter.
/// </summary>
public static class WebUserRenderHelper
{
    public static readonly WebUserIdentity TestWebUser = new("test-user", "test@example.com", PermissionLevel.Owner);

    /// <summary>
    /// Adds a CascadingValue&lt;WebUserIdentity?&gt; to the root render tree.
    /// Call this in the test constructor before rendering any dialog providers.
    /// </summary>
    public static void AddTestWebUser(this BunitContext ctx, WebUserIdentity? webUser = null)
    {
        webUser ??= TestWebUser;
        ctx.RenderTree.TryAdd<CascadingValue<WebUserIdentity?>>(p =>
            p.Add(cv => cv.Value, webUser));
    }
}
