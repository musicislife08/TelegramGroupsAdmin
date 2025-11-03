namespace TelegramGroupsAdmin.Models.Docs;

/// <summary>
/// Represents a single documentation page compiled from a markdown file
/// </summary>
public class DocDocument
{
    /// <summary>
    /// URL path (e.g., "algorithms/similarity")
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Document title extracted from first H1 or formatted filename
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Pre-rendered HTML content from markdown (cached at startup)
    /// </summary>
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// File path relative to Docs directory (e.g., "algorithms/similarity.md")
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Breadcrumb trail for navigation
    /// </summary>
    public List<DocBreadcrumb> Breadcrumbs { get; set; } = new();

    /// <summary>
    /// Numeric order from filename prefix (01- = 1, 02- = 2)
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Breadcrumb navigation item
/// </summary>
public class DocBreadcrumb
{
    public string Text { get; set; } = string.Empty;
    public string? Href { get; set; }
    public bool Disabled { get; set; }
}
