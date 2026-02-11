using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for MessageFilters tests.
/// Registers mocked IMessageQueryService.
/// </summary>
public class MessageFiltersTestContext : BunitContext
{
    protected IMessageQueryService MessageQueryService { get; }

    protected MessageFiltersTestContext()
    {
        // Create mocks
        MessageQueryService = Substitute.For<IMessageQueryService>();
        var logger = Substitute.For<ILogger<MessageFilters>>();

        // Default: return empty lists for autocomplete
        MessageQueryService.GetDistinctUserNamesAsync().Returns([]);
        MessageQueryService.GetDistinctChatNamesAsync().Returns([]);

        // Register mocks
        Services.AddSingleton(MessageQueryService);
        Services.AddSingleton(logger);

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
/// Component tests for MessageFilters.razor
/// Tests the message filter panel component.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests recommended for:
/// - Testing autocomplete dropdown interactions (requires JS)
/// - Testing filter chip removal interactions
/// - Testing date picker interactions
/// - Testing debounced search input behavior
/// - Testing filter state persistence across parent re-renders
/// </remarks>
[TestFixture]
public class MessageFiltersTests : MessageFiltersTestContext
{
    [SetUp]
    public void Setup()
    {
        MessageQueryService.ClearReceivedCalls();
    }

    #region Helper Methods

    private MessageFilters.MessageFilterState CreateDefaultFilterState()
    {
        return new MessageFilters.MessageFilterState();
    }

    #endregion

    #region Structure Tests

    [Test]
    public void RendersWithoutError()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void HasChatFilter()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Chat"));
        });
    }

    [Test]
    public void HasAllChatsPlaceholder()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("All Chats"));
        });
    }

    #endregion

    #region Spam Status Filter Tests

    [Test]
    public void HasAllSpamStatusButton()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button");
            Assert.That(buttons.Any(b => b.TextContent.Trim() == "All"), Is.True);
        });
    }

    [Test]
    public void HasSpamStatusButton()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button");
            Assert.That(buttons.Any(b => b.TextContent.Trim() == "Spam"), Is.True);
        });
    }

    [Test]
    public void HasCleanStatusButton()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button");
            Assert.That(buttons.Any(b => b.TextContent.Trim() == "Clean"), Is.True);
        });
    }

    #endregion

    #region Content Type Filter Tests

    [Test]
    public void HasImagesFilterChip()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Images"));
        });
    }

    [Test]
    public void HasLinksFilterChip()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Links"));
        });
    }

    [Test]
    public void HasEditedFilterChip()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Edited"));
        });
    }

    #endregion

    #region Advanced Filters Tests

    [Test]
    public void HasAdvancedToggleButton()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Advanced"));
        });
    }

    [Test]
    public void HasClearButton()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Clear"));
        });
    }

    [Test]
    public void ClearButtonDisabled_WhenNoActiveFilters()
    {
        // Arrange & Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, CreateDefaultFilterState()));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var clearButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Clear"));
            Assert.That(clearButton, Is.Not.Null);
            Assert.That(clearButton!.GetAttribute("disabled"), Is.Not.Null);
        });
    }

    #endregion

    #region Active Filters Display Tests

    [Test]
    public void ShowsSearchTextChip_WhenFilterActive()
    {
        // Arrange
        var filters = CreateDefaultFilterState();
        filters.SearchText = "test search";

        // Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, filters));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Text:"));
            Assert.That(cut.Markup, Does.Contain("test search"));
        });
    }

    [Test]
    public void ShowsUserChip_WhenFilterActive()
    {
        // Arrange
        var filters = CreateDefaultFilterState();
        filters.UserName = "testuser";

        // Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, filters));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User:"));
            Assert.That(cut.Markup, Does.Contain("testuser"));
        });
    }

    [Test]
    public void ShowsChatChip_WhenFilterActive()
    {
        // Arrange
        var filters = CreateDefaultFilterState();
        filters.ChatName = "Test Chat";

        // Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, filters));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Chat:"));
            Assert.That(cut.Markup, Does.Contain("Test Chat"));
        });
    }

    [Test]
    public void ShowsFromDateChip_WhenFilterActive()
    {
        // Arrange
        var filters = CreateDefaultFilterState();
        filters.StartDate = new DateTime(2024, 1, 15);

        // Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, filters));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("From:"));
            Assert.That(cut.Markup, Does.Contain("2024-01-15"));
        });
    }

    [Test]
    public void ShowsToDateChip_WhenFilterActive()
    {
        // Arrange
        var filters = CreateDefaultFilterState();
        filters.EndDate = new DateTime(2024, 12, 31);

        // Act
        var cut = Render<MessageFilters>(p => p
            .Add(x => x.Filters, filters));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("To:"));
            Assert.That(cut.Markup, Does.Contain("2024-12-31"));
        });
    }

    #endregion

    #region Filter State Tests

    [Test]
    public void MessageFilterState_HasCorrectDefaultValues()
    {
        // Arrange & Act
        var state = new MessageFilters.MessageFilterState();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(state.SearchText, Is.Null);
            Assert.That(state.UserName, Is.Null);
            Assert.That(state.ChatName, Is.Null);
            Assert.That(state.SpamFilter, Is.EqualTo(MessageFilters.SpamFilterOption.All));
            Assert.That(state.StartDate, Is.Null);
            Assert.That(state.EndDate, Is.Null);
            Assert.That(state.HasImages, Is.Null);
            Assert.That(state.HasLinks, Is.Null);
            Assert.That(state.HasEdits, Is.Null);
        }
    }

    #endregion
}
