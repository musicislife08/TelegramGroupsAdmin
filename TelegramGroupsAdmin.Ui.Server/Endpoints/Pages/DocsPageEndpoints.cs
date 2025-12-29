using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Models.Docs;
using TelegramGroupsAdmin.Ui.Server.Services.Docs;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;

/// <summary>
/// API endpoints for the Documentation page.
/// Exposes the IDocumentationService to the WASM client.
/// </summary>
public static class DocsPageEndpoints
{
    public static IEndpointRouteBuilder MapDocsPageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pages/docs")
            .RequireAuthorization();

        // GET /api/pages/docs/nav - Navigation tree for sidebar
        group.MapGet("/nav", (
            [FromServices] IDocumentationService docsService) =>
        {
            if (!docsService.IsInitialized)
            {
                return Results.Ok(Array.Empty<DocsNavItemResponse>());
            }

            var navTree = docsService.GetNavigationTree();
            var response = navTree.Select(MapNavItem).ToList();
            return Results.Ok(response);
        });

        // GET /api/pages/docs/{*path} - Document content by path
        group.MapGet("/{*path}", (
            string? path,
            [FromServices] IDocumentationService docsService) =>
        {
            if (!docsService.IsInitialized)
            {
                return Results.NotFound(new { error = "Documentation not initialized" });
            }

            // Empty path - return first document info for redirect
            if (string.IsNullOrWhiteSpace(path))
            {
                var navTree = docsService.GetNavigationTree();
                var firstDoc = FindFirstDocument(navTree);
                if (firstDoc?.Href != null)
                {
                    // Return redirect info so client can navigate
                    return Results.Ok(new { redirect = firstDoc.Href });
                }
                return Results.NotFound(new { error = "No documentation available" });
            }

            var document = docsService.GetDocument(path);
            if (document == null)
            {
                return Results.NotFound(new { error = $"Document not found: {path}" });
            }

            var response = new DocsDocumentResponse
            {
                Title = document.Title,
                HtmlContent = document.HtmlContent,
                Breadcrumbs = document.Breadcrumbs.Select(b => new DocsBreadcrumbResponse
                {
                    Text = b.Text,
                    Href = b.Href,
                    Disabled = b.Disabled
                }).ToList()
            };

            return Results.Ok(response);
        });

        return endpoints;
    }

    private static DocsNavItemResponse MapNavItem(DocNavItem item) => new()
    {
        Title = item.Title,
        Href = item.Href,
        IsFolder = item.IsFolder,
        IsExpanded = item.IsExpanded,
        Children = item.Children.Select(MapNavItem).ToList()
    };

    private static DocNavItem? FindFirstDocument(List<DocNavItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsFolder && item.Href != null)
            {
                return item;
            }

            if (item.Children.Count > 0)
            {
                var childDoc = FindFirstDocument(item.Children);
                if (childDoc != null)
                {
                    return childDoc;
                }
            }
        }
        return null;
    }
}
