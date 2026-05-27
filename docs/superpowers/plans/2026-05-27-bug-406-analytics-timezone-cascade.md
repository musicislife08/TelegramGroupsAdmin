# #406 Analytics Timezone-Cascade Defer-Until-Ready — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `MessageTrends.razor` and `PerformanceMetrics.razor` from fetching analytics data with `TimeZoneInfo.Utc` before `MainLayout`'s timezone cascade resolves. Load exactly once with the correct timezone.

**Architecture:** Two changes per component. (1) `OnInitializedAsync` no longer calls `LoadData()` when `UserTimeZone` is null. (2) A new `OnParametersSetAsync` fires `LoadLast30Days()` exactly once, gated on `UserTimeZone` transitioning from null to a real value. The `(UserTimeZone ?? TimeZoneInfo.Utc).Id` fallback in `LoadData` becomes `UserTimeZone!.Id` since callers now guarantee non-null.

**Tech Stack:** Blazor Server, MudBlazor 9, bUnit for component tests.

**Spec:** `docs/superpowers/specs/2026-05-27-bug-406-analytics-timezone-cascade-design.md`

---

## File Structure

- Modify: `TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor` — lifecycle gate + UTC-fallback removal
- Modify: `TelegramGroupsAdmin/Components/Shared/Analytics/PerformanceMetrics.razor` — same shape
- Create or modify: `TelegramGroupsAdmin.ComponentTests/Components/Analytics/MessageTrendsTimezoneTests.cs`
- Create or modify: `TelegramGroupsAdmin.ComponentTests/Components/Analytics/PerformanceMetricsTimezoneTests.cs`

---

## Task 1: Component test for MessageTrends — cold-circuit defers initial load

**Files:**
- Create: `TelegramGroupsAdmin.ComponentTests/Components/Analytics/MessageTrendsTimezoneTests.cs`

- [ ] **Step 1: Write the failing test for cold-circuit deferral**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Components.Shared.Analytics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

[TestFixture]
public class MessageTrendsTimezoneTests : Bunit.TestContext
{
    private IMessageStatsService _messageStats = null!;
    private IAnalyticsRepository _analytics = null!;
    private IChatsRepository _chats = null!;
    private WebUserIdentity _webUser = null!;

    [SetUp]
    public void SetUp()
    {
        _messageStats = Substitute.For<IMessageStatsService>();
        _analytics = Substitute.For<IAnalyticsRepository>();
        _chats = Substitute.For<IChatsRepository>();

        Services.AddSingleton(_messageStats);
        Services.AddSingleton(_analytics);
        Services.AddSingleton(_chats);

        _webUser = new WebUserIdentity("user-1", "u@example.com", PermissionLevel.Admin);
    }

