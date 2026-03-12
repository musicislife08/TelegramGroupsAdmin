---
description: Comprehensive multi-agent code review. Proactively suggest this after completing implementation of a planned task, before committing final changes, before merging branches, or before creating a pull request. If the user has just finished implementing a feature or fix and hasn't reviewed yet, suggest running this.
---

# Comprehensive Multi-Agent Code Review

Launch multiple specialized review agents in parallel to analyze current branch changes. All agents are **read-only** — no builds, no tests, no file modifications.

## Orchestration Steps

### Step 1: Gather Context

Run these commands to collect the information agents need:

1. `git diff develop...HEAD --name-only` — full list of changed files
2. `git diff develop...HEAD --stat` — compact summary of changes per file (insertions/deletions)
3. `git diff develop...HEAD --diff-filter=D --name-only` — list of deleted files (for Agent 7)
4. `git diff develop...HEAD` — full diff (only used by Agent 7, not sent to other agents)

Categorize changed files into:

- **cs_files**: All changed `.cs` files (excluding test files)
- **test_files**: Changed files matching `*.Tests*/**/*.cs` or `*.ComponentTests*/**/*.cs`
- **e2e_files**: Changed files matching `*.E2ETests*/**/*.cs` or containing "Playwright" in path
- **razor_files**: Changed `.razor` or `.razor.cs` files
- **config_files**: Changed `.csproj`, `Directory.Build.props`, `Program.cs`, DI registration files

Write a **1-2 sentence summary** of the changes based on the file names and diff stat. Describe the nature of the work (e.g., "project restructuring and class-to-record migration across the source generator", "new service implementation with tests", "bug fix in authentication flow"). This summary goes into every agent prompt.

### Step 1b: Check PR Size

If the total number of changed files exceeds **20**, the PR is too large for a single review pass. Instead of launching all agents at once:

1. Group the changed files into logical batches by area (e.g., "generator changes", "test updates", "project file changes")
2. Run a separate review pass for each batch — same agent lineup, but scoped to that batch's files
3. After all batches complete, compile a single unified summary across all passes

This prevents agents from being overwhelmed and produces more focused, higher-quality findings.

### Step 2: Launch Agents in Parallel

Launch ALL applicable agents in a **single message** with multiple Task tool calls.

**Skip any agent whose scoped file list is empty.** For example, if no `.cs` files changed, skip the Refactor Advisor and Concurrency Specialist. If no test files changed, skip the Test Specialist. Only launch agents that have work to do.

**Every agent prompt MUST include:**
- The **scoped file list** relevant to that agent (not all files — only files in their scope)
- The **diff stat** for those files (so they know the magnitude of changes)
- The **change summary** from Step 1
- The instruction: "You are in READ-ONLY review mode. Do NOT run `dotnet build`, `dotnet test`, or modify any files. Only read files and report findings."
- The instruction to read CLAUDE.md for project conventions
- The instruction to **read the actual files** using Read/Glob/Grep tools to understand the current state

**Do NOT paste the full `git diff` output into agents 1-6.** They should read files on demand for deeper context.

#### Agent 1: Dotnet Refactor Advisor (if cs_files or razor_files non-empty)
- **subagent_type**: `dotnet-refactor-advisor`
- **model**: `sonnet`
- **Scope**: Changed `.cs` and `.razor` files only (from cs_files + razor_files)
- **Focus**: Modern .NET 10/C# 14 patterns, code quality, architecture patterns, SOLID principles, DRY violations, naming conventions, extension method organization, proper use of records/primary constructors, collection expressions, pattern matching. Check for unused usings and unnecessary complexity.

#### Agent 2: Test Specialist — Review Mode (if cs_files or test_files non-empty)
- **subagent_type**: `nunit-test-writer`
- **model**: `sonnet`
- **Scope**: Changed production files + changed test files (from cs_files + test_files)
- **Prompt must include**: "You are in REVIEW MODE — do NOT write or modify tests."
- **Focus**: Evaluate test quality and coverage gaps:
  1. Are changed test files well-structured and following NUnit best practices?
  2. For each changed production file, are there corresponding tests? If not, what's missing?
  3. Are there edge cases or error paths in the changed code that lack test coverage?
  4. Do existing tests adequately cover the new/modified behavior?
- **Output**: List of test gaps with severity (critical gap vs nice-to-have)

