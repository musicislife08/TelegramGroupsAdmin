namespace TelegramGroupsAdmin.Models.Docs;

/// <summary>
/// Flattened navigation item for rendering side nav (built from DocFolder tree)
/// </summary>
public class DocNavItem
{
    /// <summary>
    /// Display title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Link href (null = folder header, not clickable)
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Nesting level for indentation (0 = root, 1 = first level, etc.)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Whether this nav group is expanded (for folders)
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Child navigation items (for nested rendering)
    /// </summary>
    public List<DocNavItem> Children { get; set; } = new();

    /// <summary>
    /// Is this a folder (has children) or a document (leaf node)
    /// </summary>
    public bool IsFolder { get; set; }
}
