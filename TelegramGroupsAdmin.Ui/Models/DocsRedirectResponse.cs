namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response indicating a redirect to a documentation page.
/// Used when navigating to /docs without a path to redirect to the first document.
/// </summary>
public class DocsRedirectResponse
{
    /// <summary>
    /// The path to redirect to (e.g., "/docs/getting-started").
    /// </summary>
    public required string RedirectPath { get; init; }
}
