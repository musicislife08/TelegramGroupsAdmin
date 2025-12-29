namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response containing a documentation page's content.
/// </summary>
public class DocsDocumentResponse
{
    /// <summary>
    /// Document title (extracted from first H1 or formatted filename).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Pre-rendered HTML content from markdown.
    /// </summary>
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// Breadcrumb trail for navigation.
    /// </summary>
    public List<DocsBreadcrumbResponse> Breadcrumbs { get; set; } = [];
}

/// <summary>
/// Breadcrumb navigation item for docs pages.
/// </summary>
public class DocsBreadcrumbResponse
{
    /// <summary>
    /// Display text for the breadcrumb.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Link href (null for current/disabled items).
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Whether this breadcrumb is disabled (current page).
    /// </summary>
    public bool Disabled { get; set; }
}
