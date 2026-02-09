using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for DetectionHistoryDialog.razor
/// Tests the dialog that displays spam detection history for a message.
/// </summary>
[TestFixture]
public class DetectionHistoryDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a test MessageRecord with default values.
    /// </summary>
    private static MessageRecord CreateTestMessage(
        long messageId = 12345,
        long userId = 67890,
        string messageText = "Test message")
    {
        return new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(userId, "Test", "User", "testuser"),
            Chat: new ChatIdentity(111222, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow.AddHours(-1),
            MessageText: messageText,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );
    }

    /// <summary>
    /// Creates a test DetectionResultRecord with the specified properties.
    /// </summary>
    private static DetectionResultRecord CreateTestResult(
        long id = 1,
        bool isSpam = true,
        int confidence = 95,
        string detectionSource = "automatic",
        string detectionMethod = "BayesClassifier",
        string? reason = null,
        DateTimeOffset? detectedAt = null,
        bool usedForTraining = true,
        int editVersion = 0)
    {
        return new DetectionResultRecord
        {
            Id = id,
            MessageId = 12345,
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            DetectionSource = detectionSource,
            DetectionMethod = detectionMethod,
            IsSpam = isSpam,
            Confidence = confidence,
            NetConfidence = isSpam ? confidence : -confidence,
            AddedBy = Actor.FromSystem("DetectionService"),
            UserId = 67890,
            Reason = reason,
            UsedForTraining = usedForTraining,
            EditVersion = editVersion
        };
    }

    /// <summary>
    /// Opens the DetectionHistoryDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        MessageRecord message,
        List<DetectionResultRecord>? detectionResults = null)
    {
        var parameters = new DialogParameters<DetectionHistoryDialog>
        {
            { x => x.Message, message },
            { x => x.DetectionResults, detectionResults }
        };

        return await DialogService.ShowAsync<DetectionHistoryDialog>("Detection History", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

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
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-actions"));
        });
    }

    [Test]
    public void HasCloseButton()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });
    }

    [Test]
    public void DisplaysTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Spam Detection History"));
        });
    }

    #endregion

    #region Empty State Tests

    [Test]
    public void ShowsInfoAlert_WhenNoResults()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message, detectionResults: []);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No detection history available"));
        });
    }

    [Test]
    public void ShowsInfoAlert_WhenResultsNull()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message, detectionResults: null);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No detection history available"));
        });
    }

    #endregion

    #region Timeline Tests

    [Test]
    public void DisplaysTimeline_WhenResultsExist()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult()
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-timeline"));
        });
    }

    [Test]
    public void DisplaysTimelineItems_ForEachResult()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(id: 1),
            CreateTestResult(id: 2),
            CreateTestResult(id: 3)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            var items = provider.FindAll(".mud-timeline-item");
            Assert.That(items.Count, Is.EqualTo(3));
        });
    }

    #endregion

    #region Detection Result Display Tests

    [Test]
    public void DisplaysSpamChip_WhenSpam()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(isSpam: true)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("SPAM"));
            Assert.That(provider.Markup, Does.Contain("mud-chip-color-error"));
        });
    }

    [Test]
    public void DisplaysHamChip_WhenHam()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(isSpam: false)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("HAM"));
            Assert.That(provider.Markup, Does.Contain("mud-chip-color-success"));
        });
    }

    [Test]
    public void DisplaysNetConfidence()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(confidence: 85)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Net:"));
        });
    }

    [Test]
    public void DisplaysDetectionMethod()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(detectionMethod: "BayesClassifier")
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Method:"));
            Assert.That(provider.Markup, Does.Contain("BayesClassifier"));
        });
    }

    [Test]
    public void DisplaysReason_WhenProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(reason: "Contains spam keywords")
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Reason:"));
            Assert.That(provider.Markup, Does.Contain("Contains spam keywords"));
        });
    }

    [Test]
    public void DisplaysDetectionSource()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(detectionSource: "automatic")
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("automatic"));
        });
    }

    #endregion

    #region Training Sample Indicator Tests

    [Test]
    public void DisplaysTrainingSampleChip_WhenUsedForTraining()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(usedForTraining: true)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Training Sample"));
        });
    }

    #endregion

    #region Edit Version Indicator Tests

    [Test]
    public void DisplaysEditVersionChip_WhenEdited()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(editVersion: 2)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Edit v2"));
        });
    }

    [Test]
    public void HidesEditVersionChip_WhenOriginal()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        var results = new List<DetectionResultRecord>
        {
            CreateTestResult(editVersion: 0)
        };

        // Act
        _ = OpenDialogAsync(message, detectionResults: results);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Edit v"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void CloseButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        _ = OpenDialogAsync(message);

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });

        // Act - Click close button
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Close");
        closeButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
