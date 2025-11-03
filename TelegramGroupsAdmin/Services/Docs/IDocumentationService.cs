using TelegramGroupsAdmin.Models.Docs;

namespace TelegramGroupsAdmin.Services.Docs;

/// <summary>
/// Service for accessing folder-based documentation (portable markdown files)
/// </summary>
public interface IDocumentationService
{
    /// <summary>
    /// Get a documentation page by its path (e.g., "algorithms/similarity")
    /// </summary>
    DocDocument? GetDocument(string path);

    /// <summary>
    /// Get the navigation tree for rendering side nav
    /// </summary>
    List<DocNavItem> GetNavigationTree();

    /// <summary>
    /// Check if the service has been initialized with documents
    /// </summary>
    bool IsInitialized { get; }
}
