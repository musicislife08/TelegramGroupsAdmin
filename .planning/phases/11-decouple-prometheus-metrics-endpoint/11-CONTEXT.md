# Phase 11: Decouple Prometheus Metrics Endpoint - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Decouple the Prometheus `/metrics` endpoint from the `SEQ_URL` env var. A new `ENABLE_METRICS` env var activates `/metrics` and registers OTEL meters independently of Seq logging/tracing. Existing `SEQ_URL` behavior preserved (no breaking change). No app-level auth — infrastructure controls access.

</domain>

<decisions>
## Implementation Decisions

### Activation model
- New `ENABLE_METRICS` env var — when set, `/metrics` is mapped and OTEL meters are registered
- `SEQ_URL` still implicitly enables `/metrics` (preserves current behavior, no breaking change)
- Either env var activates the endpoint — they are OR'd, not AND'd
- `ENABLE_METRICS` without `SEQ_URL` means: meters + Prometheus exporter only, no Serilog-to-Seq, no OTEL trace exporter

### OTEL pipeline separation
- When only `ENABLE_METRICS` is set: register OpenTelemetry meter providers and Prometheus exporter
- Logging to Seq and distributed tracing remain conditional on `SEQ_URL`
- The OTEL service registration needs to be split so meters can be registered independently of exporters

### Logging
- Startup INFO log should indicate which env var activated the metrics endpoint
- e.g., "Prometheus metrics endpoint mapped to /metrics (via ENABLE_METRICS)" or "(via SEQ_URL)"
- If both are set, either attribution is fine — Claude's discretion

### No app-level auth
- `/metrics` has no API key header, no auth middleware
- Hosting provider's infrastructure (K8s NetworkPolicy, reverse proxy rules) controls who can reach the endpoint
- This matches the unauthenticated `/healthz/live` and `/healthz/ready` pattern

### No bot connection state
- No custom Telegram bot connection gauge — bot state is the subscriber's concern, not the hosting provider's
- Matches Out of Scope in REQUIREMENTS.md

### Claude's Discretion
- How to split OTEL registration (meters vs tracing vs logging) in the service configuration
- Whether to extract metrics setup into its own extension method or keep inline in Program.cs
- Exact log message wording for the activation source

</decisions>

<specifics>
## Specific Ideas

- "We already have a /metrics endpoint which seems like the natural home for these things" — user recognized that Prometheus metrics are richer than a curated JSON blob
- "/metrics doesn't need auth — infra will control access to the endpoint"
- Phase pivoted from a custom JSON /healthz/status endpoint because Prometheus provides "meaningful data to the platform operator where the status endpoint would only provide minimal worth"

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `WebApplicationExtensions.MapApiEndpoints()`: Contains `/healthz/live` and `/healthz/ready` — `/metrics` mapping currently in Program.cs conditional block
- `app.MapPrometheusScrapingEndpoint()`: Existing Prometheus endpoint mapping call
- OTEL registration: Currently in Program.cs, conditional on `seqUrl` being non-null

### Established Patterns
- Env var gating: `SEQ_URL` conditionally enables observability stack — same pattern for `ENABLE_METRICS`
- Extension methods: `ConfigurePipeline()`, `MapApiEndpoints()` — endpoint registration follows this convention
- Conditional middleware: Prometheus endpoint is already conditionally mapped, just needs the condition widened

### Integration Points
- Program.cs lines ~77-95: OTEL/Seq configuration block — needs splitting to separate meters from tracing/logging
- Program.cs lines ~335-344: `/metrics` mapping and log — needs updated condition (SEQ_URL OR ENABLE_METRICS)
- `ServiceCollectionExtensions` or Program.cs: OTEL service registration — meters need independent registration path

</code_context>

<deferred>
## Deferred Ideas

- Auth-gated Prometheus federation endpoint for multi-tenant scraping — separate phase if needed
- UX-02: Blazor SignalR circuit count in metrics — future milestone

</deferred>

---

*Phase: 11-decouple-prometheus-metrics-endpoint*
*Context gathered: 2026-03-19*
