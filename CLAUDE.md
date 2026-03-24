# TelegramGroupsAdmin

## Stack

.NET 10.0, Blazor Server, MudBlazor 9, PostgreSQL 18, EF Core 10, Quartz.NET, OpenAI, VirusTotal, SendGrid, Seq, OpenTelemetry

## Repository

https://github.com/musicislife08/TelegramGroupsAdmin

## Git Workflow (CRITICAL)

- NEVER commit directly to `master` or `develop` — both are protected, require PRs
- NEVER create PRs from feature branches to `master` — always PR to `develop`
- ALWAYS use feature branches with conventional commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `ci:`, `chore:`)
- ALWAYS include closing keywords (`Closes #123`) at top of PR body
- ALWAYS prefer new commits over amending
- At session start, check `git branch` — if on master/develop, switch to a feature branch
- Use heredoc for multi-line commits: `git commit -F- <<'EOF'`
- Release workflow (develop → master), hotfix process, and Docker tag scheme are in context-keep memory

## Design Philosophy

Homelab single-instance deployment. Telegram Bot API enforces one connection per token — singleton by design. Do not recommend distributed systems patterns (Redis, RabbitMQ, S3, Kubernetes, microservices).

## Code Navigation

Use CSharperMcp tools (`find_symbol`, `find_references`, `get_diagnostics`) instead of grep/find. Always check `find_references` before renaming interfaces or base classes.

## Critical Rules

- NEVER run the app normally — validate with `dotnet run --migrate-only` (singleton constraint)
- EF Core: Modify models + AppDbContext FIRST → then `dotnet ef migrations add`
- Prefer Fluent API in AppDbContext over custom SQL for schema configuration
- Central Package Management: NuGet versions in `Directory.Packages.props`
- No time estimates in docs or issues
- MudBlazor v9 has breaking API changes — check context-keep memory before writing MudBlazor code
- GitHub labels are custom — check context-keep memory before labeling issues/PRs
