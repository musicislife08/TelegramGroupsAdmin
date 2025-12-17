using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for TrainingDataBalanceStatus tests.
/// Registers mocked IMLTrainingDataRepository and IMLTextClassifierService.
/// </summary>
public class TrainingDataBalanceStatusTestContext : BunitContext
{
    protected IMLTrainingDataRepository MLTrainingDataRepository { get; }
    protected IMLTextClassifierService MLTextClassifierService { get; }
    protected ILogger<TrainingDataBalanceStatus> Logger { get; }

    protected TrainingDataBalanceStatusTestContext()
    {
        // Create mocks
        MLTrainingDataRepository = Substitute.For<IMLTrainingDataRepository>();
        MLTextClassifierService = Substitute.For<IMLTextClassifierService>();
        Logger = Substitute.For<ILogger<TrainingDataBalanceStatus>>();

        // Default: no pending data, no model trained
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats());
        MLTextClassifierService.GetMetadata().Returns((SpamClassifierMetadata?)null);

        // Register mocks
        Services.AddSingleton(MLTrainingDataRepository);
        Services.AddSingleton(MLTextClassifierService);
        Services.AddSingleton(Logger);

        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Setup JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>
/// Component tests for TrainingDataBalanceStatus.razor (#174)
/// Tests the training data balance visualization with current model vs pending data comparison.
/// </summary>
[TestFixture]
public class TrainingDataBalanceStatusTests : TrainingDataBalanceStatusTestContext
{
    [SetUp]
    public void Setup()
    {
        MLTrainingDataRepository.ClearReceivedCalls();
    }

    #region Structure Tests

    [Test]
    public void RendersWithoutError()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void DisplaysTitle()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Training Data Balance"));
    }

    [Test]
    public void HasPaperContainer()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    #endregion

    #region No Model Tests (#174)

    [Test]
    public void ShowsNoModelMessage_WhenMetadataNull()
    {
        // Arrange - MLTextClassifierService returns null for GetMetadata() by default
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitSpamCount = 10, ExplicitHamCount = 20 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert - Should show "No model trained yet" message
        Assert.That(cut.Markup, Does.Contain("No model trained yet"));
    }

    [Test]
    public void ShowsPendingTrainingDataSection()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitSpamCount = 10, ExplicitHamCount = 20 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Pending Training Data"));
    }

    #endregion

    #region Balance Stats Display Tests

    [Test]
    public void DisplaysExplicitSpamCount()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitSpamCount = 25 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Explicit Spam"));
        Assert.That(cut.Markup, Does.Contain("25"));
    }

    [Test]
    public void DisplaysImplicitSpamCount()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ImplicitSpamCount = 15 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Implicit Spam"));
        Assert.That(cut.Markup, Does.Contain("15"));
    }

    [Test]
    public void DisplaysExplicitHamCount()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitHamCount = 30 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Explicit Ham"));
        Assert.That(cut.Markup, Does.Contain("30"));
    }

    [Test]
    public void DisplaysImplicitHamCount()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ImplicitHamCount = 50 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Implicit Ham"));
        Assert.That(cut.Markup, Does.Contain("50"));
    }

    [Test]
    public void DisplaysTotalSampleCount()
    {
        // Arrange
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats
            {
                ExplicitSpamCount = 10,
                ImplicitSpamCount = 5,
                ExplicitHamCount = 20,
                ImplicitHamCount = 15
            });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert - Total: 10+5+20+15 = 50
        Assert.That(cut.Markup, Does.Contain("Total: 50 samples"));
    }

    #endregion

    #region Status Text Tests

    [Test]
    public void ShowsInsufficientDataWarning_WhenSpamBelowMinimum()
    {
        // Arrange - Need 20 minimum per class
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitSpamCount = 10, ExplicitHamCount = 30 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Insufficient data"));
        Assert.That(cut.Markup, Does.Contain("spam"));
    }

    [Test]
    public void ShowsInsufficientDataWarning_WhenHamBelowMinimum()
    {
        // Arrange - Need 20 minimum per class
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats { ExplicitSpamCount = 30, ExplicitHamCount = 10 });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Insufficient data"));
        Assert.That(cut.Markup, Does.Contain("ham"));
    }

    [Test]
    public void ShowsMeetsRequirements_WhenSufficientData()
    {
        // Arrange - Both classes above 20
        MLTrainingDataRepository.GetTrainingBalanceStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new TrainingBalanceStats
            {
                ExplicitSpamCount = 25,
                ExplicitHamCount = 55
            });

        // Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert - Should show "Meets minimum requirements"
        Assert.That(cut.Markup, Does.Contain("Meets minimum requirements"));
    }

    #endregion

    #region Refresh Button Tests

    [Test]
    public void HidesRefreshButton_ByDefault()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert - Refresh button should not be visible by default
        Assert.That(cut.FindAll("button").Count, Is.EqualTo(0));
    }

    [Test]
    public void ShowsRefreshButton_WhenEnabled()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>(p => p
            .Add(x => x.ShowRefreshButton, true));

        // Assert - Refresh button should be visible
        Assert.That(cut.FindAll("button").Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task InvokesOnRefresh_WhenRefreshButtonClicked()
    {
        // Arrange
        var refreshCalled = false;
        var cut = Render<TrainingDataBalanceStatus>(p => p
            .Add(x => x.ShowRefreshButton, true)
            .Add(x => x.OnRefresh, EventCallback.Factory.Create(this, () => refreshCalled = true)));

        // Act
        var refreshButton = cut.Find("button");
        await refreshButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(refreshCalled, Is.True);
    }

    #endregion

    #region Elevation Parameter Tests

    [Test]
    public void UsesDefaultElevation()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>();

        // Assert - Default elevation is 1
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    [Test]
    public void UsesCustomElevation()
    {
        // Arrange & Act
        var cut = Render<TrainingDataBalanceStatus>(p => p
            .Add(x => x.Elevation, 4));

        // Assert - Should render with elevation
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    #endregion
}
