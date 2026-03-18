---
phase: 08-frontend-fixes
plan: "01"
subsystem: analytics
tags: [analytics, bug-fix, tdd, blazor, ef-core]
dependency_graph:
  requires: []
  provides:
    - week-over-week growth always computed from endDate regardless of startDate
    - DailyAverageGrowthPercent as independent metric on WeekOverWeekGrowth
    - All Time view hides growth chips entirely
  affects:
    - TelegramGroupsAdmin/Repositories/MessageStatsService.cs
    - TelegramGroupsAdmin/Models/Analytics/WeekOverWeekGrowth.cs
    - TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor
tech_stack:
  added:
    - Microsoft.EntityFrameworkCore.InMemory 10.0.0 (unit test support)
  patterns:
    - TDD (RED → GREEN) with EF Core InMemory provider
    - Separate DB queries for growth calculation window independent of view filter
key_files:
  created:
    - TelegramGroupsAdmin.UnitTests/Services/MessageStatsServiceTests.cs
  modified:
    - TelegramGroupsAdmin/Repositories/MessageStatsService.cs
    - TelegramGroupsAdmin/Models/Analytics/WeekOverWeekGrowth.cs
    - TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor
    - Directory.Packages.props
    - TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj
decisions:
  - "Growth queries run independently from the startDate filter window — two fresh DB queries for [endDate-14d, endDate-7d] and [endDate-7d, endDate] rather than filtering distinctMessages in-memory"
  - "hasPreviousPeriod determined by actual presence of messages in previous-week window, not by daysDiff >= 14"
  - "DailyAverageGrowthPercent formula: (currentAvg - previousAvg) / previousAvg * 100 where avg = count/7. Algebraically equivalent to MessageGrowthPercent in a symmetric window but architecturally independent."
  - "InMemory EF Core provider chosen for unit tests; avoids Testcontainers for fast pure-logic tests"
metrics:
  duration: "8 minutes"
  completed: "2026-03-17"
  tasks_completed: 2
  files_modified: 6
---

# Phase 8 Plan 01: Analytics Growth Percentage Fix Summary

One-liner: Fixed 7-day analytics view to always show week-over-week growth by decoupling growth computation from the startDate range filter, added DailyAverageGrowthPercent, and hidden chips on All Time view.

## What Was Done

### Task 1: Fix growth calculation and add DailyAverageGrowthPercent (TDD)

**Root cause:** `MessageStatsService.GetMessageTrendsAsync` computed `hasPreviousPeriod` as `daysDiff >= 14` where `daysDiff = endDate - startDate`. For the 7-day view, `daysDiff = 7 < 14`, so `WeekOverWeekGrowth` was always null — no growth shown even when the database had 14+ days of data.

**Fix:** Replaced the `daysDiff` gate with an actual DB query. The service now runs a lightweight query against `[endDate-14d, endDate-7d]` to check whether any messages exist in the previous week window. If messages exist there, growth is computed using two independent queries (current and previous week), completely independent of `startDate`.

**DailyAverageGrowthPercent:** Added as a new property on `WeekOverWeekGrowth`. Formula: `(currentWeekMessages/7 - previousWeekMessages/7) / (previousWeekMessages/7) * 100`. Algebraically equivalent to `MessageGrowthPercent` in the symmetric 7-day window but properly named and architecturally independent — the Daily Average card now shows daily-average-specific growth.

**TDD cycle:**
- RED: Tests 1 and 4 failed (`WeekOverWeekGrowth` was null for 7-day range)
- GREEN: All 5 tests pass after fix

### Task 2: Update MessageTrends.razor chips

- Added `_currentView` field (`"7d"`, `"30d"`, `"all"`) set in each Load method
- Added `_currentView != "all"` guard to all four overview card chips (Total Messages, Daily Average, Active Users, Spam Rate) — chips are hidden entirely when viewing All Time
- Daily Average card changed from `MessageGrowthPercent` to `DailyAverageGrowthPercent`

## Verification Results

- `dotnet build` — 0 warnings, 0 errors
- `dotnet test TelegramGroupsAdmin.UnitTests --filter "MessageStatsService"` — 5/5 pass
- `dotnet test TelegramGroupsAdmin.UnitTests` — 1752/1752 pass

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written with one test assertion adjustment.

**Test seed data correction (Rule 1 - Bug):** The initial seed for Test 4 had messages landing in the wrong time windows (timestamps at `Now.AddDays(-(i-1)).AddHours(-1)` crossed the 7-day boundary). Fixed to use `Now.AddHours(-(6 + i*12))` for clear 12-hour-interval timestamps firmly within [endDate-7d, endDate]. This was a test correctness fix, not a production code change.

## Self-Check: PASSED

All files verified present:
- FOUND: TelegramGroupsAdmin/Repositories/MessageStatsService.cs
- FOUND: TelegramGroupsAdmin/Models/Analytics/WeekOverWeekGrowth.cs
- FOUND: TelegramGroupsAdmin/Components/Shared/Analytics/MessageTrends.razor
- FOUND: TelegramGroupsAdmin.UnitTests/Services/MessageStatsServiceTests.cs

All commits verified:
- FOUND: 939a3288 (test: RED phase)
- FOUND: f0eea9d2 (feat: GREEN phase - service fix)
- FOUND: 5f44aede (feat: Task 2 - Razor chip fixes)
