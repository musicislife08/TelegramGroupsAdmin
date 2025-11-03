using TelegramGroupsAdmin.Models.Help;

namespace TelegramGroupsAdmin.Services.Help;

/// <summary>
/// Service for accessing compiled help documentation
/// </summary>
public interface IHelpDocumentService
{
    /// <summary>
    /// Get a help document by its slug (e.g., "spam-detection", "algorithms/similarity")
    /// </summary>
    HelpDocument? GetDocument(string slug);

    /// <summary>
    /// Get all help documents for the index/search
    /// </summary>
    IReadOnlyList<HelpDocument> GetAllDocuments();

    /// <summary>
    /// Search help documents by query (searches title, description, keywords, and content)
    /// </summary>
    IReadOnlyList<HelpDocument> Search(string query);

    /// <summary>
    /// Get documents by category (for grouped navigation)
    /// </summary>
    IReadOnlyList<HelpDocument> GetDocumentsByCategory(string? category);

    /// <summary>
    /// Check if the service has been initialized with documents
    /// </summary>
    bool IsInitialized { get; }
}
