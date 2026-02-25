# Comprehensive Multi-Agent Code Review

Launch multiple specialized review agents in parallel to analyze current branch changes. All agents are **read-only** — no builds, no tests, no file modifications.

## Orchestration Steps

### Step 1: Gather Changed Files

Run `git diff develop...HEAD --name-only` to get the full list of changed files. Categorize them into:

- **cs_files**: All changed `.cs` files (excluding test files)
- **test_files**: Changed files matching `*.Tests*/**/*.cs` or `*.ComponentTests*/**/*.cs`
- **e2e_files**: Changed files matching `*.E2ETests*/**/*.cs` or containing "Playwright" in path
- **razor_files**: Changed `.razor` or `.razor.cs` files
- **config_files**: Changed `.csproj`, `Directory.Build.props`, `Program.cs`, DI registration files

Also run `git diff develop...HEAD` to get the full diff content for agents that need it.

### Step 2: Launch Agents in Parallel

Launch ALL applicable agents in a **single message** with multiple Task tool calls. Every agent prompt MUST include:
- The full list of changed files
- The instruction: "You are in READ-ONLY review mode. Do NOT run `dotnet build`, `dotnet test`, or modify any files. Only read files and report findings."
- The instruction to read CLAUDE.md for project conventions

#### Agent 1: Dotnet Refactor Advisor (ALWAYS)
- **subagent_type**: `dotnet-refactor-advisor`
- **Scope**: All changed `.cs` and `.razor` files
- **Focus**: Modern .NET 10/C# 14 patterns, code quality, architecture patterns, SOLID principles, DRY violations, naming conventions, extension method organization, proper use of records/primary constructors, collection expressions, pattern matching. Check for dead code, unused usings, and unnecessary complexity.

#### Agent 2: NUnit Test Writer — Review Mode (ALWAYS)
- **subagent_type**: `nunit-test-writer`
- **Scope**: All changed files (both production code and test code)
- **Focus**: Review mode only — do NOT write tests. Evaluate:
  1. Are changed test files well-structured and following NUnit best practices?
  2. For each changed production file, are there corresponding tests? If not, what's missing?
  3. Are there edge cases or error paths in the changed code that lack test coverage?
  4. Do existing tests adequately cover the new/modified behavior?
- **Output**: List of test gaps with severity (critical gap vs nice-to-have)

#### Agent 3: Security Reviewer (ALWAYS)
- **subagent_type**: `security-reviewer`
- **Scope**: All changed files
- **Focus**: OWASP top 10, injection risks, XSS in Blazor, CSRF, authentication/authorization gaps, sensitive data exposure, API key handling, input validation at system boundaries, exception messages leaking internals

#### Agent 4: Concurrency Specialist (ALWAYS)
- **subagent_type**: `dotnet-skills:dotnet-concurrency-specialist`
- **Scope**: All changed `.cs` files
- **Focus**: Race conditions, thread safety of shared state, async/await anti-patterns (fire-and-forget, sync-over-async, async void), CancellationToken propagation, potential deadlocks, lock ordering issues, concurrent collection usage

#### Agent 5: Playwright E2E Tester (CONDITIONAL — only if e2e_files is non-empty)
- **subagent_type**: `playwright-e2e-tester`
- **Scope**: Changed E2E test files
- **Focus**: Review only — evaluate test quality, selector strategies, wait patterns, flaky test risks, Page Object Model usage, proper assertion patterns

#### Agent 6: Your Choice (CONDITIONAL — based on changed files)
- **Decision**: At runtime, examine the changed files and determine if there is a domain that Agents 1-5 don't adequately cover. Pick the most appropriate agent from the available agent types. Examples of when to pick what:
  - `.razor` / UI component changes → `ux-reviewer` (accessibility, MudBlazor patterns, UX)
  - Akka.NET actor changes → `dotnet-skills:akka-net-specialist`
  - DocFX / documentation changes → `dotnet-skills:docfx-specialist`
  - Performance-sensitive code paths → `dotnet-skills:dotnet-performance-analyst`
  - If nothing warrants a 6th agent, skip it — don't force a pick

### Step 3: Compile Results

After ALL agents complete, compile a unified review summary:

```
## Comprehensive Review Summary

### Agent Results
| Agent | Findings | Critical | Suggestions |
|-------|----------|----------|-------------|
| Refactor | X | Y | Z |
| Tests | X | Y | Z |
| Security | X | Y | Z |
| Concurrency | X | Y | Z |
| E2E (if run) | X | Y | Z |
| Agent 6 (if run) | X | Y | Z |

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
- **Each agent gets full context** — include changed file list and full diff summary in every prompt
- **Deduplicate findings** — if multiple agents flag the same issue, consolidate into one finding
- **Confidence filtering** — only report medium-to-high confidence findings
- **Respect CLAUDE.md** — project conventions override generic advice
- **No time estimates** — never include time predictions in the output
