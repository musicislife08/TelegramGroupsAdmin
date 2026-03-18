---
phase: 08-frontend-fixes
plan: "02"
subsystem: frontend
tags: [blazor, timezone, cascading-parameter, js-interop, bug-fix]
dependency_graph:
  requires:
    - 08-01 (MessageTrends.razor modified by analytics fix)
  provides:
    - MainLayout cascades TimeZoneInfo? to all child components via CascadingValue
    - LocalTimestamp uses C# TimeZoneInfo.ConvertTime() — no JS DOM overwrite
    - Three analytics/content-detection components receive timezone via CascadingParameter
  affects:
    - TelegramGroupsAdmin/Components/Layout/MainLayout.razor
    - TelegramGroupsAdmin/Components/Shared/LocalTimestamp.razor
    - TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor
    - TelegramGroupsAdmin/Components/Shared/Analytics/PerformanceMetrics.razor
    - TelegramGroupsAdmin/Components/Shared/ContentDetection/StopWordRecommendations.razor
    - TelegramGroupsAdmin/wwwroot/js/app.js
tech_stack:
  added: []
  patterns:
    - CascadingValue<TimeZoneInfo?> at root layout level for app-wide timezone availability
    - TimeZoneInfo.ConvertTime() in C# replacing JS DOM overwrite pattern
    - OnAfterRenderAsync(firstRender) for safe JS interop after SignalR connects
key_files:
  created: []
  modified:
    - TelegramGroupsAdmin/Components/Layout/MainLayout.razor
    - TelegramGroupsAdmin/Components/Shared/LocalTimestamp.razor
    - TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor
    - TelegramGroupsAdmin/Components/Shared/Analytics/PerformanceMetrics.razor
    - TelegramGroupsAdmin/Components/Shared/ContentDetection/StopWordRecommendations.razor
    - TelegramGroupsAdmin/wwwroot/js/app.js
decisions:
  - "Timezone detected once in MainLayout.OnAfterRenderAsync(firstRender) — only fires after SignalR connects, eliminating JSException during server prerender"
  - "CascadingValue<TimeZoneInfo?> wraps CascadingValue<WebUserIdentity?> — outer cascade means TimeZoneInfo is available to all children including LocalTimestamp"
  - "LocalTimestamp falls back to Value (UTC) when UserTimeZone is null (prerender state) — acceptable brief UTC display before cascade resolves"
  - "TimeZoneInfo object cascaded (not IANA string) — analytics components extract .Id when service methods require string, eliminating per-component FindSystemTimeZoneById calls"
  - "StopWordRecommendations.OnAfterRenderAsync removed entirely — timezone detection was its only purpose"
metrics:
  duration: "7 minutes"
  completed: "2026-03-17"
  tasks_completed: 2
  files_modified: 6
---

# Phase 8 Plan 02: Timezone Prerender Fix Summary

One-liner: Root layout cascade pattern — detect timezone once in MainLayout.OnAfterRenderAsync(firstRender), cascade TimeZoneInfo to all children, LocalTimestamp uses C# TimeZoneInfo.ConvertTime() instead of JS DOM overwriting.

## What Was Done

### Task 1: Add timezone detection to MainLayout and cascade TimeZoneInfo

**Root cause:** Three components called `getUserTimeZone` via JS interop in `OnInitializedAsync` (or `OnAfterRenderAsync`). During Blazor server-side prerendering, the SignalR circuit is not yet established, so JS interop throws JSException for first-time page loads. Additionally, `LocalTimestamp.razor` used `Value.ToLocalTime()` (server timezone) plus a JS MutationObserver that overwrote the rendered text client-side — fragile and incorrect.

**MainLayout.razor changes:**
- Added `@inject IJSRuntime JsRuntime`
- Added `private TimeZoneInfo? _userTimeZone` field
- Added `OnAfterRenderAsync(bool firstRender)` override that calls `getUserTimeZone` and calls `StateHasChanged()` to trigger cascade propagation
- Wrapped the existing `<CascadingValue Value="_webUser">` with an outer `<CascadingValue Value="_userTimeZone">` — outer because it must be available to LocalTimestamp at all nesting levels

**LocalTimestamp.razor changes:**
- Added `[CascadingParameter] public TimeZoneInfo? UserTimeZone { get; set; }`
- Replaced `<span class="local-timestamp" data-utc="@Value.ToString("o")">@Value.ToLocalTime().ToString(Format)</span>` with C# `TimeZoneInfo.ConvertTime()` rendering — no `data-utc` attribute, no `local-timestamp` CSS class, no JS targeting needed
- Before timezone resolves (prerender): shows UTC. After MainLayout.OnAfterRenderAsync fires and cascades down: re-renders with local time.

