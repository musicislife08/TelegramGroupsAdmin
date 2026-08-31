# #406 — Analytics: defer initial load until timezone cascade resolves

Closes #406

## Problem

`MessageTrends.razor` and `PerformanceMetrics.razor` call `LoadData()` in `OnInitializedAsync`, which runs before `MainLayout.OnAfterRenderAsync(firstRender)` detects the user's timezone via JS interop. On a cold-circuit visit, `UserTimeZone` is still null, so both components fall back to `TimeZoneInfo.Utc` and bucket their analytics data by UTC days instead of local days. Charts only correct themselves when the user clicks a time-range button.

Full diagnosis and lifecycle walk-through: [issue #406](https://github.com/musicislife08/TelegramGroupsAdmin/issues/406).

## Approach: defer until ready

Skip the initial `LoadData()` call when `UserTimeZone` is null. Add `OnParametersSetAsync` that fires `LoadData()` **exactly once**, gated on the cascade flipping from null to a real value.

Rationale: the analytics aggregations are expensive (multiple parallel timezone-bucketed queries against `messages`, `audit_log`, and detection tables). A "load with UTC, then re-load on cascade" pattern would double the DB workload on every cold visit. Loading once with the correct timezone is the right ergonomic and cost trade.

Rejected alternatives:
- Reactive re-load (load with UTC, refetch on cascade) — doubles DB work on cold visits.
- Block until timezone resolves — larger refactor, breaks prerender, no benefit over deferred init.

## Implementation

Both components use the same pattern.

**`OnInitializedAsync`** — drop the unconditional `LoadData()` call. Keep all the synchronous setup (e.g., `_accessibleChats` lookup in `MessageTrends`). If `UserTimeZone` is already populated (warm circuit, navigating between pages), call `LoadLast30Days()` immediately. Otherwise wait.

**`OnParametersSetAsync`** — track the previously-observed `UserTimeZone` in a private field. When the cascade transitions from null to a non-null value AND `_dataLoaded` is false, fire `LoadLast30Days()`. After that, never re-fire from this hook.

```csharp
private TimeZoneInfo? _seenTimeZone;

protected override async Task OnParametersSetAsync()
{
    if (_seenTimeZone is null && UserTimeZone is not null && !_dataLoaded)
    {
        _seenTimeZone = UserTimeZone;
        await LoadLast30Days();
    }
}
```

**Remove the fallback** in `LoadData()`:

```csharp
// before
var timeZoneId = (UserTimeZone ?? TimeZoneInfo.Utc).Id;

// after — UserTimeZone is guaranteed non-null by the caller gate
var timeZoneId = UserTimeZone!.Id;
```

The bang operator is justified because all entry points (`LoadLast7Days`, `LoadLast30Days`, `LoadAllTime`, `OnParametersSetAsync` gate) only fire after `UserTimeZone` is set.

### Failure safety

`MainLayout.OnAfterRenderAsync` already sets `_userTimeZone = TimeZoneInfo.Utc` in the JS interop catch block (line 139), so the cascade is guaranteed to flip to a non-null value within one render cycle — even if browser timezone detection fails. The components will load; they just bucket by UTC in the JS-broken case (which is the correct fallback).

### `PerformanceMetrics` has no WebUser dependency

Its `OnInitializedAsync` only calls `LoadLast30Days()`. Drop the call entirely; the gate in `OnParametersSetAsync` covers both cold and warm circuits.

### `MessageTrends` has a WebUser dependency

Keep the `_accessibleChats` lookup in `OnInitializedAsync` (it depends on `WebUser`, not timezone). Move only the `LoadLast30Days()` call behind the gate.

## Files

- `TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor` — line 427 (`OnInitializedAsync`) and line 483 (UTC fallback in `LoadData`)
- `TelegramGroupsAdmin/Components/Shared/Analytics/PerformanceMetrics.razor` — line 324 (`OnInitializedAsync`) and line 356 (UTC fallback in `LoadData`)

## Tests

bUnit component tests (see `tga_feedback_component_test_scope` — Blazor component tests cover only the component's own logic):

- Cold-circuit case: render with `UserTimeZone = null`, assert no service calls fired. Set the cascade to a real `TimeZoneInfo`, assert exactly one `LoadData()`-equivalent service call fires with the right `timeZoneId`.
- Warm-circuit case: render with `UserTimeZone` already set, assert exactly one service call fires immediately on init.
- Re-cascade is idempotent: after initial load, set `UserTimeZone` to a *different* value (simulates user switching timezones mid-session — out of scope for #406 but a useful regression guard). Confirm no additional auto-fire from `OnParametersSetAsync`. (Behavior we want: only the explicit time-range buttons re-load.)

Mock both `MessageStatsService` and `AnalyticsRepository`.

## Acceptance Criteria

- [ ] On cold-circuit first visit, the daily-volume chart renders with local-day buckets (not UTC) without the user clicking anything.
- [ ] Exactly one set of data-loading service calls fires per page load (verified via test mock call count).
- [ ] Existing time-range button behavior (`LoadLast7Days` / `LoadLast30Days` / `LoadAllTime`) is unchanged.
- [ ] If JS timezone detection fails (`MainLayout` catch path), the components still load — bucketed by UTC, which matches user-perceived behavior in that failure mode.

## Out of Scope

- Reworking the `MainLayout` timezone detection itself (already a `OnAfterRenderAsync` JS interop call; see #203 for the prior fix that put it there).
- Other analytics components or pages that may have the same pattern — limit this fix to the two components called out in the issue. Audit during PR if grep turns up siblings.
