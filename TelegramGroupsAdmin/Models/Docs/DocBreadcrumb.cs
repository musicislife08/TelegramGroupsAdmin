namespace TelegramGroupsAdmin.Models.Docs;

/// <summary>
/// Breadcrumb navigation item
/// </summary>
public class DocBreadcrumb
{
    public string Text { get; set; } = string.Empty;
    public string? Href { get; set; }
    public bool Disabled { get; set; }
}