**app.js changes:**
- Removed `formatLocalTimestamp()` function (~30 lines)
- Removed `initializeTimestamps()` function (~10 lines)
- Removed `debounce()` helper (was only used for timestamp initialization)
- Removed `MutationObserver` and all its supporting code (~25 lines)
- Removed `DOMContentLoaded` handler for timestamp initialization
- Retained `getUserTimeZone` (still called from MainLayout)

**StopWordRecommendations.razor (bonus fix):** Inline `local-timestamp` spans in the markup for `AnalysisPeriodStart`/`AnalysisPeriodEnd` were replaced with `<LocalTimestamp>` components to use the cascade. This was logically part of the same fix and committed together.

### Task 2: Remove per-component timezone detection, use cascaded TimeZoneInfo

**MessageTrends.razor:**
- Removed `@inject IJSRuntime JsRuntime` (was only used for timezone)
- Removed `private string _userTimeZone = "UTC"` field
- Removed the timezone detection try/catch block from `OnInitializedAsync`
- Added `[CascadingParameter] public TimeZoneInfo? UserTimeZone { get; set; }`
- In `LoadData()`: `var timeZoneId = (UserTimeZone ?? TimeZoneInfo.Utc).Id` — extracted once, passed to both `GetMessageTrendsAsync` and `GetSpamTrendComparisonAsync`

**PerformanceMetrics.razor:**
- Same pattern: removed injection, field, detection block
- Added `[CascadingParameter] public TimeZoneInfo? UserTimeZone { get; set; }`
- All five analytics repository calls now use the cascaded timezone ID

**StopWordRecommendations.razor:**
- Removed `@inject IJSRuntime JsRuntime`
- Removed `private string _userTimeZone = "UTC"` field
- Removed the entire `OnAfterRenderAsync` override (timezone detection was its only purpose)
- Added `[CascadingParameter] public TimeZoneInfo? UserTimeZone { get; set; }`
- `GenerateRecommendationsAsync`: replaced `TimeZoneInfo.FindSystemTimeZoneById(_userTimeZone)` with `UserTimeZone ?? TimeZoneInfo.Utc` — already a TimeZoneInfo, no lookup needed

## Verification Results

- `dotnet build` — 0 warnings, 0 errors
- `grep -r "getUserTimeZone" Components/` — only MainLayout.razor (correct)
- `LocalTimestamp.razor` — no `data-utc` attribute, no `local-timestamp` class, no JS targeting
- `app.js` — has `getUserTimeZone`, no `initializeTimestamps`, no `formatLocalTimestamp`, no MutationObserver
- All three components (MessageTrends, PerformanceMetrics, StopWordRecommendations) have `[CascadingParameter] TimeZoneInfo?`

## Deferred Issues

**Other components using old local-timestamp + data-utc pattern** (pre-existing, not caused by this plan):

The following components still use inline `<span class="local-timestamp" data-utc="...">@....ToLocalTime()...</span>` markup. Since `initializeTimestamps` has been removed, these now show server timezone (same as before the JS overwrite — no regression, just not fixed). These are candidates for a follow-up fix-as-found pass:

- `ProfileScanHistoryDialog.razor`
- `EditHistoryDialog.razor`
- `ImageViewerDialog.razor`
- `BackupBrowser.razor`
- `DetectionHistoryDialog.razor`
- `BackupRestore.razor`
- `RestoreBackupModal.razor`
- `ChatConfigModal.razor`
- `MessageBubbleTelegram.razor`
- `UserNotesDialog.razor`
- `ExamReviewCard.razor`
- `ModerationReportCard.razor`
- `ProfileScanAlertCard.razor`
- `ImpersonationAlertCard.razor`

All of these should be converted to `<LocalTimestamp>` components (which now correctly uses the cascade). Logged to deferred-items.

## Deviations from Plan

### Auto-fixed Issues

**[Rule 2 - Missing Critical Functionality] StopWordRecommendations inline timestamps converted to `<LocalTimestamp>`**
- **Found during:** Task 1
- **Issue:** The component had two inline `<span class="local-timestamp" data-utc="...">` elements for analysis period display — these would be orphaned by removing `initializeTimestamps` from app.js
- **Fix:** Replaced with `<LocalTimestamp Value="..." Format="MMM d, yyyy" />` components that use the cascade
- **Files modified:** `StopWordRecommendations.razor` (committed with Task 1)

## Self-Check: PASSED
