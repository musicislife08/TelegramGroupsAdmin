using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Ui.Server.Services.Docs;

namespace TelegramGroupsAdmin.UnitTests.Services.Docs;

/// <summary>
/// Unit tests for DocumentationService pure logic methods.
/// Tests the string parsing and formatting helpers that don't require file I/O.
/// </summary>
[TestFixture]
public class DocumentationServiceTests
{
    private DocumentationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new DocumentationService(NullLogger<DocumentationService>.Instance);
    }

    #region ParseNumericPrefix Tests

    [Test]
    public void ParseNumericPrefix_WithPrefix_ReturnsOrderAndDisplayName()
    {
        // Arrange & Act
        var (order, displayName) = _service.ParseNumericPrefix("01-getting-started");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(1));
            Assert.That(displayName, Is.EqualTo("Getting Started"));
        });
    }

    [Test]
    public void ParseNumericPrefix_WithoutPrefix_ReturnsMaxIntAndDisplayName()
    {
        // Arrange & Act
        var (order, displayName) = _service.ParseNumericPrefix("getting-started");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(int.MaxValue));
            Assert.That(displayName, Is.EqualTo("Getting Started"));
        });
    }

    [Test]
    public void ParseNumericPrefix_WithMultiDigitPrefix_ParsesCorrectly()
    {
        // Arrange & Act
        var (order, displayName) = _service.ParseNumericPrefix("99-advanced-topics");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(99));
            Assert.That(displayName, Is.EqualTo("Advanced Topics"));
        });
    }

    [Test]
    public void ParseNumericPrefix_WithUnderscores_FormatsCorrectly()
    {
        // Arrange & Act
        var (order, displayName) = _service.ParseNumericPrefix("02-spam_detection");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(2));
            Assert.That(displayName, Is.EqualTo("Spam Detection"));
        });
    }

    #endregion

    #region FormatDisplayName Tests

    [Test]
    public void FormatDisplayName_WithDashes_ConvertsToPascalCase()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("spam-detection");

        // Assert
        Assert.That(result, Is.EqualTo("Spam Detection"));
    }

    [Test]
    public void FormatDisplayName_WithUnderscores_ConvertsToPascalCase()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("spam_detection");

        // Assert
        Assert.That(result, Is.EqualTo("Spam Detection"));
    }

    [Test]
    public void FormatDisplayName_WithMixedSeparators_HandlesCorrectly()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("spam-detection_settings");

        // Assert
        Assert.That(result, Is.EqualTo("Spam Detection Settings"));
    }

    [Test]
    public void FormatDisplayName_SingleWord_CapitalizesFirst()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("overview");

        // Assert
        Assert.That(result, Is.EqualTo("Overview"));
    }

    #endregion

    #region Slugify Tests

    [Test]
    public void Slugify_WithNumericPrefix_RemovesPrefixAndLowercases()
    {
        // Arrange & Act
        var result = _service.Slugify("01-Getting-Started");

        // Assert
        Assert.That(result, Is.EqualTo("getting-started"));
    }

    [Test]
    public void Slugify_WithUnderscores_ConvertsToHyphens()
    {
        // Arrange & Act
        var result = _service.Slugify("spam_detection");

        // Assert
        Assert.That(result, Is.EqualTo("spam-detection"));
    }

    [Test]
    public void Slugify_WithUppercase_Lowercases()
    {
        // Arrange & Act
        var result = _service.Slugify("SpamDetection");

        // Assert
        Assert.That(result, Is.EqualTo("spamdetection"));
    }

    [Test]
    public void Slugify_WithoutPrefix_JustLowercasesAndConverts()
    {
        // Arrange & Act
        var result = _service.Slugify("Advanced_Topics");

        // Assert
        Assert.That(result, Is.EqualTo("advanced-topics"));
    }

    #endregion

    #region GenerateBreadcrumbs Tests

    [Test]
    public void GenerateBreadcrumbs_WithRootFile_ReturnsDocsAndFile()
    {
        // Arrange & Act
        var breadcrumbs = _service.GenerateBreadcrumbs("01-getting-started.md");

        // Assert
        Assert.That(breadcrumbs, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            // First: Documentation root
            Assert.That(breadcrumbs[0].Text, Is.EqualTo("Documentation"));
            Assert.That(breadcrumbs[0].Href, Is.EqualTo("/docs"));
            Assert.That(breadcrumbs[0].Disabled, Is.False);

            // Last: Current page (disabled)
            Assert.That(breadcrumbs[1].Text, Is.EqualTo("Getting Started"));
            Assert.That(breadcrumbs[1].Href, Is.Null);
            Assert.That(breadcrumbs[1].Disabled, Is.True);
        });
    }

    [Test]
    public void GenerateBreadcrumbs_WithNestedPath_ReturnsCorrectHierarchy()
    {
        // Arrange & Act
        var breadcrumbs = _service.GenerateBreadcrumbs("01-algorithms/02-similarity.md");

        // Assert
        Assert.That(breadcrumbs, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            // First: Documentation root
            Assert.That(breadcrumbs[0].Text, Is.EqualTo("Documentation"));
            Assert.That(breadcrumbs[0].Href, Is.EqualTo("/docs"));

            // Middle: Folder
            Assert.That(breadcrumbs[1].Text, Is.EqualTo("Algorithms"));
            Assert.That(breadcrumbs[1].Href, Is.EqualTo("/docs/algorithms"));
            Assert.That(breadcrumbs[1].Disabled, Is.False);

            // Last: Current page (disabled)
            Assert.That(breadcrumbs[2].Text, Is.EqualTo("Similarity"));
            Assert.That(breadcrumbs[2].Disabled, Is.True);
        });
    }

    [Test]
    public void GenerateBreadcrumbs_WithDeeplyNestedPath_BuildsFullHierarchy()
    {
        // Arrange & Act
        var breadcrumbs = _service.GenerateBreadcrumbs("01-section/02-subsection/03-page.md");

        // Assert
        Assert.That(breadcrumbs, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(breadcrumbs[0].Text, Is.EqualTo("Documentation"));
            Assert.That(breadcrumbs[1].Text, Is.EqualTo("Section"));
            Assert.That(breadcrumbs[2].Text, Is.EqualTo("Subsection"));
            Assert.That(breadcrumbs[3].Text, Is.EqualTo("Page"));
        });
    }

    [Test]
    public void GenerateBreadcrumbs_LastItem_IsDisabledWithNoHref()
    {
        // Arrange & Act
        var breadcrumbs = _service.GenerateBreadcrumbs("folder/file.md");

        // Assert
        var lastBreadcrumb = breadcrumbs[^1];
        Assert.Multiple(() =>
        {
            Assert.That(lastBreadcrumb.Disabled, Is.True);
            Assert.That(lastBreadcrumb.Href, Is.Null);
        });
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void FormatDisplayName_SingleCharWord_HandlesCorrectly()
    {
        // Arrange & Act - this was a bug where word[1..] threw on single-char words
        var result = _service.FormatDisplayName("a-test");

        // Assert
        Assert.That(result, Is.EqualTo("A Test"));
    }

    [Test]
    public void FormatDisplayName_AllSingleCharWords_HandlesCorrectly()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("a-b-c");

        // Assert
        Assert.That(result, Is.EqualTo("A B C"));
    }

    [Test]
    public void FormatDisplayName_EmptyString_ReturnsEmpty()
    {
        // Arrange & Act
        var result = _service.FormatDisplayName("");

        // Assert
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void Slugify_EmptyString_ReturnsEmpty()
    {
        // Arrange & Act
        var result = _service.Slugify("");

        // Assert
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void ParseNumericPrefix_EmptyString_ReturnsMaxIntAndEmpty()
    {
        // Arrange & Act
        var (order, displayName) = _service.ParseNumericPrefix("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(int.MaxValue));
            Assert.That(displayName, Is.EqualTo(""));
        });
    }

    [Test]
    public void GenerateBreadcrumbs_WithBackslashes_NormalizesToForwardSlashes()
    {
        // Arrange & Act - Windows paths use backslashes
        var breadcrumbs = _service.GenerateBreadcrumbs("folder\\subfolder\\file.md");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(breadcrumbs, Has.Count.EqualTo(4)); // Docs, folder, subfolder, file
            Assert.That(breadcrumbs[1].Href, Is.EqualTo("/docs/folder"));
            Assert.That(breadcrumbs[2].Href, Is.EqualTo("/docs/folder/subfolder"));
        });
    }

    #endregion
}
