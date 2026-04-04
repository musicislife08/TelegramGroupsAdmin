# TelegramGroupsAdmin.Core - AI Reference

## Metrics Pattern

All custom metrics use **domain-scoped singleton classes** with wrapper methods. Do not add counters/histograms directly to `TelemetryConstants` — that file holds only `ActivitySource` fields for distributed tracing.

### Adding a New Metric

1. **Find the right metrics class** for your domain:

| Class | Project | Domain |
|---|---|---|
| `ApiMetrics` | Core | External API calls (OpenAI, VirusTotal, SendGrid, Telegram) |
| `CacheMetrics` | Core | Cache hit/miss/removal tracking |
| `DetectionMetrics` | ContentDetection | Spam detection, file scanning, veto |
| `PipelineMetrics` | Telegram | Message processing, moderation, profile scans |
| `ChatMetrics` | Telegram | Chat health, managed chats, joins/leaves |
| `WelcomeMetrics` | Telegram | Welcome flow outcomes, security checks |
| `ReportMetrics` | Telegram | Report creation, resolution, pending count |
| `JobMetrics` | BackgroundJobs | Quartz job execution, duration, rows affected |
| `MemoryMetrics` | Main app | ObservableGauges for stateful singleton memory attribution |

2. **Add the instrument** to the appropriate class:

```csharp
// In the metrics class constructor or field initializer
private readonly Counter<long> _myCounter = _meter.CreateCounter<long>(
    "tga.domain.my_counter_total",
    description: "What this counts");
```

3. **Add a recording method** that enforces required tags:

```csharp
public void RecordMyEvent(string requiredTag, bool success)
{
    _myCounter.Add(1, new TagList
    {
        { "required_tag", requiredTag },
        { "status", success ? "success" : "failure" }
    });
}
```

4. **Call the recording method** from the service, injecting the metrics class via DI:

```csharp
public class MyService(ApiMetrics apiMetrics)
{
    public async Task DoWorkAsync()
    {
        // ... do work ...
        apiMetrics.RecordMyEvent("feature_name", success: true);
    }
}
```

### Naming Rules

- **Prefix**: All metrics start with `tga.`
- **Pattern**: `tga.<domain>.<subject>.<metric_type>`
- **Counters**: End with `_total` (e.g., `tga.api.openai.calls_total`)
- **Histograms**: Use `unit: "ms"` parameter, do NOT include `_ms` in the name (the Prometheus exporter appends the unit as `_milliseconds`)
- **Gauges**: Use descriptive names (e.g., `tga.cache.chat.count`)
- **Tags**: Use snake_case, bounded cardinality only (no user IDs, chat IDs, message IDs)

### Histogram Unit Suffix

The OTel Prometheus exporter auto-appends the unit as a suffix. A histogram named `tga.jobs.duration` with `unit: "ms"` exports as `tga_jobs_duration_milliseconds_bucket`. If you include `_ms` in the name, it exports as `tga_jobs_duration_ms_milliseconds_bucket` (redundant).

### ObservableGauge Constraints

`ObservableGauge` callbacks are `Func<T>` (synchronous). They **cannot** call async methods or query the database. Only read from in-memory singleton state:

```csharp
// OK - reads in-memory count
meter.CreateObservableGauge("tga.cache.chat.count", () => chatCache.Count);

// BAD - async DB call in synchronous callback
meter.CreateObservableGauge("tga.reports.pending", () => repo.GetCountAsync().Result); // DEADLOCK RISK
```

For metrics that need DB-sourced values, maintain a cached counter via `Interlocked.Increment`/`Decrement` in the recording methods.

### Creating a New Metrics Class

If no existing class fits your domain, create a new one:

1. Create `YourDomainMetrics.cs` in the appropriate project
2. Make it a `sealed class` with a `Meter` field: `private readonly Meter _meter = new("TelegramGroupsAdmin.YourDomain");`
3. Register as singleton in that project's `ServiceCollectionExtensions`
4. The existing `AddMeter("TelegramGroupsAdmin.*")` wildcard in `Program.cs` auto-discovers new meters — no OTel config changes needed

### Design Spec

Full instrument catalog, tag schemas, and migration details: `docs/superpowers/specs/2026-03-26-metrics-expansion-design.md`
