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
    private static ProfileScanResultRecord Row(long id, ProfileScanSource source, bool explicitName, bool promotionalName) =>
        new(id, 42, DateTimeOffset.UtcNow.AddMinutes(-id), 3.0m, ProfileScanOutcome.HeldForReview, 0m, 3.0m,
            "reason", null, ExplicitDisplayText: explicitName, PromotionalDisplayText: promotionalName, Source: source);

    private IRenderedComponent<MudDialogProvider> Open(params ProfileScanResultRecord[] rows)
    {
        Results.GetByUserIdAsync(42, Arg.Any<CancellationToken>()).Returns(rows.ToList());
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        _ = dialogs.ShowAsync<ProfileScanHistoryDialog>("History", new DialogParameters<ProfileScanHistoryDialog>
        {
            { d => d.UserId, 42L },
            { d => d.UserDisplayName, "Sam Rivera" }
        });
        return provider;
    }

    [Test]
    public void NameOnlyPromotionalRow_ShowsSourceAndPromotionalChip()
    {
        var provider = Open(Row(1, ProfileScanSource.NameOnly, explicitName: false, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name only"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
        });
    }

    [Test]
    public void FullScanRowWithBothFlags_ShowsSourceAndBothChips()
    {
        var provider = Open(Row(1, ProfileScanSource.FullScan, explicitName: true, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
        });
    }

    [Test]
    public void UnflaggedRow_ShowsNoNameChips()
    {
        var provider = Open(Row(1, ProfileScanSource.FullScan, explicitName: false, promotionalName: false));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Not.Contain("Promotional name"));
        });
    }

    [TestCase(ProfileScanSource.FullScan, "Full scan")]
    [TestCase(ProfileScanSource.NameOnly, "Name only")]
    public void FormatSource_MapsEachSource(ProfileScanSource source, string expected)
    {
        Assert.That(ProfileScanHistoryDialog.FormatSource(source), Is.EqualTo(expected));
    }

    [Test]
    public void FormatSource_UnmappedSource_FailsLoudly()
    {
        Assert.That(() => ProfileScanHistoryDialog.FormatSource((ProfileScanSource)99), Throws.InvalidOperationException);
    }

    [Test]
    public void NameOnlyRow_ExplainsTheChipInATooltip()
    {
        var provider = Open(Row(1, ProfileScanSource.NameOnly, explicitName: false, promotionalName: false));

        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Name only")), TimeSpan.FromSeconds(2));
        // Tooltip content renders only on hover, so the tooltip wrapping the chip is checked instead.
        var tooltip = provider.FindComponents<MudTooltip>()
            .Single(t => t.Find(".mud-chip").TextContent.Contains("Name only"));
        Assert.That(tooltip.Instance.Text,
            Is.EqualTo("The profile could not be read; only the display name and username were scored"));
    }
}
