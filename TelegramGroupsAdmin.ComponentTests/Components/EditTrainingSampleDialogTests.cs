using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for EditTrainingSampleDialog.razor
/// Tests the dialog for editing training samples in the Bayes classifier.
/// </summary>
[TestFixture]
public class EditTrainingSampleDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a test DetectionResultRecord with the specified properties.
    /// </summary>
    private static DetectionResultRecord CreateTestSample(
        long id = 1,
        string messageText = "Test message",
        bool isSpam = true,
        string detectionSource = "manual",
        int confidence = 95,
        DateTimeOffset? detectedAt = null,
        string addedByEmail = "admin@test.com")
    {
        return new DetectionResultRecord
        {
            Id = id,
            MessageId = 12345,
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            DetectionSource = detectionSource,
            DetectionMethod = "BayesClassifier",
            IsSpam = isSpam,
            Confidence = confidence,
            AddedBy = Actor.FromWebUser("user-123", addedByEmail),
            UserId = 67890,
            MessageText = messageText,
            NetConfidence = isSpam ? confidence : -confidence,
            UsedForTraining = true
        };
    }

    /// <summary>
    /// Opens the EditTrainingSampleDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(DetectionResultRecord sample)
    {
        var parameters = new DialogParameters<EditTrainingSampleDialog>
        {
            { x => x.Sample, sample }
        };

        return await DialogService.ShowAsync<EditTrainingSampleDialog>("Edit Training Sample", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

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
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

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
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
            Assert.That(provider.Markup, Does.Contain("Update"));
        });
    }

    #endregion

    #region Message Text Field Tests

    [Test]
    public void HasMessageTextField()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Message Text"));
        });
    }

    [Test]
    public void DisplaysMessageTextHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("The message text that will be used for training the Bayes classifier"));
        });
    }

    #endregion

    #region Classification Radio Tests

    [Test]
    public void DisplaysClassificationSection()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Classification:"));
        });
    }

    [Test]
    public void DisplaysSpamOption()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("SPAM - This message is spam/unwanted"));
        });
    }

    [Test]
    public void DisplaysHamOption()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("HAM - This message is legitimate/wanted"));
        });
    }

    [Test]
    public void HasRadioGroup()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-radio-group"));
        });
    }

    #endregion

    #region Source Select Tests

    [Test]
    public void HasSourceSelect()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Source"));
            Assert.That(provider.Markup, Does.Contain("mud-select"));
        });
    }

    // Note: MudSelect items are rendered via popover which requires JS interop.
    // Testing specific select options is better suited for Playwright E2E tests.

    #endregion

    #region Confidence Field Tests

    [Test]
    public void HasConfidenceField()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Confidence"));
        });
    }

    [Test]
    public void DisplaysConfidenceHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Confidence level when this sample was added"));
        });
    }

    #endregion

    #region Metadata Display Tests

    [Test]
    public void DisplaysAddedBy()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample(addedByEmail: "moderator@example.com");

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("By:"));
            Assert.That(provider.Markup, Does.Contain("moderator@example.com"));
        });
    }

    [Test]
    public void DisplaysOriginalType_Spam()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample(isSpam: true);

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Original Type:"));
            Assert.That(provider.Markup, Does.Contain("SPAM"));
        });
    }

    [Test]
    public void DisplaysOriginalType_Ham()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample(isSpam: false);

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Original Type:"));
            Assert.That(provider.Markup, Does.Contain("HAM"));
        });
    }

    [Test]
    public void DisplaysAddedDate()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Added:"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void UpdateButtonHasPrimaryColor()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();

        // Act
        _ = OpenDialogAsync(sample);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-button-filled-primary"));
        });
    }

    [Test]
    public void CancelButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var sample = CreateTestSample();
        _ = OpenDialogAsync(sample);

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });

        // Act - Click cancel button
        var cancelButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");
        cancelButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
