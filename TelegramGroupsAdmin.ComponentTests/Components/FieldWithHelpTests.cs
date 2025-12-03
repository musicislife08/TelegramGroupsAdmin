using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for FieldWithHelp.razor
/// Tests the wrapper component that adds a help tooltip to any form field.
/// </summary>
[TestFixture]
public class FieldWithHelpTests : MudBlazorTestContext
{
    #region Structure Tests

    [Test]
    public void HasCorrectWrapperStructure()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "Help"));

        // Assert - Should have field-with-help wrapper with flex layout
        var wrapper = cut.Find(".field-with-help");
        Assert.That(wrapper.ClassList, Does.Contain("d-flex"));
        Assert.That(wrapper.ClassList, Does.Contain("align-center"));
        Assert.That(wrapper.ClassList, Does.Contain("gap-2"));
    }

    [Test]
    public void HasFlexGrowContentContainer()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "Help"));

        // Assert - Should have flex-grow-1 container for content
        var contentDiv = cut.Find(".flex-grow-1");
        Assert.That(contentDiv, Is.Not.Null);
    }

    #endregion

    #region ChildContent Tests

    [Test]
    public void RendersChildContent()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "type", "text");
                builder.AddAttribute(2, "class", "test-input");
                builder.CloseElement();
            })));

        // Assert
        var input = cut.Find("input.test-input");
        Assert.That(input, Is.Not.Null);
        Assert.That(input.GetAttribute("type"), Is.EqualTo("text"));
    }

    [Test]
    public void RendersChildContentInFlexContainer()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "id", "child-element");
                builder.AddContent(2, "Child content");
                builder.CloseElement();
            })));

        // Assert - Child should be inside flex-grow-1 container
        var flexContainer = cut.Find(".flex-grow-1");
        Assert.That(flexContainer.InnerHtml, Does.Contain("child-element"));
    }

    [Test]
    public void RendersWithoutChildContent()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "Help"));

        // Assert - Should render wrapper even without child content
        var wrapper = cut.Find(".field-with-help");
        Assert.That(wrapper, Is.Not.Null);
    }

    [Test]
    public void RendersComplexChildContent()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "parent");
                builder.OpenElement(2, "span");
                builder.AddContent(3, "Nested content");
                builder.CloseElement();
                builder.CloseElement();
            })));

        // Assert
        var parent = cut.Find(".parent");
        Assert.That(parent.InnerHtml, Does.Contain("Nested content"));
    }

    #endregion

    #region HelpText Parameter Tests

    [Test]
    public void ShowsHelpTooltip_WhenHelpTextProvided()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "This is help text"));

        // Assert - Should contain HelpTooltip component
        var helpIcon = cut.Find(".help-tooltip-icon");
        Assert.That(helpIcon, Is.Not.Null);
    }

    [Test]
    public void HidesHelpTooltip_WhenHelpTextIsNull()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, null));

        // Assert - Should NOT contain HelpTooltip
        var helpIcons = cut.FindAll(".help-tooltip-icon");
        Assert.That(helpIcons.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesHelpTooltip_WhenHelpTextIsEmpty()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, ""));

        // Assert
        var helpIcons = cut.FindAll(".help-tooltip-icon");
        Assert.That(helpIcons.Count, Is.EqualTo(0));
    }

    [Test]
    public void HidesHelpTooltip_WhenHelpTextIsWhitespace()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "   "));

        // Assert
        var helpIcons = cut.FindAll(".help-tooltip-icon");
        Assert.That(helpIcons.Count, Is.EqualTo(0));
    }

    #endregion

    #region HelpTooltip Styling Tests

    [Test]
    public void HelpTooltipHasBottomMargin()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.HelpText, "Help text"));

        // Assert - HelpTooltip should have margin-bottom style for alignment
        var button = cut.Find("button.help-tooltip-icon");
        var style = button.GetAttribute("style");
        Assert.That(style, Does.Contain("margin-bottom: 20px"));
    }

    #endregion

    #region Combined Tests

    [Test]
    public void RendersChildContentAndHelpTooltip()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "id", "my-field");
                builder.CloseElement();
            }))
            .Add(x => x.HelpText, "Field help text"));

        // Assert - Both should be present
        var input = cut.Find("#my-field");
        Assert.That(input, Is.Not.Null);

        var helpIcon = cut.Find(".help-tooltip-icon");
        Assert.That(helpIcon, Is.Not.Null);
    }

    [Test]
    public void ChildContentComesBeforeHelpTooltip()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>(p => p
            .Add(x => x.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "first-element");
                builder.CloseElement();
            }))
            .Add(x => x.HelpText, "Help"));

        // Assert - flex-grow-1 (content) should come before help icon
        var wrapper = cut.Find(".field-with-help");
        var children = wrapper.Children;

        Assert.That(children[0].ClassList, Does.Contain("flex-grow-1"));
        // Second child is the HelpTooltip's MudTooltip wrapper
    }

    #endregion

    #region Default Value Tests

    [Test]
    public void DefaultHelpTextIsNull()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>();

        // Assert - No help tooltip should be rendered
        var helpIcons = cut.FindAll(".help-tooltip-icon");
        Assert.That(helpIcons.Count, Is.EqualTo(0));
    }

    [Test]
    public void DefaultChildContentIsNull()
    {
        // Arrange & Act
        var cut = Render<FieldWithHelp>();

        // Assert - Should still render wrapper
        var wrapper = cut.Find(".field-with-help");
        Assert.That(wrapper, Is.Not.Null);
    }

    #endregion
}
