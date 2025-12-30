using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using TelegramGroupsAdmin.Ui.Server.Models.Docs;

namespace TelegramGroupsAdmin.Ui.Server.Services.Docs;

/// <summary>
/// In-memory cache of folder-based documentation loaded at startup
/// </summary>
public partial class DocumentationService : IDocumentationService
{
    private readonly ConcurrentDictionary<string, DocDocument> _documents = new();
    private readonly MarkdownPipeline _markdownPipeline;
    private readonly ILogger<DocumentationService> _logger;
    private DocFolder? _rootFolder;

    public bool IsInitialized => _documents.Count > 0;

    [GeneratedRegex(@"^(\d+)-(.+)$")]
    private static partial Regex NumericPrefixRegex();

    public DocumentationService(ILogger<DocumentationService> logger)
    {
        _logger = logger;

        // Reuse same Markdig pipeline as Help system
        // Note: UseGenericAttributes() intentionally omitted - it allows XSS via markdown attribute injection
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers()
            .UseDiagrams() // Mermaid diagram support (renders to <div class="mermaid">)
            .Build();
    }

    /// <summary>
    /// Load and compile all markdown files from the Docs directory (called by startup service)
    /// </summary>
    public void LoadDocuments(string docsDirectoryPath)
    {
        if (!Directory.Exists(docsDirectoryPath))
        {
            _logger.LogWarning("Docs directory not found: {Path}", docsDirectoryPath);
            return;
        }

        _logger.LogInformation("Loading documentation from: {Path}", docsDirectoryPath);

        // Build folder tree recursively
        _rootFolder = LoadFolder(docsDirectoryPath, docsDirectoryPath, string.Empty);

        // Flatten to dictionary for fast lookup
        FlattenFolderToDocuments(_rootFolder, string.Empty);

        _logger.LogInformation("Successfully loaded {Count} documentation pages", _documents.Count);
    }

