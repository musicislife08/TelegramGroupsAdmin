# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — Dead Code Removal

**Shipped:** 2026-03-17
**Phases:** 5 | **Plans:** 8 | **Sessions:** 1

### What Was Built
- Removed ~30 dead files across 6 projects (Core, Configuration, Data, Telegram, ContentDetection, main app)
- Removed 22 dead methods from 10 live interfaces/classes
- Removed 4 dead properties, 1 dead enum value, 2 dead DI registrations
- Fixed 2 stale comments, cleaned up 17+ orphaned tests
- Net -636 lines, 1747 tests passing

### What Worked
- Skipping research phase for a well-documented issue saved significant tokens
- Parallel plan execution in Phases 3 and 5 (no file overlap) cut execution time
- Phase-by-phase verification caught cascading issues early (stale mock setups, missed set-sites)
- The original 5-agent dead code audit (issue #396) was thorough — 10.1% false positive rejection rate meant minimal surprises during execution

### What Was Inefficient
- Phase 4 wave ordering was wrong — tests that reference deleted Razor components can't be in a later wave; the executor had to auto-fix by absorbing Wave 2 into Wave 1
- Phase 5 missed a `ContentCheckRequest.ImageFileName` set-site in a Razor file — the plan checker verified ContentDetection call sites but didn't catch a main-app Razor usage
- Plan 04-02 created unnecessarily — could have been one plan since test deletion must happen in same pass as component deletion

### Patterns Established
- For dead code removal: organize phases by project layer (foundation → downstream)
- For parallel executor agents: explicitly prohibit `dotnet build`/`dotnet test` in agent prompts
- Auto-fix deviations are expected when removing interface methods — mock setups cascade

### Key Lessons
1. Dead method removal cascades further than static analysis shows — mock setups, write-only property assignments, and Razor file references are all "callers" that don't appear in usage analysis
2. Sequential waves for "delete code then delete tests" don't work when the tests reference the deleted code — plan them together
3. A well-verified issue manifest (like #396) can substitute entirely for the research phase

### Cost Observations
- Model mix: ~20% opus (orchestrator), ~80% sonnet (planners, checkers, executors, verifiers)
- Sessions: 1 continuous session
- Notable: Entire 62-item cleanup planned, executed, verified, PR'd, and merged in a single session

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | 1 | 5 | First GSD milestone — established dead code removal patterns |

### Cumulative Quality

| Milestone | Tests | Coverage | Deviations |
|-----------|-------|----------|------------|
| v1.0 | 1747 | - | 3 auto-fixes |

### Top Lessons (Verified Across Milestones)

1. Phase verification with `dotnet build` catches what grep-based audits miss
2. Parallel agents must not share build artifacts (bin/obj directories)
