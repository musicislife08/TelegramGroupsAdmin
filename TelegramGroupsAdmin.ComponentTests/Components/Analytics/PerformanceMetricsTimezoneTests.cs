using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Components.Shared.Analytics;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

/// <summary>
/// Shared setup base for PerformanceMetrics timezone tests.
/// Each concrete subclass is a separate NUnit [TestFixture] so each gets a fresh BunitContext.
/// </summary>
public abstract class PerformanceMetricsTimezoneContext : BunitContext
{
    protected IAnalyticsRepository AnalyticsRepository { get; }
    protected ISnackbar Snackbar { get; }

    protected PerformanceMetricsTimezoneContext(TimeZoneInfo? initialTimeZone)
    {
        AnalyticsRepository = Substitute.For<IAnalyticsRepository>();
        Snackbar = Substitute.For<ISnackbar>();

        Services.AddSingleton(AnalyticsRepository);
        Services.AddSingleton(Snackbar);

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

        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, initialTimeZone));

        AnalyticsRepository.GetDetectionAccuracyStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DetectionAccuracyStats());

        AnalyticsRepository.GetResponseTimeStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ResponseTimeStats());

        AnalyticsRepository.GetDetectionMethodComparisonAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DetectionMethodStats>());

        AnalyticsRepository.GetDailyDetectionTrendsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DailyDetectionTrend>());

        AnalyticsRepository.GetAlgorithmPerformanceStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<AlgorithmPerformanceStats>());
    }

    [SetUp]
    public void ClearCalls()
    {
        AnalyticsRepository.ClearReceivedCalls();
    }

    /// <summary>
    /// Renders PerformanceMetrics wrapped in a TimezoneCascadeHost whose Tz [Parameter]
    /// can be updated mid-test via host.InvokeAsync(() => host.Instance.SetParametersAsync(...)),
    /// using only public bUnit APIs.
    /// </summary>
    protected (IRenderedComponent<PerformanceMetrics> Cut, IRenderedComponent<TimezoneCascadeHost> Host)
        RenderWithCascadeHost(TimeZoneInfo? initial)
    {
        RenderFragment childContent = builder =>
        {
            builder.OpenComponent<PerformanceMetrics>(0);
            builder.CloseComponent();
        };

        var host = Render<TimezoneCascadeHost>(p => p
            .Add(h => h.Tz, initial)
            .Add(h => h.ChildContent, childContent));

        var cut = host.FindComponent<PerformanceMetrics>();
        return (cut, host);
    }

    /// <summary>
    /// Updates the host's Tz [Parameter] (public bUnit API; SetParametersAsync on a
    /// regular [Parameter] is allowed by Blazor — only [CascadingParameter] explicit-set is blocked).
    /// </summary>
    protected async Task SetHostTimezone(
        IRenderedComponent<TimezoneCascadeHost> host,
        TimeZoneInfo? newTimeZone)
    {
        await host.InvokeAsync(() =>
            host.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(TimezoneCascadeHost.Tz)] = newTimeZone,
                    [nameof(TimezoneCascadeHost.ChildContent)] = (RenderFragment)(builder =>
                    {
                        builder.OpenComponent<PerformanceMetrics>(0);
                        builder.CloseComponent();
                    })
                })));
        await Task.Yield();
    }
}

/// <summary>
/// Cold-circuit: UserTimeZone is null when OnInitializedAsync runs.
/// LoadData must NOT fire at all.
/// </summary>
[TestFixture]
public class PerformanceMetricsTimezone_ColdCircuit : PerformanceMetricsTimezoneContext
{
    public PerformanceMetricsTimezone_ColdCircuit() : base(null) { }

    [Test]
    public async Task ColdCircuit_NullTimezone_DoesNotLoadDataOnInit()
    {
        var cut = Render<PerformanceMetrics>();
        await Task.Yield();

        await AnalyticsRepository.DidNotReceive().GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// Warm-circuit: UserTimeZone is already set when OnInitializedAsync runs.
/// LoadData must fire exactly once with the correct timezone.
/// </summary>
[TestFixture]
public class PerformanceMetricsTimezone_WarmCircuit : PerformanceMetricsTimezoneContext
{
    public PerformanceMetricsTimezone_WarmCircuit() : base(TimeZoneInfo.Utc) { }

    [Test]
    public async Task WarmCircuit_TimezoneAlreadySet_LoadsExactlyOnceOnInit()
    {
        var cut = Render<PerformanceMetrics>();
        await Task.Yield();

        await AnalyticsRepository.Received(1).GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "UTC",
            Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// Cascade-arrival: cascade starts null, then the real timezone arrives.
/// LoadData fires exactly once on first non-null cascade; subsequent changes are ignored.
/// </summary>
[TestFixture]
public class PerformanceMetricsTimezone_CascadeArrival : PerformanceMetricsTimezoneContext
{
    public PerformanceMetricsTimezone_CascadeArrival() : base(null) { }

    [Test]
    public async Task CascadeArrives_LoadsExactlyOnce_WithRealTimezone()
    {
        var (cut, host) = RenderWithCascadeHost(null);
        await Task.Yield();

        var real = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        await SetHostTimezone(host, real);

        await AnalyticsRepository.Received(1).GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "America/Chicago",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CascadeArrives_SubsequentParameterSetsDoNotReload()
    {
        var (cut, host) = RenderWithCascadeHost(null);
        await Task.Yield();

        var tz1 = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        await SetHostTimezone(host, tz1);

        // Second arrival — _seenTimeZone latch must block a second load
        var tz2 = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        await SetHostTimezone(host, tz2);

        await AnalyticsRepository.Received(1).GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
