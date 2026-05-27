using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
    protected IMessageStatsService MessageStatsService { get; }
    protected IAnalyticsRepository AnalyticsRepository { get; }
    protected IManagedChatsRepository ManagedChatsRepository { get; }
    protected ISnackbar Snackbar { get; }

    protected MessageTrendsTimezoneContext(TimeZoneInfo? initialTimeZone)
    {
        MessageStatsService = Substitute.For<IMessageStatsService>();
        AnalyticsRepository = Substitute.For<IAnalyticsRepository>();
        ManagedChatsRepository = Substitute.For<IManagedChatsRepository>();
        Snackbar = Substitute.For<ISnackbar>();

        Services.AddSingleton(MessageStatsService);
        Services.AddSingleton(AnalyticsRepository);
        Services.AddSingleton(ManagedChatsRepository);
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

        RenderTree.TryAdd<CascadingValue<WebUserIdentity?>>(p =>
            p.Add(cv => cv.Value, WebUserRenderHelper.TestWebUser));

        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, initialTimeZone));

        ManagedChatsRepository.GetUserAccessibleChatsAsync(
                Arg.Any<string>(),
                Arg.Any<PermissionLevel>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        AnalyticsRepository.GetSpamTrendComparisonAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new SpamTrendComparison());

        MessageStatsService.GetMessageTrendsAsync(
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
        MessageStatsService.ClearReceivedCalls();
        AnalyticsRepository.ClearReceivedCalls();
        ManagedChatsRepository.ClearReceivedCalls();
    }

    /// <summary>
    /// Renders MessageTrends wrapped in a TimezoneCascadeHost whose Tz [Parameter] can be
    /// updated mid-test via host.InvokeAsync(() => host.Instance.SetParametersAsync(...)),
    /// using only public bUnit APIs.
    /// </summary>
    protected (IRenderedComponent<MessageTrends> Cut, IRenderedComponent<TimezoneCascadeHost> Host)
        RenderWithCascadeHost(TimeZoneInfo? initial)
    {
        RenderFragment childContent = builder =>
        {
            builder.OpenComponent<MessageTrends>(0);
            builder.CloseComponent();
        };

        var host = Render<TimezoneCascadeHost>(p => p
            .Add(h => h.Tz, initial)
            .Add(h => h.ChildContent, childContent));

        var cut = host.FindComponent<MessageTrends>();
        return (cut, host);
    }

    /// <summary>
    /// Updates the host's Tz [Parameter] and waits for the propagation cycle to settle.
    /// Pure public bUnit API — SetParametersAsync on a regular [Parameter] is allowed
    /// (only [CascadingParameter] explicit-set is blocked by Blazor).
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
                        builder.OpenComponent<MessageTrends>(0);
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
public class MessageTrendsTimezone_ColdCircuit : MessageTrendsTimezoneContext
{
    public MessageTrendsTimezone_ColdCircuit() : base(null) { }

    [Test]
    public async Task ColdCircuit_NullTimezone_DoesNotLoadDataOnInit()
    {
        var cut = Render<MessageTrends>();
        await Task.Yield();

        await MessageStatsService.DidNotReceive().GetMessageTrendsAsync(
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

        await MessageStatsService.Received(1).GetMessageTrendsAsync(
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
        var (cut, host) = RenderWithCascadeHost(null);
        await Task.Yield();

        var real = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        await SetHostTimezone(host, real);

        await MessageStatsService.Received(1).GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            "America/New_York",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CascadeArrives_SubsequentParameterSetsDoNotReload()
    {
        var (cut, host) = RenderWithCascadeHost(null);
        await Task.Yield();

        var tz1 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        await SetHostTimezone(host, tz1);

        // Second arrival — _seenTimeZone latch must block a second load
        var tz2 = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        await SetHostTimezone(host, tz2);

        await MessageStatsService.Received(1).GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
