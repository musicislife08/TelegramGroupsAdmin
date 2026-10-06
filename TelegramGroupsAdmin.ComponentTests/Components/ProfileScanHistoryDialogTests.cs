using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>Test context for ProfileScanHistoryDialog: a substituted scan-results repository.</summary>
public class ProfileScanHistoryDialogTestContext : BunitContext
{
    protected IProfileScanResultsRepository Results { get; }

    protected ProfileScanHistoryDialogTestContext()
    {
        Results = Substitute.For<IProfileScanResultsRepository>();
        Services.AddSingleton(Results);
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>Component tests for ProfileScanHistoryDialog: source and name flag chips.</summary>
[TestFixture]
public class ProfileScanHistoryDialogTests : ProfileScanHistoryDialogTestContext
{
    [SetUp]
    public void Setup() => Results.ClearReceivedCalls();

    private static ProfileScanResultRecord Row(long id, ProfileScanSource source, bool explicitName, bool promotionalName) =>
        new(id, 42, DateTimeOffset.UtcNow.AddMinutes(-id), 3.0m, ProfileScanOutcome.HeldForReview, 0m, 3.0m,
            "reason", null, ExplicitDisplayText: explicitName, PromotionalDisplayText: promotionalName, Source: source);

    private Task<IRenderedComponent<MudDialogProvider>> OpenAsync(params ProfileScanResultRecord[] rows)
    {
        Results.GetByUserIdAsync(42, Arg.Any<CancellationToken>()).Returns(rows.ToList());
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        _ = dialogs.ShowAsync<ProfileScanHistoryDialog>("History", new DialogParameters<ProfileScanHistoryDialog>
        {
            { d => d.UserId, 42L },
            { d => d.UserDisplayName, "Sam Rivera" }
        });
        return Task.FromResult(provider);
    }

    [Test]
    public async Task NameOnlyPromotionalRow_ShowsSourceAndPromotionalChip()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.NameOnly, explicitName: false, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name only"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
        });
    }

    [Test]
    public async Task FullScanRowWithBothFlags_ShowsSourceAndBothChips()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.FullScan, explicitName: true, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
        });
    }

    [Test]
    public async Task UnflaggedRow_ShowsNoNameChips()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.FullScan, explicitName: false, promotionalName: false));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Not.Contain("Promotional name"));
        });
    }
}
