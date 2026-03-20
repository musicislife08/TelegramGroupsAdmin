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

## Milestone: v1.2 — SaaS Hosting Readiness

**Shipped:** 2026-03-20
**Phases:** 4 | **Plans:** 4

### What Was Built
- ClamAV env var override (CLAMAV_HOST/CLAMAV_PORT) — operators point instances at shared daemon without DB pre-seeding
- `--bootstrap` CLI flag — headless Owner account creation for K8s init container pattern with idempotent retry
- `ENABLE_METRICS` env var — decoupled Prometheus /metrics from full OTEL/Seq stack
- Compose file alignment — fixed deployment examples to match code's Environment.GetEnvironmentVariable calls

### What Worked
- Three independent features with no cross-dependencies — phases 9/10/11 could execute in any order
- TDD approach for phases 9 and 10 caught edge cases early (e.g., ClamAV health check missing host:port in exception path)
- Milestone audit caught the compose env var mismatch (CLAMAV__HOST vs CLAMAV_HOST) that would have been a silent deployment bug
- Discussion-driven pivot from IOptions to simple compose fix saved significant complexity — user caught that IOptions was over-engineering

### What Was Inefficient
- Initial IOptions refactor plan (Phase 12 v1) went through full research → plan → verify cycle before being pivoted to a 2-file rename — the discuss-phase step should have happened first
- The compose env var naming mismatch (single vs double underscore) should have been caught during Phase 9 planning, not post-milestone audit
- Phase 11 REQUIREMENTS.md had stale SEQ_URL references that should have been caught during requirements definition

### Patterns Established
- For env var overrides: use raw Environment.GetEnvironmentVariable when only overriding a subset of a config class's properties
- ASP.NET Core double-underscore convention (IConfiguration binding) is distinct from single-underscore raw env vars — compose files must use whichever the code actually reads
- Milestone audit → gap closure → re-audit cycle works well for catching deployment documentation gaps

### Key Lessons
1. Always validate compose/deployment examples against the actual code during phase verification, not just after milestone audit
2. IOptions<T> is wrong when you only need 2 of N fields from a config class — the other fields (Enabled, TimeoutSeconds) must come from the database
3. The simplest fix (rename 2 strings in compose files) beat the "architecturally correct" fix (IOptions refactor) because the code was already right
4. discuss-phase before plan-phase prevents wasted planning cycles on the wrong approach

### Cost Observations
- Model mix: ~15% opus (orchestrator), ~85% sonnet (researchers, planners, checkers, executors, verifiers)
- Notable: Phase 12 pivoted twice (IOptions refactor → IOptions with nullable defaults → simple compose rename) before landing on the right answer

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | 1 | 5 | First GSD milestone — established dead code removal patterns |
| v1.2 | 2 | 4 | First feature milestone — milestone audit caught deployment gap, discuss-phase pivot saved complexity |

### Cumulative Quality

| Milestone | Tests | Coverage | Deviations |
|-----------|-------|----------|------------|
| v1.0 | 1747 | - | 3 auto-fixes |
| v1.2 | 1803 | - | 2 auto-fixes (Phase 9), 1 plan pivot (Phase 12) |

### Top Lessons (Verified Across Milestones)

1. Phase verification with `dotnet build` catches what grep-based audits miss
2. Parallel agents must not share build artifacts (bin/obj directories)
3. Milestone audit catches deployment/integration gaps that phase-level verification misses
4. discuss-phase before plan-phase prevents wasted planning on wrong approaches
5. The simplest fix that works is usually the right one — resist over-engineering