    private DocFolder LoadFolder(string folderPath, string baseDirectory, string parentSlug)
    {
        var folderInfo = new DirectoryInfo(folderPath);
        var (order, displayName) = ParseNumericPrefix(folderInfo.Name);

        // Root folder should have empty slug (avoid /docs/docs/... paths)
        var isRootFolder = folderPath == baseDirectory;
        var slug = isRootFolder
            ? string.Empty
            : string.IsNullOrEmpty(parentSlug)
                ? Slugify(folderInfo.Name)
                : $"{parentSlug}/{Slugify(folderInfo.Name)}";

        var folder = new DocFolder
        {
            Name = displayName,
            Order = order,
            Slug = slug
        };

        // Load markdown files in this folder
        var markdownFiles = Directory.GetFiles(folderPath, "*.md");
        foreach (var filePath in markdownFiles)
        {
            try
            {
                var document = CompileDocument(filePath, baseDirectory, slug);
                folder.Documents.Add(document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compile documentation file: {Path}", filePath);
            }
        }

        // Sort documents by order
        folder.Documents = folder.Documents.OrderBy(d => d.Order).ThenBy(d => d.Title).ToList();

        // Load subfolders recursively
        var subfolders = Directory.GetDirectories(folderPath);
        foreach (var subfolderPath in subfolders)
        {
            var subfolder = LoadFolder(subfolderPath, baseDirectory, slug);
            folder.Subfolders.Add(subfolder);
        }

        // Sort subfolders by order
        folder.Subfolders = folder.Subfolders.OrderBy(f => f.Order).ThenBy(f => f.Name).ToList();

        return folder;
    }

    private DocDocument CompileDocument(string filePath, string baseDirectory, string parentSlug)
    {
        var markdown = File.ReadAllText(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var (order, displayName) = ParseNumericPrefix(fileName);
        var slug = string.IsNullOrEmpty(parentSlug)
            ? Slugify(fileName)
            : $"{parentSlug}/{Slugify(fileName)}";

        // Render markdown to HTML
        var html = Markdig.Markdown.ToHtml(markdown, _markdownPipeline);

        // Extract title from first H1 or use formatted filename
        var title = ExtractTitleFromMarkdown(markdown) ?? displayName;

        // Generate breadcrumbs from path
        var relativePath = Path.GetRelativePath(baseDirectory, filePath);
        var breadcrumbs = GenerateBreadcrumbs(relativePath);

        return new DocDocument
        {
            Slug = slug,
            Title = title,
            HtmlContent = html,
            FilePath = relativePath,
            Breadcrumbs = breadcrumbs,
            Order = order
        };
    }

    private string? ExtractTitleFromMarkdown(string markdown)
    {
        try
        {
            var document = Markdig.Markdown.Parse(markdown, _markdownPipeline);
            var firstHeading = document.Descendants<HeadingBlock>()
                .FirstOrDefault(h => h.Level == 1);

            if (firstHeading != null)
            {
                return firstHeading.Inline?.FirstChild?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract H1 title from markdown");
        }

        return null;
    }

    internal (int order, string displayName) ParseNumericPrefix(string name)
    {
        var match = NumericPrefixRegex().Match(name);
        if (match.Success)
        {
            var order = int.Parse(match.Groups[1].Value);
            var namePart = match.Groups[2].Value;
            var displayName = FormatDisplayName(namePart);
            return (order, displayName);
        }

        return (int.MaxValue, FormatDisplayName(name));
    }

    internal string FormatDisplayName(string name)
    {
        // Convert "spam-detection" or "spam_detection" to "Spam Detection"
        return string.Join(" ", name.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 1
                ? char.ToUpper(word[0]).ToString()
                : char.ToUpper(word[0]) + word[1..]));
    }

    internal string Slugify(string name)
    {
        // Remove numeric prefix and convert to lowercase slug
        var match = NumericPrefixRegex().Match(name);
        var cleanName = match.Success ? match.Groups[2].Value : name;
        return cleanName.ToLowerInvariant().Replace('_', '-');
    }

    internal List<DocBreadcrumb> GenerateBreadcrumbs(string relativePath)
    {
        var breadcrumbs = new List<DocBreadcrumb>
        {
            new() { Text = "Documentation", Href = "/docs", Disabled = false }
        };

        var parts = relativePath.Replace('\\', '/').Split('/');
        var currentPath = "";

        for (var i = 0; i < parts.Length - 1; i++) // Skip the filename itself
        {
            var part = parts[i];
            var (_, displayName) = ParseNumericPrefix(part);
            currentPath = string.IsNullOrEmpty(currentPath)
                ? Slugify(part)
                : $"{currentPath}/{Slugify(part)}";

            breadcrumbs.Add(new DocBreadcrumb
            {
                Text = displayName,
                Href = $"/docs/{currentPath}",
                Disabled = false
            });
        }

        // Add current page (disabled, no link)
        var filename = Path.GetFileNameWithoutExtension(parts[^1]);
        var (_, fileDisplayName) = ParseNumericPrefix(filename);
        breadcrumbs.Add(new DocBreadcrumb
        {
            Text = fileDisplayName,
            Href = null,
            Disabled = true
        });

        return breadcrumbs;
    }

    private void FlattenFolderToDocuments(DocFolder folder, string parentPath)
    {
        foreach (var document in folder.Documents)
        {
            _documents[document.Slug] = document;
        }

        foreach (var subfolder in folder.Subfolders)
        {
            FlattenFolderToDocuments(subfolder, folder.Slug);
        }
    }

    public DocDocument? GetDocument(string path)
    {
        _documents.TryGetValue(path.ToLowerInvariant(), out var document);
        return document;
    }

    public List<DocNavItem> GetNavigationTree()
    {
        if (_rootFolder == null)
        {
            return new List<DocNavItem>();
        }

        return BuildNavItems(_rootFolder, 0);
    }

    private List<DocNavItem> BuildNavItems(DocFolder folder, int level)
    {
        var items = new List<DocNavItem>();

        // Add documents at this level
        foreach (var document in folder.Documents)
        {
            items.Add(new DocNavItem
            {
                Title = document.Title,
                Href = $"/docs/{document.Slug}",
                Level = level,
                IsFolder = false
            });
        }

        // Add subfolders
        foreach (var subfolder in folder.Subfolders)
        {
            var folderItem = new DocNavItem
            {
                Title = subfolder.Name,
                Href = null, // Folders are not clickable, just headers
                Level = level,
                IsFolder = true,
                IsExpanded = true,
                Children = BuildNavItems(subfolder, level + 1)
            };

            items.Add(folderItem);
        }

        return items;
    }
}