    [Test]
    public async Task ColdCircuit_NullTimezone_DoesNotLoadDataOnInit()
    {
        var cut = RenderComponent<MessageTrends>(parameters => parameters
            .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", null)
            .AddCascadingValue("WebUser", _webUser));

        await Task.Yield(); // allow OnInitializedAsync to complete

        await _messageStats.DidNotReceive().GetMessageTrendsAsync(
            Arg.Any<List<long>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MessageTrendsTimezoneTests.ColdCircuit_NullTimezone_DoesNotLoadDataOnInit"`

Expected: FAIL — `GetMessageTrendsAsync` was called at least once during `OnInitializedAsync` despite null timezone (current bug behavior).

- [ ] **Step 3: Implement the defer gate in MessageTrends.razor**

Edit `TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor` around line 427:

```csharp
protected override async Task OnInitializedAsync()
{
    if (WebUser is null) return;

    // Load accessible chats based on permissions
    _accessibleChats = await ChatsRepository.GetUserAccessibleChatsAsync(
        WebUser.Id,
        WebUser.PermissionLevel,
        cancellationToken: CancellationToken.None);

    if (UserTimeZone is not null)
    {
        _seenTimeZone = UserTimeZone;
        await LoadLast30Days();
    }
}

private TimeZoneInfo? _seenTimeZone;

protected override async Task OnParametersSetAsync()
{
    if (_seenTimeZone is null && UserTimeZone is not null)
    {
        _seenTimeZone = UserTimeZone;
        await LoadLast30Days();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MessageTrendsTimezoneTests.ColdCircuit_NullTimezone_DoesNotLoadDataOnInit"`

Expected: PASS

---

## Task 2: Component test for MessageTrends — cascade arrival triggers exactly one load

**Files:**
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/Analytics/MessageTrendsTimezoneTests.cs`

- [ ] **Step 1: Add test for cascade-arrival behavior**

```csharp
[Test]
public async Task CascadeArrives_LoadsExactlyOnce_WithRealTimezone()
{
    var cut = RenderComponent<MessageTrends>(parameters => parameters
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", null)
        .AddCascadingValue("WebUser", _webUser));

    await Task.Yield();

    var real = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    cut.SetParametersAndRender(p => p
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", real)
        .AddCascadingValue("WebUser", _webUser));

    await Task.Yield();

    await _messageStats.Received(1).GetMessageTrendsAsync(
        Arg.Any<List<long>>(),
        Arg.Any<DateTimeOffset>(),
        Arg.Any<DateTimeOffset>(),
        "America/New_York",
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run test to verify it passes (already-implemented gate handles this)**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MessageTrendsTimezoneTests.CascadeArrives_LoadsExactlyOnce_WithRealTimezone"`

Expected: PASS

- [ ] **Step 3: Add test for warm-circuit (timezone already set on init)**

```csharp
[Test]
public async Task WarmCircuit_TimezoneAlreadySet_LoadsExactlyOnceOnInit()
{
    var real = TimeZoneInfo.FindSystemTimeZoneById("UTC");
    var cut = RenderComponent<MessageTrends>(parameters => parameters
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", real)
        .AddCascadingValue("WebUser", _webUser));

    await Task.Yield();

    await _messageStats.Received(1).GetMessageTrendsAsync(
        Arg.Any<List<long>>(),
        Arg.Any<DateTimeOffset>(),
        Arg.Any<DateTimeOffset>(),
        "UTC",
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 4: Add test for cascade-arrival idempotency**

```csharp
[Test]
public async Task CascadeArrives_SubsequentParameterSetsDoNotReload()
{
    var cut = RenderComponent<MessageTrends>(parameters => parameters
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", null)
        .AddCascadingValue("WebUser", _webUser));

    await Task.Yield();

    var tz1 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    cut.SetParametersAndRender(p => p
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", tz1)
        .AddCascadingValue("WebUser", _webUser));
    await Task.Yield();

    var tz2 = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    cut.SetParametersAndRender(p => p
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", tz2)
        .AddCascadingValue("WebUser", _webUser));
    await Task.Yield();

    // Only the first non-null cascade triggers a load; subsequent changes don't.
    await _messageStats.Received(1).GetMessageTrendsAsync(
        Arg.Any<List<long>>(),
        Arg.Any<DateTimeOffset>(),
        Arg.Any<DateTimeOffset>(),
        Arg.Any<string>(),
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 5: Run all MessageTrends timezone tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MessageTrendsTimezoneTests"`

Expected: PASS (4 tests)

---

## Task 3: Strip the UTC fallback from MessageTrends.LoadData

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor:483`

- [ ] **Step 1: Replace the UTC-fallback with non-null assertion**

Change line 483 from:

```csharp
var timeZoneId = (UserTimeZone ?? TimeZoneInfo.Utc).Id;
```

To:

```csharp
var timeZoneId = UserTimeZone!.Id;
```

(All call paths into `LoadData` — `LoadLast7Days`, `LoadLast30Days`, `LoadAllTime`, and the `OnParametersSetAsync` gate — only fire after `UserTimeZone` is set.)

- [ ] **Step 2: Run all MessageTrends timezone tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MessageTrendsTimezoneTests"`

Expected: PASS (4 tests, unchanged)

---

## Task 4: Component test for PerformanceMetrics — same three behaviors

**Files:**
- Create: `TelegramGroupsAdmin.ComponentTests/Components/Analytics/PerformanceMetricsTimezoneTests.cs`

- [ ] **Step 1: Write the failing test (cold-circuit defers)**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Components.Shared.Analytics;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

[TestFixture]
public class PerformanceMetricsTimezoneTests : Bunit.TestContext
{
    private IAnalyticsRepository _analytics = null!;

    [SetUp]
    public void SetUp()
    {
        _analytics = Substitute.For<IAnalyticsRepository>();
        Services.AddSingleton(_analytics);
    }

    [Test]
    public async Task ColdCircuit_NullTimezone_DoesNotLoadDataOnInit()
    {
        var cut = RenderComponent<PerformanceMetrics>(parameters => parameters
            .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", null));

        await Task.Yield();

        await _analytics.DidNotReceive().GetDetectionAccuracyStatsAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~PerformanceMetricsTimezoneTests.ColdCircuit_NullTimezone_DoesNotLoadDataOnInit"`

Expected: FAIL — service call fired despite null timezone.

- [ ] **Step 3: Implement the same gate in PerformanceMetrics.razor**

Edit around line 324:

```csharp
protected override async Task OnInitializedAsync()
{
    if (UserTimeZone is not null)
    {
        _seenTimeZone = UserTimeZone;
        await LoadLast30Days();
    }
}

private TimeZoneInfo? _seenTimeZone;

protected override async Task OnParametersSetAsync()
{
    if (_seenTimeZone is null && UserTimeZone is not null)
    {
        _seenTimeZone = UserTimeZone;
        await LoadLast30Days();
    }
}
```

- [ ] **Step 4: Replace UTC fallback at line 356**

Change:

```csharp
var timeZoneId = (UserTimeZone ?? TimeZoneInfo.Utc).Id;
```

To:

```csharp
var timeZoneId = UserTimeZone!.Id;
```

- [ ] **Step 5: Add cascade-arrival and warm-circuit tests**

```csharp
[Test]
public async Task CascadeArrives_LoadsExactlyOnce_WithRealTimezone()
{
    var cut = RenderComponent<PerformanceMetrics>(parameters => parameters
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", null));
    await Task.Yield();

    var real = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    cut.SetParametersAndRender(p => p.AddCascadingValue<TimeZoneInfo?>("UserTimeZone", real));
    await Task.Yield();

    await _analytics.Received(1).GetDetectionAccuracyStatsAsync(
        Arg.Any<DateTimeOffset>(),
        Arg.Any<DateTimeOffset>(),
        "America/Chicago",
        Arg.Any<CancellationToken>());
}

[Test]
public async Task WarmCircuit_TimezoneAlreadySet_LoadsExactlyOnceOnInit()
{
    var real = TimeZoneInfo.FindSystemTimeZoneById("UTC");
    var cut = RenderComponent<PerformanceMetrics>(parameters => parameters
        .AddCascadingValue<TimeZoneInfo?>("UserTimeZone", real));
    await Task.Yield();

    await _analytics.Received(1).GetDetectionAccuracyStatsAsync(
        Arg.Any<DateTimeOffset>(),
        Arg.Any<DateTimeOffset>(),
        "UTC",
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 6: Run all PerformanceMetrics timezone tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~PerformanceMetricsTimezoneTests"`

Expected: PASS (3 tests)

---

## Task 5: Final verification + commit

- [ ] **Step 1: Run the entire ComponentTests project**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests`

Expected: all tests pass, no regression in other component tests.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build`

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor \
        TelegramGroupsAdmin/Components/Shared/Analytics/PerformanceMetrics.razor \
        TelegramGroupsAdmin.ComponentTests/Components/Analytics/MessageTrendsTimezoneTests.cs \
        TelegramGroupsAdmin.ComponentTests/Components/Analytics/PerformanceMetricsTimezoneTests.cs

git commit -m "$(cat <<'EOF'
fix(analytics): defer LoadData until timezone cascade resolves

Closes #406.

MessageTrends and PerformanceMetrics no longer fire LoadData() in
OnInitializedAsync with a TimeZoneInfo.Utc fallback. A new
OnParametersSetAsync gate calls LoadLast30Days() exactly once when the
UserTimeZone cascade transitions from null to a real value. Eliminates
the cold-circuit double-fetch and the UTC-bucketed first-render bug.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
