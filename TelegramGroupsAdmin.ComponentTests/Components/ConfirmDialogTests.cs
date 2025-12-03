using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Base context for dialog tests that includes MudDialogProvider.
/// </summary>
public abstract class DialogTestContext : BunitContext
{
    protected IDialogService DialogService { get; private set; } = null!;

    protected DialogTestContext()
    {
        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true);
        JSInterop.SetupVoid("mudPopover.connect", _ => true);
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true);
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }

    /// <summary>
    /// Renders a wrapper component that includes MudDialogProvider.
    /// Must be called before opening dialogs.
    /// </summary>
    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for ConfirmDialog.razor
/// Tests the minimal confirmation dialog with customizable text, button text, and color.
/// </summary>
[TestFixture]
public class ConfirmDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the ConfirmDialog and returns the rendered content.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        string? contentText = null,
        string? buttonText = null,
        Color? color = null)
    {
        var parameters = new DialogParameters<ConfirmDialog>();
        if (contentText != null) parameters.Add(x => x.ContentText, contentText);
        if (buttonText != null) parameters.Add(x => x.ButtonText, buttonText);
        if (color != null) parameters.Add(x => x.Color, color.Value);

        return await DialogService.ShowAsync<ConfirmDialog>("Confirm", parameters);
    }

    #endregion

    #region Content Text Tests

    [Test]
    public void DisplaysContentText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act - Open dialog (don't await to avoid deadlock)
        _ = OpenDialogAsync(contentText: "Delete this item?");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Delete this item?"));
        });
    }

    [Test]
    public void DisplaysDefaultContentText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Are you sure?"));
        });
    }

    #endregion

    #region Button Text Tests

    [Test]
    public void DisplaysCustomButtonText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(buttonText: "Delete");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Delete"));
        });
    }

    [Test]
    public void DisplaysDefaultButtonText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Confirm"));
        });
    }

    [Test]
    public void DisplaysCancelButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });
    }

    #endregion

    #region Color Tests

    [Test]
    public void AppliesErrorColor()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(color: Color.Error);

        // Assert - MudBlazor uses mud-button-filled-error for filled buttons
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-button-filled-error"));
        });
    }

    [Test]
    public void AppliesDefaultPrimaryColor()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - MudBlazor uses mud-button-filled-primary for filled buttons
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-button-filled-primary"));
        });
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-content"));
        });
    }

    [Test]
    public void HasDialogActions()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-actions"));
        });
    }

    [Test]
    public void HasTwoButtons()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            var buttons = provider.FindAll("button.mud-button-root");
            Assert.That(buttons.Count, Is.EqualTo(2));
        });
    }

    #endregion

    #region Button Click Tests

    [Test]
    public void CancelButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });

        // Act - Click cancel button
        var cancelButton = provider.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    [Test]
    public void ConfirmButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Confirm"));
        });

        // Act - Click confirm button
        var confirmButton = provider.FindAll("button").First(b => b.TextContent.Contains("Confirm"));
        confirmButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    // Note: Testing the actual dialog result (Canceled vs Ok) requires awaiting
    // dialogRef.Result which causes deadlocks in bUnit. The dialog close behavior
    // is verified by checking markup changes. Full result verification is better
    // suited for Playwright E2E tests.

    #endregion
}
