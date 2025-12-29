namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Navigation item for docs sidebar (recursive tree structure).
/// </summary>
public class DocsNavItemResponse
{
    /// <summary>
    /// Display title for the navigation item.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Link href (null for folders, which are not clickable).
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Whether this is a folder (has children) or a document (leaf node).
    /// </summary>
    public bool IsFolder { get; set; }

    /// <summary>
    /// Whether this nav group is expanded (for folders).
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Child navigation items for nested rendering.
    /// </summary>
    public List<DocsNavItemResponse> Children { get; set; } = [];
}
