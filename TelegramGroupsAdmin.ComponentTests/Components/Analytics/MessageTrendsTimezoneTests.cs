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
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

/// <summary>
/// Shared setup base for MessageTrends timezone tests.
/// Each concrete subclass is a separate NUnit [TestFixture] so each gets a fresh BunitContext.
/// Services and mocks are registered in the constructor (not SetUp) because BunitContext
/// locks its service collection once the first component is rendered.
/// </summary>
public abstract class MessageTrendsTimezoneContext : BunitContext
{
    protected IMessageStatsService MessageStats { get; }
    protected IAnalyticsRepository Analytics { get; }
    protected IManagedChatsRepository Chats { get; }
    protected ISnackbar Snackbar { get; }

    protected MessageTrendsTimezoneContext(TimeZoneInfo? initialTimeZone)
    {
        MessageStats = Substitute.For<IMessageStatsService>();
        Analytics = Substitute.For<IAnalyticsRepository>();
        Chats = Substitute.For<IManagedChatsRepository>();
        Snackbar = Substitute.For<ISnackbar>();

        Services.AddSingleton(MessageStats);
        Services.AddSingleton(Analytics);
        Services.AddSingleton(Chats);
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

        // Cascade WebUserIdentity — component returns early in OnInitializedAsync if null
        RenderTree.TryAdd<CascadingValue<WebUserIdentity?>>(p =>
            p.Add(cv => cv.Value, WebUserRenderHelper.TestWebUser));

        // Cascade TimeZoneInfo with the initial value for this scenario
        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, initialTimeZone));

        // Chats — return empty list to avoid NRE on _accessibleChats.Any() in the template
        Chats.GetUserAccessibleChatsAsync(
                Arg.Any<string>(),
                Arg.Any<PermissionLevel>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        // Analytics mocks — valid defaults so LoadData doesn't throw if it fires
        Analytics.GetSpamTrendComparisonAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new SpamTrendComparison());

        MessageStats.GetMessageTrendsAsync(
                Arg.Any<List<long>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new MessageTrendsData());
    }

    [SetUp]
    public void ClearCalls()
    {
        MessageStats.ClearReceivedCalls();
        Analytics.ClearReceivedCalls();
        Chats.ClearReceivedCalls();
    }

    /// <summary>
    /// Renders MessageTrends wrapped in a controllable CascadingValue[TimeZoneInfo?].
    /// Returns the MessageTrends cut and the container so the cascade can be updated mid-test.
    /// The RenderTree's TimeZoneInfo? cascade is not used here; the fragment's own CascadingValue
    /// takes precedence since it is the direct parent.
    /// </summary>
    protected (IRenderedComponent<MessageTrends> Cut, IRenderedComponent<ContainerFragment> Container)
        RenderWithControllableCascade(TimeZoneInfo? initial)
    {
        // Capture value in a mutable box so the fragment closure can observe changes
        var box = new TimeZoneBox { Value = initial };

        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<CascadingValue<TimeZoneInfo?>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<TimeZoneInfo?>.Value), box.Value);
            builder.AddComponentParameter(2, nameof(CascadingValue<TimeZoneInfo?>.IsFixed), false);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<MessageTrends>(0);
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        };

        var container = Render(fragment);
        var cut = container.FindComponent<MessageTrends>();
        return (cut, container);
    }

    /// <summary>
    /// Simulates a CascadingValue[TimeZoneInfo?] change by finding the parent CascadingValue
    /// in the container's render tree and setting its Value via BunitRenderer.SetDirectParametersAsync
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

        // The CascadingValue<TimeZoneInfo?> is a direct child of the ContainerFragment
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
public class MessageTrendsTimezone_ColdCircuit : MessageTrendsTimezoneContext
{
    public MessageTrendsTimezone_ColdCircuit() : base(null) { }

    [Test]
    public async Task ColdCircuit_NullTimezone_DoesNotLoadDataOnInit()
    {
        var cut = Render<MessageTrends>();
        await Task.Yield();

        await MessageStats.DidNotReceive().GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
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
public class MessageTrendsTimezone_WarmCircuit : MessageTrendsTimezoneContext
{
    public MessageTrendsTimezone_WarmCircuit() : base(TimeZoneInfo.Utc) { }

    [Test]
    public async Task WarmCircuit_TimezoneAlreadySet_LoadsExactlyOnceOnInit()
    {
        var cut = Render<MessageTrends>();
        await Task.Yield();

        await MessageStats.Received(1).GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
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
public class MessageTrendsTimezone_CascadeArrival : MessageTrendsTimezoneContext
{
    public MessageTrendsTimezone_CascadeArrival() : base(null) { }

    [Test]
    public async Task CascadeArrives_LoadsExactlyOnce_WithRealTimezone()
    {
        var (cut, container) = RenderWithControllableCascade(null);
        await Task.Yield();

        var real = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        await SimulateCascadeArrival(container, real);

        await MessageStats.Received(1).GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "America/New_York",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CascadeArrives_SubsequentParameterSetsDoNotReload()
    {
        var (cut, container) = RenderWithControllableCascade(null);
        await Task.Yield();

        // First arrival — should trigger exactly one load
        var tz1 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        await SimulateCascadeArrival(container, tz1);

        // Second arrival — _seenTimeZone latch must block a second load
        var tz2 = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        await SimulateCascadeArrival(container, tz2);

        await MessageStats.Received(1).GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
