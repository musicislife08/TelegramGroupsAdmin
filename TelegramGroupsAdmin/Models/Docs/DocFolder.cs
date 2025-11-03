namespace TelegramGroupsAdmin.Models.Docs;

/// <summary>
/// Represents a folder in the documentation hierarchy (recursive tree structure)
/// </summary>
public class DocFolder
{
    /// <summary>
    /// Folder name (display name, numeric prefix stripped)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Numeric order from folder name prefix (01- = 1, 02- = 2)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// URL path segment for this folder
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Documents directly in this folder
    /// </summary>
    public List<DocDocument> Documents { get; set; } = new();

    /// <summary>
    /// Subfolders (recursive)
    /// </summary>
    public List<DocFolder> Subfolders { get; set; } = new();
}
