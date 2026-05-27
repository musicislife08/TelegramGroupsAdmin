using System.Reflection;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
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
    protected IAnalyticsRepository Analytics { get; }
    protected ISnackbar Snackbar { get; }

    protected PerformanceMetricsTimezoneContext(TimeZoneInfo? initialTimeZone)
    {
        Analytics = Substitute.For<IAnalyticsRepository>();
        Snackbar = Substitute.For<ISnackbar>();

        Services.AddSingleton(Analytics);
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

        // Cascade TimeZoneInfo with the initial value for this scenario
        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, initialTimeZone));

        // Analytics mocks — valid defaults so LoadData doesn't throw if it fires
        Analytics.GetDetectionAccuracyStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DetectionAccuracyStats());

        Analytics.GetResponseTimeStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ResponseTimeStats());

        Analytics.GetDetectionMethodComparisonAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DetectionMethodStats>());

        Analytics.GetDailyDetectionTrendsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DailyDetectionTrend>());

        Analytics.GetAlgorithmPerformanceStatsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<AlgorithmPerformanceStats>());
    }

    [SetUp]
    public void ClearCalls()
    {
        Analytics.ClearReceivedCalls();
    }

    /// <summary>
    /// Renders PerformanceMetrics wrapped in a controllable CascadingValue[TimeZoneInfo?].
    /// Returns the PerformanceMetrics cut and the container so the cascade can be updated mid-test.
    /// </summary>
    protected (IRenderedComponent<PerformanceMetrics> Cut, IRenderedComponent<ContainerFragment> Container)
        RenderWithControllableCascade(TimeZoneInfo? initial)
    {
        var box = new TimeZoneBox { Value = initial };

        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<CascadingValue<TimeZoneInfo?>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<TimeZoneInfo?>.Value), box.Value);
            builder.AddComponentParameter(2, nameof(CascadingValue<TimeZoneInfo?>.IsFixed), false);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<PerformanceMetrics>(0);
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        };

        var container = Render(fragment);
        var cut = container.FindComponent<PerformanceMetrics>();
        return (cut, container);
    }

    /// <summary>
    /// Simulates a CascadingValue[TimeZoneInfo?] change via BunitRenderer.SetDirectParametersAsync
    /// (accessed via reflection since it is internal in bUnit 2.7.x).
    /// </summary>
    protected async Task SimulateCascadeArrival(
        IRenderedComponent<ContainerFragment> container,
        TimeZoneInfo newTimeZone)
    {
        var setDirectParams = typeof(BunitRenderer)
            .GetMethod(
                "SetDirectParametersAsync",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .MakeGenericMethod(typeof(CascadingValue<TimeZoneInfo?>));

        var cascadeComp = Renderer.FindComponent<CascadingValue<TimeZoneInfo?>>(
            (IRenderedComponent<IComponent>)container);

        var newParams = ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["Value"] = (TimeZoneInfo?)newTimeZone });

        await (Task)setDirectParams.Invoke(Renderer, [cascadeComp, newParams])!;
        await Task.Yield();
    }

    protected sealed class TimeZoneBox
    {
        public TimeZoneInfo? Value { get; set; }
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

        await Analytics.DidNotReceive().GetDetectionAccuracyStatsAsync(
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

        await Analytics.Received(1).GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "UTC",
            Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// Cascade-arrival: cascade starts null, then the real timezone arrives.
/// LoadData fires exactly once on first non-null cascade.
/// </summary>
[TestFixture]
public class PerformanceMetricsTimezone_CascadeArrival : PerformanceMetricsTimezoneContext
{
    public PerformanceMetricsTimezone_CascadeArrival() : base(null) { }

    [Test]
    public async Task CascadeArrives_LoadsExactlyOnce_WithRealTimezone()
    {
        var (cut, container) = RenderWithControllableCascade(null);
        await Task.Yield();

        var real = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        await SimulateCascadeArrival(container, real);

        await Analytics.Received(1).GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "America/Chicago",
            Arg.Any<CancellationToken>());
    }
}
