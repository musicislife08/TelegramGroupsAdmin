# Codebase Concerns

**Analysis Date:** 2026-03-15

## Project Philosophy

This is a **single-instance anti-spam Telegram bot** with moderation features, deployed on a homelab. It knows what it is and isn't pretending to be something else.

**What this means for evaluating concerns:**

- **Single instance is the architecture, not a limitation.** Telegram Bot API enforces one connection per token. In-memory caching, local file storage, and embedded background jobs are correct choices — not tech debt waiting for Redis.
- **Homelab scale (500-20,000 messages/day).** Performance concerns should be evaluated against this reality, not hypothetical enterprise load. If it works at this scale, it's not a bottleneck.
- **Operational simplicity over enterprise patterns.** No circuit breakers, distributed caches, message queues, or microservices. Adding these would make the project harder to maintain for zero benefit.
- **Fail loud, fail open.** Services are designed to fail open — if a check or external service fails, the message/user is allowed through rather than blocked. This is critical for two reasons: (1) multiple detection checks feed into the decision — if one errors out, it shouldn't skew the result toward a ban, and (2) **Telegram bans are destructive and partially irreversible** — a banned user must be unbanned AND re-invited, and their deleted messages cannot be restored by admins. A false positive ban caused by a flaky upstream service is far worse than a missed spam message. Failures are logged loudly so admins can investigate manually.
- **E2E tests cover critical flows.** TODO comments in component tests are design decisions (considered and deferred), not coverage gaps. Over-testing E2E is an anti-pattern.
- **External API usage is already managed.** VirusTotal has an internal rate limiter. Other services (OpenAI, SendGrid) enforce quotas server-side. Additional application-level rate limiting adds complexity without meaningful protection at this scale.

**Working principles for AI agents on this codebase:**
- **When unsure, stop and ask.** Do not guess at intent, architecture decisions, or unfamiliar patterns. Pause, explain what's unclear, and ask for guidance. Wrong assumptions waste more time than a question.
- **Fix bugs as you find them.** If you discover a bug while working on something else, fix it — even if it expands the scope. The exception is if it's large enough or foundational enough to warrant its own tracked issue. Small stuff gets fixed in place.
- **This is a collaboration, not a one-way push.** Re-evaluate as you go. If the plan isn't working, say so. If you see a better approach mid-execution, raise it. The goal is good software, not blind adherence to a plan.

**When reviewing this codebase, do NOT recommend:**
- Distributed systems patterns (Redis, RabbitMQ, S3, Kubernetes)
- Custom exception hierarchies for internal services that already log and handle errors
- Circuit breakers or retry frameworks beyond what's already implemented
- Horizontal scaling strategies
- Additional E2E test coverage beyond critical user flows

## Tracked Issues

The following concerns are already tracked in GitHub Issues and should not be re-discovered or re-reported:

- **#327**: `UserAutoTrustService` audit gap (trust granted without audit log entry)
- **StopWords performance query**: `GetAverageStopWordsExecutionTimeAsync()` returns null — JSONB query not yet implemented (`StopWordRecommendationService.cs:512-515`)
- **Rich formatting in notifications**: `NotificationHandler.cs` uses plain text only — enhancement tracked for future work

All other TODOs in the codebase are tracked as GitHub Issues. Check there before flagging anything as a new concern.

## Actual Concerns

### Database Query Result Sizes Not Capped at Data Layer
- Some repository queries (audit log, message history) lack result size limits in the data layer
- Pagination is enforced at the UI layer, but the data layer itself doesn't enforce caps
- Low risk for homelab, but a defensive `LIMIT` at the repo level would be a cheap safeguard

### Test Coverage Gaps
- OpenAI API timeout → retry behavior not comprehensively tested (`AIContentCheckV2.cs`)
- `JobPayloadHelper.TryGetPayloadAsync<T>()` stale trigger cleanup logic not tested (self-healing design, but untested)
- `NotificationHandler.EscapeHtml()` edge cases with Unicode/emoji not covered

### Config Reflection-Based Empty Check
- `ConfigService.IsRecordEmpty()` uses reflection with cached `PropertyInfo[]` — works correctly but new properties on `ConfigRecordDto` are picked up automatically at class load (no staleness risk in practice, but no test verifying completeness)

## Genuine Architecture Notes

### ffmpeg External Dependency
- Video content detection requires `ffmpeg` in the container
- Docker image includes it; if missing, video checks fail gracefully (logged, not fatal)
- Worth knowing for deployment troubleshooting, not a code fix

### Raw SQL in CachedBlockedDomainsRepository
- Uses PostgreSQL `UNNEST()` for bulk upserts — this is a **deliberate performance optimization**, not tech debt
- Update frequency is low (periodic blocklist sync); raw SQL is the right call here

### Config Merge via JSON Serialization
- `ConfigService.MergeConfigs<T>()` double-serializes through `JsonElement` — works correctly for all current configs
- Only worth revisiting if config structures become deeply nested (unlikely given current patterns)

---

*Concerns audit: 2026-03-15*
