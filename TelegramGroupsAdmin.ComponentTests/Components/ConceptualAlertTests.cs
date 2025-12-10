using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ConceptualAlert.razor
/// Tests title rendering, child content, severity, and custom styling.
/// </summary>
[TestFixture]
public class ConceptualAlertTests : MudBlazorTestContext
{
    #region Title Tests

    [Test]
    public void DisplaysTitle_WhenProvided()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Title, "Important Information")
            .AddChildContent("Alert content here"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Important Information"));
        Assert.That(cut.Markup, Does.Contain("<strong>"));
    }

    [Test]
    public void HidesTitle_WhenNull()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Title, null)
            .AddChildContent("Alert content here"));

        // Assert - no strong/title element for null title
        // The component wraps title in <strong>, so check there's no dangling strong tags
        Assert.That(cut.Markup, Does.Not.Contain("<strong>"));
    }

    [Test]
    public void HidesTitle_WhenEmpty()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Title, "")
            .AddChildContent("Alert content here"));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("<strong>"));
    }

    [Test]
    public void HidesTitle_WhenWhitespace()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Title, "   ")
            .AddChildContent("Alert content here"));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("<strong>"));
    }

    #endregion

    #region Child Content Tests

    [Test]
    public void DisplaysChildContent()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .AddChildContent("This is the alert message"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("This is the alert message"));
    }

    [Test]
    public void DisplaysHtmlChildContent()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .AddChildContent("<em>Emphasized</em> text"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("<em>Emphasized</em>"));
    }

    #endregion

    #region Severity Tests

    [Test]
    public void UsesInfoSeverity_ByDefault()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .AddChildContent("Alert content"));

        // Assert - MudAlert with Info severity has specific classes
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Contain("mud-alert-text-info"));
    }

    [Test]
    [TestCase(Severity.Success, "mud-alert-text-success")]
    [TestCase(Severity.Warning, "mud-alert-text-warning")]
    [TestCase(Severity.Error, "mud-alert-text-error")]
    [TestCase(Severity.Info, "mud-alert-text-info")]
    public void AppliesSeverityClass(Severity severity, string expectedClass)
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Severity, severity)
            .AddChildContent("Alert content"));

        // Assert
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Contain(expectedClass));
    }

    #endregion

    #region Styling Tests

    [Test]
    public void AppliesCustomClass()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Class, "my-custom-class")
            .AddChildContent("Alert content"));

        // Assert
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Contain("my-custom-class"));
    }

    [Test]
    public void AppliesConceptualAlertClass()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .AddChildContent("Alert content"));

        // Assert
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Contain("conceptual-alert"));
    }

    [Test]
    public void AppliesCustomStyle()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Style, "margin-top: 20px;")
            .AddChildContent("Alert content"));

        // Assert
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.GetAttribute("style"), Does.Contain("margin-top: 20px"));
    }

    [Test]
    public void AppliesDenseMode()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .Add(x => x.Dense, true)
            .AddChildContent("Alert content"));

        // Assert - MudBlazor 8.x uses "mud-dense" class for dense mode
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Contain("mud-dense"));
    }

    [Test]
    public void NotDense_ByDefault()
    {
        // Arrange & Act
        var cut = Render<ConceptualAlert>(p => p
            .AddChildContent("Alert content"));

        // Assert
        var alert = cut.Find(".mud-alert");
        Assert.That(alert.ClassList, Does.Not.Contain("mud-dense"));
    }

    #endregion
}
