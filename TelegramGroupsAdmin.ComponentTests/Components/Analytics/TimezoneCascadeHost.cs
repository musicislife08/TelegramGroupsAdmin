using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

/// <summary>
/// Stateful test parent that wraps ChildContent in a CascadingValue&lt;TimeZoneInfo?&gt;.
/// Lets a bUnit test render the host with an initial Tz value and later call
/// host.SetParametersAndRender(p => p.Add(x => x.Tz, real)) to simulate the cascade
/// arriving from MainLayout — using only public bUnit APIs.
/// </summary>
public sealed class TimezoneCascadeHost : ComponentBase
{
    [Parameter] public TimeZoneInfo? Tz { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<TimeZoneInfo?>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<TimeZoneInfo?>.Value), Tz);
        builder.AddComponentParameter(2, nameof(CascadingValue<TimeZoneInfo?>.IsFixed), false);
        builder.AddComponentParameter(3, nameof(CascadingValue<TimeZoneInfo?>.ChildContent), ChildContent);
        builder.CloseComponent();
    }
}
