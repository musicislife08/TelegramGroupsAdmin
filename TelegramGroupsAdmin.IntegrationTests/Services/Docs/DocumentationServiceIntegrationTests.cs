using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Ui.Server.Services.Docs;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Docs;

/// <summary>
/// Integration tests for DocumentationService using golden test documents.
/// Tests the full document loading and parsing pipeline against known markdown files.
/// </summary>
[TestFixture]
public class DocumentationServiceIntegrationTests
{
    private DocumentationService _service = null!;
    private string _testDocsPath = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _testDocsPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Docs");
        _service = new DocumentationService(NullLogger<DocumentationService>.Instance);
        _service.LoadDocuments(_testDocsPath);
    }

    [Test]
    public void LoadDocuments_WithValidPath_SetsIsInitialized()
    {
        // Assert - service should be initialized after loading docs in OneTimeSetUp
        Assert.That(_service.IsInitialized, Is.True);
    }

    [Test]
    public void GetNavigationTree_ReturnsExpectedStructure()
    {
        // Act
        var navTree = _service.GetNavigationTree();

        // Assert - should have root level items
        Assert.That(navTree, Is.Not.Empty);

        // Should have the getting-started doc at root
        var gettingStarted = navTree.FirstOrDefault(n => n.Title == "Getting Started");
        Assert.That(gettingStarted, Is.Not.Null);
        Assert.That(gettingStarted!.IsFolder, Is.False);
        Assert.That(gettingStarted.Href, Is.EqualTo("/docs/getting-started"));

        // Should have the features folder
        var featuresFolder = navTree.FirstOrDefault(n => n.Title == "Features");
        Assert.That(featuresFolder, Is.Not.Null);
        Assert.That(featuresFolder!.IsFolder, Is.True);
        Assert.That(featuresFolder.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetDocument_WithValidSlug_ReturnsDocument()
    {
        // Act
        var doc = _service.GetDocument("getting-started");

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Title, Is.EqualTo("Getting Started"));
        Assert.That(doc.HtmlContent, Does.Contain("<h1"));
        Assert.That(doc.HtmlContent, Does.Contain("Prerequisites"));
    }

    [Test]
    public void GetDocument_WithNestedSlug_ReturnsDocument()
    {
        // Act
        var doc = _service.GetDocument("features/overview");

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Title, Is.EqualTo("Features Overview"));
        Assert.That(doc.Slug, Is.EqualTo("features/overview"));
    }

    [Test]
    public void GetDocument_WithInvalidSlug_ReturnsNull()
    {
        // Act
        var doc = _service.GetDocument("nonexistent/path");

        // Assert
        Assert.That(doc, Is.Null);
    }

    [Test]
    public void GetDocument_ExtractsH1Title_FromMarkdown()
    {
        // Act
        var doc = _service.GetDocument("features/details");

        // Assert - title should come from the H1 in the markdown
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Title, Is.EqualTo("Feature Details"));
    }

    [Test]
    public void GetDocument_GeneratesBreadcrumbs_ForNestedDoc()
    {
        // Act
        var doc = _service.GetDocument("features/overview");

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Breadcrumbs, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(doc.Breadcrumbs[0].Text, Is.EqualTo("Documentation"));
            Assert.That(doc.Breadcrumbs[1].Text, Is.EqualTo("Features"));
            Assert.That(doc.Breadcrumbs[2].Text, Is.EqualTo("Overview"));
            Assert.That(doc.Breadcrumbs[2].Disabled, Is.True);
        });
    }

    [Test]
    public void GetNavigationTree_OrdersDocumentsByNumericPrefix()
    {
        // Act
        var navTree = _service.GetNavigationTree();
        var featuresFolder = navTree.First(n => n.Title == "Features");

        // Assert - children should be ordered: overview (01) before details (02)
        Assert.That(featuresFolder.Children[0].Title, Is.EqualTo("Features Overview"));
        Assert.That(featuresFolder.Children[1].Title, Is.EqualTo("Feature Details"));
    }
}
