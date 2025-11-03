namespace TelegramGroupsAdmin.Models.Help;

/// <summary>
/// Represents a compiled help documentation page with metadata and rendered HTML content
/// </summary>
public class HelpDocument
{
    /// <summary>
    /// URL-friendly slug used for routing (e.g., "spam-detection", "algorithms/similarity")
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Metadata extracted from YAML front-matter
    /// </summary>
    public HelpMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Pre-rendered HTML content from markdown (cached at startup)
    /// </summary>
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// Original markdown source (for debugging/editing)
    /// </summary>
    public string MarkdownSource { get; set; } = string.Empty;

    /// <summary>
    /// File path relative to Help directory (e.g., "spam-detection.md", "algorithms/similarity.md")
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Breadcrumb trail for navigation (generated from folder structure)
    /// </summary>
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
}

/// <summary>
/// Breadcrumb navigation item
/// </summary>
public class BreadcrumbItem
{
    public string Text { get; set; } = string.Empty;
    public string? Href { get; set; }
    public bool Disabled { get; set; }
}
