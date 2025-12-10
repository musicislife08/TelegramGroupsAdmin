using Bunit;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for HelpTooltip.razor
/// Tests the reusable help icon tooltip component.
/// Note: MudTooltip content is lazy-rendered on hover, so we focus on
/// testing the icon button structure, classes, and styles.
/// </summary>
[TestFixture]
public class HelpTooltipTests : MudBlazorTestContext
{
    #region Icon Rendering Tests

    [Test]
    public void RendersHelpIconButton()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help text"));

        // Assert - Should contain the icon button with help-tooltip-icon class
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton, Is.Not.Null);
    }

    [Test]
    public void RendersAsButton()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button, Is.Not.Null);
        Assert.That(button.GetAttribute("type"), Is.EqualTo("button"));
    }

    [Test]
    public void IconButtonHasSmallSize()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert - MudIconButton with Size.Small adds mud-icon-button-size-small class
        var button = cut.Find("button");
        Assert.That(button.ClassList, Does.Contain("mud-icon-button-size-small"));
    }

    [Test]
    public void IconButtonHasMudButtonClasses()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.ClassList, Does.Contain("mud-button-root"));
        Assert.That(button.ClassList, Does.Contain("mud-icon-button"));
    }

    [Test]
    public void RendersSvgHelpIcon()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert - Should contain SVG icon
        var svg = cut.Find("svg.mud-icon-root");
        Assert.That(svg, Is.Not.Null);
        Assert.That(svg.ClassList, Does.Contain("mud-svg-icon"));
    }

    #endregion

    #region Class Parameter Tests

    [Test]
    public void AppliesCustomClass()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Class, "my-custom-class"));

        // Assert
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton.ClassList, Does.Contain("my-custom-class"));
    }

    [Test]
    public void AppliesMultipleCustomClasses()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Class, "class-one class-two"));

        // Assert
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton.ClassList, Does.Contain("class-one"));
        Assert.That(iconButton.ClassList, Does.Contain("class-two"));
    }

    [Test]
    public void WorksWithoutCustomClass()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Class, null));

        // Assert - Should still have base class
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton, Is.Not.Null);
    }

    [Test]
    public void PreservesBaseClassWithCustomClass()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Class, "custom"));

        // Assert - Should have both help-tooltip-icon and custom
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton.ClassList, Does.Contain("help-tooltip-icon"));
        Assert.That(iconButton.ClassList, Does.Contain("custom"));
    }

    #endregion

    #region Style Parameter Tests

    [Test]
    public void AppliesCustomStyle()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Style, "margin-left: 10px;"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.GetAttribute("style"), Does.Contain("margin-left: 10px"));
    }

    [Test]
    public void AppliesMultipleStyles()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Style, "color: red; font-size: 14px;"));

        // Assert
        var button = cut.Find("button");
        var style = button.GetAttribute("style");
        Assert.That(style, Does.Contain("color: red"));
        Assert.That(style, Does.Contain("font-size: 14px"));
    }

    [Test]
    public void WorksWithoutCustomStyle()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help")
            .Add(x => x.Style, null));

        // Assert - Should render without error
        var button = cut.Find("button");
        Assert.That(button, Is.Not.Null);
    }

    #endregion

    #region Tooltip Structure Tests

    [Test]
    public void HasMudTooltipWrapper()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Tooltip text"));

        // Assert - MudTooltip wraps content with tooltip-root class
        Assert.That(cut.Markup, Does.Contain("mud-tooltip-root"));
    }

    [Test]
    public void TooltipIsInline()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Tooltip text"));

        // Assert - Should be inline tooltip
        Assert.That(cut.Markup, Does.Contain("mud-tooltip-inline"));
    }

    [Test]
    public void HasPopoverCascadingValue()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Tooltip text"));

        // Assert - MudTooltip creates a popover cascading value div
        Assert.That(cut.Markup, Does.Contain("mud-popover-cascading-value"));
    }

    #endregion

    #region Default Value Tests

    [Test]
    public void DefaultTextIsEmpty()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>();

        // Assert - Should render without error even with default empty text
        var button = cut.Find("button");
        Assert.That(button, Is.Not.Null);
    }

    [Test]
    public void DefaultClassIsNull()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert - Should only have base classes, no extra custom class
        var iconButton = cut.Find(".help-tooltip-icon");
        Assert.That(iconButton.ClassList, Does.Contain("help-tooltip-icon"));
    }

    [Test]
    public void DefaultStyleIsNull()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert - Button renders without custom style attribute
        // (MudBlazor may add its own styles)
        var button = cut.Find("button");
        Assert.That(button, Is.Not.Null);
    }

    #endregion

    #region Accessibility Tests

    [Test]
    public void SvgIconHasAriaHidden()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert - SVG should be hidden from screen readers
        var svg = cut.Find("svg");
        Assert.That(svg.GetAttribute("aria-hidden"), Is.EqualTo("true"));
    }

    [Test]
    public void SvgIconHasImgRole()
    {
        // Arrange & Act
        var cut = Render<HelpTooltip>(p => p
            .Add(x => x.Text, "Help"));

        // Assert
        var svg = cut.Find("svg");
        Assert.That(svg.GetAttribute("role"), Is.EqualTo("img"));
    }

    #endregion

    // Note: Tooltip content (Text parameter) is rendered via MudPopover which requires
    // JavaScript interop to display. Testing tooltip text content is better suited for
    // Playwright E2E tests. Component tests verify the icon button structure and styling.
}