#### Agent 3: Security Reviewer (if any files changed)
- **subagent_type**: `security-reviewer`
- **Scope**: All changed files (broadest scope — security issues can hide anywhere)
- **Focus**: OWASP top 10, injection risks, XSS in Blazor, CSRF, authentication/authorization gaps, sensitive data exposure, API key handling, input validation at system boundaries, exception messages leaking internals

#### Agent 4: Concurrency Specialist (if cs_files non-empty)
- **subagent_type**: `dotnet-skills:dotnet-concurrency-specialist`
- **model**: `sonnet`
- **Scope**: Changed `.cs` files only (from cs_files)
- **Focus**: Race conditions, thread safety of shared state, async/await anti-patterns (fire-and-forget, sync-over-async, async void), CancellationToken propagation, potential deadlocks, lock ordering issues, concurrent collection usage

#### Agent 5: Playwright E2E Tester (if e2e_files non-empty)
- **subagent_type**: `playwright-e2e-tester`
- **Scope**: Changed E2E test files only (from e2e_files)
- **Focus**: Review only — evaluate test quality, selector strategies, wait patterns, flaky test risks, Page Object Model usage, proper assertion patterns

#### Agent 6: Your Choice (CONDITIONAL — based on changed files)
- **Decision**: At runtime, examine the changed files and determine if there is a domain that other agents don't adequately cover. Pick the most appropriate agent from the available agent types. Examples of when to pick what:
  - `.razor` / UI component changes → `ux-reviewer` (MudBlazor patterns, UX layout, information hierarchy — do NOT review accessibility/ARIA/screen readers)
  - Akka.NET actor changes → `dotnet-skills:akka-net-specialist`
  - DocFX / documentation changes → `dotnet-skills:docfx-specialist`
  - Performance-sensitive code paths → `dotnet-skills:dotnet-performance-analyst`
  - If nothing warrants a 6th agent, skip it — don't force a pick

#### Agent 7: Dead Code Hunter (if deleted files exist OR diff contains removed methods/classes)
- **subagent_type**: `general-purpose`
- **Scope**: Deleted files and removed code (from the full diff, filtered to deletions only)
- **Focus**: This agent receives the list of **deleted files** and a summary of **removed methods/classes/interfaces** extracted from the diff. Its job is to trace references and find anything that is now orphaned:
  1. For each deleted file: search for imports, usages, and references across the codebase
  2. For each removed public/internal method or type: search for call sites that no longer exist
  3. Flag any methods, classes, interfaces, or using statements that were only referenced by the deleted code
  4. Check for stale documentation references (csproj descriptions, comments mentioning removed functionality)
- **Output**: List of now-dead code with file paths and line numbers
- **Note**: This agent DOES receive relevant portions of the diff (deletions) since it needs to know what was removed. Keep it focused — only the deleted content, not the full diff.
- **Skip condition**: If no files were deleted AND the diff contains no removed method/class/interface signatures, skip this agent entirely.

### Step 3: Compile Results

After ALL agents complete, compile a unified review summary:

```
## Comprehensive Review Summary

### Agent Results
| Agent | Findings | Critical | Suggestions |
|-------|----------|----------|-------------|
(only include rows for agents that ran)

### Critical Issues (must fix before PR)
{Consolidated list from all agents}

### Suggestions (consider for this PR)
{Consolidated list from all agents}

### Things Done Well
{Positive highlights from agents}

### Overall Verdict
APPROVED / APPROVED (with suggestions) / NEEDS CHANGES
```

## Important Rules

- **All agents are read-only** — no builds, no tests, no file modifications
- **Launch agents in parallel** — use a single message with multiple Task tool calls
- **Skip agents with empty scopes** — don't launch agents that have no relevant files to review
- **Scoped context per agent** — each agent gets only the file list and diff stat relevant to their concern, not the full diff
- **Agents read on demand** — they use Read/Glob/Grep tools to examine file contents, rather than having the full diff pasted into their prompt
- **Exception: Agent 7** receives deletion-related diff content because it needs to know what was removed
- **Deduplicate findings** — if multiple agents flag the same issue, consolidate into one finding
- **Confidence filtering** — only report medium-to-high confidence findings
- **Respect CLAUDE.md** — project conventions override generic advice
- **No time estimates** — never include time predictions in the output
