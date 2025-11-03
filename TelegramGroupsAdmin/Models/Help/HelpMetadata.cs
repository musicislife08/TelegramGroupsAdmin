namespace TelegramGroupsAdmin.Models.Help;

/// <summary>
/// Metadata extracted from YAML front-matter in markdown help documents
/// </summary>
public class HelpMetadata
{
    /// <summary>
    /// Document title (used in navigation and page header)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Short description for search and cards
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// MudBlazor icon name (e.g., Icons.Material.Filled.Shield)
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// MudBlazor color name (e.g., Primary, Success, Warning)
    /// </summary>
    public string Color { get; set; } = "Primary";

    /// <summary>
    /// Space-separated keywords for search functionality
    /// </summary>
    public string SearchKeywords { get; set; } = string.Empty;

    /// <summary>
    /// Display order in navigation (lower = earlier)
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// Parent category for nested navigation (e.g., "algorithms", "configuration")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Whether this document should appear in the main help index
    /// </summary>
    public bool ShowInIndex { get; set; } = true;
}
