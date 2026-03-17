# Phase 8: Frontend Fixes - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix two Blazor UI correctness issues: analytics percentage calculations that don't display correctly per time range (#384), and timezone JS interop that fails during prerendering (#203). No new features, no API changes beyond what's needed for the fixes.

</domain>

<decisions>
## Implementation Decisions

### Analytics growth percentages (FRONT-01, #384)
- Growth percentages are **always week-over-week** comparisons (this week vs last week) — they do NOT change based on the time range filter
- The time range filter (7 days, 30 days, All Time) controls what data the cards display, not how growth is calculated
- **7-day view bug**: Growth percentages are missing entirely (should show week-over-week comparison)
- **Daily Average card bug**: Reuses `MessageGrowthPercent` instead of having its own `DailyAverageGrowthPercent` calculation
- **All Time view**: Should hide the percentage chips entirely (no meaningful comparison period)
- **Keep `WeekOverWeekGrowth` name** — it IS week-over-week, the name is correct
- Fix any incorrect growth calculation logic in `MessageStatsService.GetMessageTrendsAsync()` to ensure the math is correct

### Timezone detection (FRONT-02, #203)
- **Root layout cascade approach**: Detect timezone once in `MainLayout.razor` (or equivalent root) via JS interop in `OnAfterRenderAsync(firstRender)`
- Store the detected `TimeZoneInfo` as a `[CascadingParameter]` available to all child components
- **C# does all datetime conversion** — no JS DOM overwriting. Components receive the timezone and use `TimeZoneInfo.ConvertTime()` in C#
- **Reuse existing `LocalTimestamp` component** — update it to accept the cascaded `TimeZoneInfo` instead of using `Value.ToLocalTime()` (which uses server timezone)
- Remove the `data-utc` attribute and any JS that overwrites rendered timestamps client-side
- Remove timezone detection from individual components (`MessageTrends.razor:403`, `PerformanceMetrics.razor:330`, `AlgorithmTuning.razor:256`) — the root layout handles it
- During prerender (no JS available): times show as UTC briefly, then re-render with local time once SignalR connects and timezone is detected — acceptable since root layout fires before user navigates to timestamp-heavy pages
- UTC fallback if JS interop fails (e.g., privacy extensions blocking JS)

### Testing strategy
- **FRONT-01**: Unit tests for `MessageStatsService` growth calculations (verify week-over-week math, DailyAverage has its own calc, All Time returns null growth) AND component tests for `MessageTrends.razor` verifying chips show/hide correctly per time range
- **FRONT-02**: One small E2E test using existing auth bypass pattern (backend-created cookie injected, no login flow) — verify timezone cascade renders local times correctly in a child component

### Claude's Discretion
- Exact implementation of the cascading parameter (CascadingValue in MainLayout vs a scoped service)
- Whether to use `TimeZoneInfo` or just a timezone string (IANA ID) as the cascaded value
- How to handle the `LocalTimestamp` component when timezone is null (UTC fallback rendering)
- Whether `MessageStatsService` needs a separate method for DailyAverage growth or extends the existing one

</decisions>

<specifics>
## Specific Ideas

- User wants C# to do all timezone conversion — not JS overwriting the DOM after render
- `LocalTimestamp` component already exists and is widely used (UserDetailDialog, Users page) — it's the natural integration point for the cascaded timezone
- The current `LocalTimestamp` uses `ToLocalTime()` (server timezone) + `data-utc` attribute for JS to overwrite — this entire pattern gets replaced
- Growth percentages being missing on 7-day view is the primary user-visible bug, not that they need to change with the filter

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `LocalTimestamp.razor` — existing component at `Components/Shared/LocalTimestamp.razor`, used across the app. Currently uses server-side `ToLocalTime()` + JS overwrite pattern. Update to use cascaded `TimeZoneInfo` for C#-side conversion.
- `MessageStatsService.GetMessageTrendsAsync()` — existing service with growth calculation logic at lines 318-366
- `WeekOverWeekGrowth.cs` — existing model at `Models/Analytics/WeekOverWeekGrowth.cs`
- `AnalyticsConstants.cs` — `CurrentWeekLookbackDays = -7` hardcoded constant

### Established Patterns
- 3 components currently do their own timezone detection in `OnInitializedAsync`: `MessageTrends.razor:403`, `PerformanceMetrics.razor:330`, `AlgorithmTuning.razor:256`
- Existing E2E tests use auth bypass via backend-created cookie — follow same pattern for timezone E2E test
- Component tests use bUnit — follow existing patterns in `ComponentTests` project

### Integration Points
- `MainLayout.razor` (or `App.razor`) — root component where timezone detection will live
- `LocalTimestamp.razor` — receives cascaded timezone, all datetime-displaying components use it
- `MessageTrends.razor` lines 68-139 — overview cards with percentage chips

</code_context>

<deferred>
## Deferred Ideas

- Issue #388 (Make LocalTimestamp usable in non-markup contexts like snackbar, dialogs, logs) — related but separate enhancement, not a bug fix
- Server-side timezone caching per user session — could avoid the brief UTC flash on prerender, but adds complexity

</deferred>

---

*Phase: 08-frontend-fixes*
*Context gathered: 2026-03-17*
