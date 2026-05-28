# Migrate Semantic Kernel → Microsoft.Extensions.AI, add OpenRouter & Anthropic providers

**Date:** 2026-05-28
**Status:** Design approved, pending spec review
**Delivery:** Single PR (`develop` target), three commits

## Summary

Replace the `Microsoft.SemanticKernel`-backed chat implementation with
`Microsoft.Extensions.AI` (MEAI), then exploit the cleaner abstraction to add two new
AI providers: **OpenRouter** (OpenAI-compatible aggregator) and **Anthropic** (Claude,
direct). The migration is contained behind the existing `IChatService` interface, so no
downstream consumer changes.

## Motivation

- Semantic Kernel is heavier than this codebase needs. The repo uses SK purely as a
  chat-completion client wrapper — no plugins, planners, embeddings, memory, or agents.
- MEAI's `IChatClient` is the exact primitive this code reaches for, and SK itself now
  sits on top of `IChatClient`. Moving down the stack removes a layer.
- MEAI is GA (v10.6.0, versioned with .NET 10), not experimental. The `SKEXP0010`
  experimental-API warning suppression in the AI csproj disappears.
- The smaller, vendor-neutral interface makes adding providers cheap — OpenRouter and
  Anthropic both reduce to one `BuildClient` branch each.

## Current state (as-is)

- **One file** touches SK types: `TelegramGroupsAdmin.AI/Services/SemanticKernelChatService.cs`.
- Everything else uses the project-owned `IChatService` abstraction. Consumers
  (`AIContentCheckV2`, `ImageContentCheckV2`, `VideoContentCheckV2`, `ExamEvaluationService`,
  `ProfileScoringEngine`, `AITranslationService`, `PromptBuilderService`,
  `ExamCriteriaBuilderService`, `FeatureTestService`) never see SK.
- `SemanticKernelChatService` caches built `Kernel` instances in a `static
  ConcurrentDictionary<string, CachedKernel>`, keyed by a composite of
  `connectionId | provider | model | azureDeployment | azureEndpoint | azureApiVersion |
  localEndpoint | apiKey`. Populated on-demand from DB config, invalidated via
  `InvalidateCache(connectionId?)`.
- AI services are all `Scoped` (matching `ISystemConfigRepository`); the kernel cache is
  `static` and persists across scoped instances.
- Provider config lives in the `configs` table as JSONB. `AIProviderType` is stored as an
  **int** at the data layer (`AIConnectionData.Provider` is `int`, mapped to the enum at the
  repository boundary). No `JsonStringEnumConverter` is registered on the config
  serializer, so even the in-domain serialization is integer-based. **Renaming an enum
  member is therefore a pure code change with no data migration.**

### Current `AIProviderType`

```csharp
public enum AIProviderType
{
    OpenAI,        // = 0
    AzureOpenAI,   // = 1
    LocalOpenAI    // = 2
}
```

## Package facts (verified against NuGet + Microsoft Learn, 2026-05-28)

| Package | Version to pin | Status | Role |
|---|---|---|---|
| `Microsoft.Extensions.AI` | `10.6.0` | GA | `IChatClient`, middleware/builder, `UseLogging`/`UseOpenTelemetry` |
| `Microsoft.Extensions.AI.OpenAI` | `10.6.0` | GA | `AsIChatClient()` bridge for OpenAI + OpenAI-compatible |
| `OpenAI` | `2.10.0` | GA | official SDK; provides `OpenAIClient`/`ChatClient` (transitive via above; pin explicitly) |
| `Azure.AI.OpenAI` | `2.1.0` | GA | `AzureOpenAIClient` for the Azure path |
| `Anthropic.SDK` | `5.10.0` | stable (community, maintainer **tghamm**) | implements `IChatClient` via `.Messages`; commit 3 only |

**Remove:** `Microsoft.SemanticKernel` (1.74.0), `Microsoft.SemanticKernel.Abstractions` (1.74.0).

### Critical API details (these differ from older docs / memory)

- **`AsChatClient()` is obsolete — use `AsIChatClient()`.** It is called on the OpenAI
  *chat client* (`client.GetChatClient(model)`), not on the top-level `OpenAIClient`.
- `IChatClient : IDisposable` — **must dispose evicted cache entries** (SK's `Kernel`
  was not disposable; this is new responsibility).
- `ChatOptions.Temperature` is `float?`. Rather than cast at the boundary, the **temperature
  type is changed to `float` across the whole chain** (options, feature config, data layer,
  UI) so the abstraction speaks the same type as its implementation. See the dedicated
  temperature-type subsection in commit 1. No data migration (JSON has one number type).
- `ChatOptions.MaxOutputTokens` is `int?` (maps cleanly from `MaxTokens`).
- `ChatOptions.ResponseFormat = ChatResponseFormat.Json` replaces the SK `"json_object"`
  string for JSON mode.
- Token usage: `response.Usage?.InputTokenCount` / `OutputTokenCount` / `TotalTokenCount`
  (all `long?` — cast to `int?` for the existing `ChatCompletionResult`).
- Finish reason: `response.FinishReason` is `ChatFinishReason?` (struct; `.ToString()`).
- Vision: build `new ChatMessage(ChatRole.User, [ new TextContent(text), new
  DataContent(bytes, mimeType), ... ])`. `DataContent(ReadOnlyMemory<byte>, string
  mediaType)` — `byte[]` converts implicitly.
- `Anthropic.SDK`: `new AnthropicClient(apiKey).Messages` **is** an `IChatClient`; model is
  selected via `ChatOptions.ModelId`. Do **not** confuse with the unrelated `Anthropic`
  package (tryAGI).
- Azure api-version: the Azure SDK uses an `AzureOpenAIClientOptions` service-version
  **enum**, not a free string. The stored `AzureApiVersion` string is mapped to the enum
  where recognized; otherwise the SDK default is used (logged at Debug). See Risks.

## Guiding principle

`IChatService`, `ChatCompletionResult`, `ChatCompletionOptions`, `ImageInput`, and every
consumer of those types are **unchanged** by commit 1. The migration is confined to the
implementation behind `IChatService` plus provider-config plumbing. New providers in
commits 2–3 are additive.

---

## Commit 1 — Swap SK → MEAI (pure refactor, zero behavior change)

### Package surface
- `Directory.Packages.props`: remove the two `Microsoft.SemanticKernel*` versions; add
  `Microsoft.Extensions.AI` 10.6.0, `Microsoft.Extensions.AI.OpenAI` 10.6.0, `OpenAI`
  2.10.0, `Azure.AI.OpenAI` 2.1.0.
- `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`: swap the `Microsoft.SemanticKernel`
  `PackageReference` for `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`,
  `OpenAI`, `Azure.AI.OpenAI`. Remove `<NoWarn>SKEXP0010</NoWarn>`.
- `TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj`: remove
  the two `Microsoft.SemanticKernel*` `PackageReference` lines — they are dead (no test
  `.cs` references SK types; verified).

### Rename + rewrite
- `SemanticKernelChatService.cs` → `ChatService.cs` (file + class). Update the AI-project
  `ServiceCollectionExtensions.AddAIServices()` registration:
  `AddScoped<IChatService, ChatService>()`.
- Cache type: `ConcurrentDictionary<string, CachedKernel>` →
  `ConcurrentDictionary<string, CachedClient>`, where
  `CachedClient(IChatClient Client, string ModelId)`. **Same composite cache key.** Same
  `GetOrAdd`, same `InvalidateCache(connectionId?)` semantics, same cache hit/miss metrics
  (`CacheMetrics.RecordHit/Miss("kernel")` — keep the `"kernel"` tag value, or rename to
  `"chat_client"`; see Naming below).
- **Disposal (new):** `InvalidateCache` and the clear-all path must `Dispose()` each
  removed `IChatClient`. Use `TryRemove` then dispose; clear-all enumerates and disposes
  before/after `Clear()`.
- `BuildKernel` → `BuildClient`, returning `IChatClient`:
  - **OpenAI:** `new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient()`.
  - **AzureOpenAI:** `new AzureOpenAIClient(new Uri(endpoint), new
    ApiKeyCredential(apiKey), options).GetChatClient(deploymentName).AsIChatClient()`,
    where `options` carries the mapped service version (see Risks).
  - **LocalOpenAI:** `new OpenAIClient(new ApiKeyCredential(apiKey ?? "not-required"), new
    OpenAIClientOptions { Endpoint = new Uri(localEndpoint) }).GetChatClient(model)
    .AsIChatClient()`.
  - Wire `OpenAIClientOptions.Transport` (and the Azure equivalent) through the existing
    `IHttpClientFactory` so all clients share the pooled handler. (`Microsoft.Extensions.Http`
    is already referenced.)
- Call internals in each `Get*`/`Test*` method:
  - `ChatHistory` + `AddSystemMessage`/`AddUserMessage` → `List<ChatMessage>` with
    `ChatRole.System` / `ChatRole.User`.
  - Vision: SK `ImageContent` → MEAI `DataContent(bytes, mimeType)`; text → `TextContent`.
  - `OpenAIPromptExecutionSettings` → `ChatOptions { MaxOutputTokens, Temperature,
    ResponseFormat }` (Temperature now `float?` end-to-end — no cast; see below).
  - `GetChatMessageContentAsync(...)` → `IChatClient.GetResponseAsync(messages, options,
    ct)`.
  - `CreateResult`: read `response.Text`/message content, `response.Usage?.*` (cast `long?`
    → `int?`), `response.FinishReason?.ToString()`, `response.ModelId ?? fallback`. The
    `ChatCompletionResult` shape is unchanged.
- Keep the existing `Stopwatch` + `try/catch` + `ApiMetrics.RecordOpenAiCall(...)`
  instrumentation **exactly as-is** (see Telemetry decision below).

### Temperature type change (`double` → `float`, end-to-end)
MEAI's `ChatOptions.Temperature` is `float?` where SK used `double`. Rather than cast at
the boundary, change the type across the chain so the abstraction matches its implementation:
- `TelegramGroupsAdmin.AI/Services/ChatCompletionOptions.cs:16`: `double?` → `float?`.
- `TelegramGroupsAdmin.Configuration/Models/AIFeatureConfig.cs:26`: `double = 0.2` →
  `float = 0.2f`.
- `TelegramGroupsAdmin.Data/Models/Configs/AIFeatureConfigData.cs:26`: `double = 0.2` →
  `float = 0.2f` (persisted JSONB; **no migration** — JSON has one number type, `0.2`
  round-trips identically).
- The `AIFeatureConfig` ↔ `AIFeatureConfigData` mapping assigns `Temperature` directly —
  both `float`, no cast.
- `TelegramGroupsAdmin/Components/Shared/ContentDetection/AIFeatureCard.razor:291`:
  `OnTemperatureChanged(double value)` → `(float value)`; the bound `MudNumericField`
  becomes `T="float"` (its `Step`/`Min`/`Max` attribute values become `float`).
- Behavior-preserving: `0.2f` and `0.2d` both serialize to the wire string `"0.2"` sent to
  the provider.

### Knock-on references
- `TelegramGroupsAdmin/Services/MemoryMetrics.cs:95-99`: the gauge bound to
  `SemanticKernelChatService.CachedKernelCount` → `ChatService.CachedClientCount`. Rename
  the static property accordingly. Rename the gauge `tga.cache.kernel.count` →
  `tga.cache.chat_client.count` and update its description (no backward-compat alias, per
  project rules).
- `TelegramGroupsAdmin.Core/QueryConstants.cs:21,27`: doc comments naming
  `SemanticKernelChatService` → `ChatService`.
- `TelegramGroupsAdmin.AI/CLAUDE.md`: update Project Role + Design Rules — replace
  "Semantic Kernel" / "kernel cache" language with MEAI / `IChatClient` cache; replace the
  "owns the `Microsoft.SemanticKernel` package" rule with "owns the
  `Microsoft.Extensions.AI*` package references."

### Telemetry decision (investigated, no conditions)
The PR does **not** adopt MEAI's `UseOpenTelemetry()`/`UseLogging()` middleware. Reason:
`ApiMetrics.RecordOpenAiCall` emits per-**feature**-attributed metrics
(`tga.api.openai.calls_total/.latency/.tokens_total`, tagged `feature` + `model` +
`status`). MEAI's OpenTelemetry middleware emits the standard GenAI semantic conventions,
which have no notion of `AIFeatureType`, so adopting it would **lose feature attribution**
that the Core metrics rules mandate and the Grafana dashboards rely on. The manual
instrumentation is retained verbatim.

### Naming note (cache metric tag)
`CacheMetrics.RecordHit/Miss` is currently called with the literal `"kernel"`. Rename to
`"chat_client"` for accuracy (bounded-cardinality tag, dashboard-facing). This is a code +
dashboard label change consistent with the no-backward-compat rule. If the existing
`"kernel"` tag value must persist for dashboard continuity, that is the only reason to keep
it — default is to rename.

### Verification (hybrid contract)
- The existing AI tests **must pass with no change to assertion intent**: `AIContentCheckTests`,
  `ExamEvaluationServiceTests`, `ProfileScoringEngineTests` (UnitTests),
  `FeatureTestServiceTests` (ComponentTests).
- **One permitted class of mechanical test edit:** the `double` → `float` temperature
  change forces numeric-literal suffix updates in config/integration tests (`0.2` →
  `0.2f`, `Is.EqualTo(0.2)` → `Is.EqualTo(0.2f)`) at `AIProviderConfigTests`,
  `AIProviderConfigIntegrationTests`, and `AIFeatureCardTests`. These are mechanical;
  assertion *intent* is unchanged. (`ExamEvaluationServiceTests:607` asserts `Is.Null` —
  type-agnostic, no edit.)
- **Dedicated audit step:** dispatch an agent to scan the test projects for any SK type
  that leaked into test setup/mocks (`ChatHistory`, `Kernel`, `OpenAIPromptExecutionSettings`,
  `IChatCompletionService`, `Microsoft.SemanticKernel.*`) and flag any mechanical
  adjustment needed beyond the temperature suffixes. (Pre-check shows zero SK-type
  references in tests, so the expected outcome is "no SK-related changes needed" — the
  audit confirms it.)
- `dotnet build` with `TreatWarningsAsErrors=true` must be clean (no leftover obsolete-API
  warnings, e.g. accidental `AsChatClient`).

---

## Commit 2 — Rename `LocalOpenAI` → `OpenAICompatible`, add OpenRouter

### Enum
```csharp
public enum AIProviderType
{
    OpenAI           = 0,
    AzureOpenAI      = 1,
    OpenAICompatible = 2,   // renamed from LocalOpenAI; value preserved → no data migration
    OpenRouter       = 3    // new
}
```
- Explicit numeric values added to lock the contract. **Never renumber or reorder.**
- The compiler finds all references: `ChatService.BuildClient`, the
  `Provider != AIProviderType.LocalOpenAI` API-key guards (3 sites), the Azure model-id
  branches, `AIServiceFactory`, and the UI.

### Data-layer doc
- `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs:5,15`: update the
  `0=OpenAI, 1=AzureOpenAI, 2=...` comment to reflect the rename and new values.

### Client construction
- `ChatService.BuildClient`: the `OpenAICompatible` branch is the former `LocalOpenAI`
  branch unchanged. Add an `OpenRouter` branch — identical OpenAI-compatible construction,
  with the endpoint defaulting to `https://openrouter.ai/api/v1` when the connection does
  not specify one. OpenRouter requires an API key (no keyless path).

### Model discovery
- `AIServiceFactory.FetchModelsAsync`: `OpenAICompatible` keeps the existing
  endpoint-driven OpenAI-compatible fetch (including the Ollama `/api/tags` heuristic).
  `OpenRouter` uses the same OpenAI-compatible `/v1/models` path against
  `https://openrouter.ai/api/v1`. (Richer OpenRouter model metadata — pricing, context
  length, modality — is explicitly out of scope; deferred.)

### UI
- `AddAIConnectionDialog.razor`: rename the `LocalOpenAI` select item to "OpenAI-Compatible
  (Ollama, LM Studio, vLLM, …)"; add an "OpenRouter" item. Update `GetProviderHelperText`
  and `GetIdPlaceholder` for both. OpenRouter selection pre-fills the endpoint default.
- `AIConnectionCard.razor` / `AIProviderSettings.razor`: handle the renamed enum value and
  the new OpenRouter value (endpoint field shown, api-key required).

---

## Commit 3 — Anthropic (Claude), direct

### Enum
```csharp
Anthropic = 4   // appended
```

### Package
- Add `Anthropic.SDK` 5.10.0 to `Directory.Packages.props` and the AI csproj.

### Client construction
- `ChatService.BuildClient`: `Anthropic` branch →
  `new AnthropicClient(apiKey).Messages` (an `IChatClient`). **Model-binding asymmetry:**
  the OpenAI/Azure/compat clients bind the model at `GetChatClient(model)`, but the
  Anthropic `IChatClient` is **not** model-bound at construction — the Claude model id must
  be supplied per request via `ChatOptions.ModelId`. Resolve this by storing `ModelId` on
  `CachedClient` (already present) and setting `ChatOptions.ModelId = cached.ModelId` for
  the Anthropic path in the options mapping. (For the OpenAI family `ModelId` stays unset;
  the model is already bound to the client.) API key required.
- Vision flows through the same `DataContent` path. Prompt caching is **out of scope**
  (future enhancement — `Anthropic.SDK` supports it, but it needs explicit cache markers
  and threshold awareness).

### Model discovery
- `AIServiceFactory.FetchModelsAsync`: `Anthropic` branch queries Anthropic's models
  listing endpoint (`GET https://api.anthropic.com/v1/models`) using the Anthropic auth
  headers (`x-api-key: <key>`, `anthropic-version: <date>`) rather than OpenAI's Bearer
  scheme, and parses Anthropic's `{ data: [{ id, display_name, created_at }] }` shape into
  `AIModelInfo`. This mirrors "query the API for the model list" as done for OpenAI, with
  provider-specific auth + parsing.

### UI
- Add an "Anthropic (Claude)" provider item to `AddAIConnectionDialog.razor` with helper
  text + placeholder; `AIConnectionCard.razor` shows api-key field and the
  refresh-models button (now backed by the Anthropic listing).

---

## Out of scope (YAGNI)

- Gemini / AWS Bedrock / Groq / DeepSeek as first-class providers — reachable today via the
  `OpenAICompatible` endpoint or OpenRouter.
- Anthropic prompt caching.
- Cost-per-feature dashboards / OpenRouter pricing surfacing.
- Structured-output JSON-schema upgrades (`ChatResponseFormat.ForJsonSchema`) — current
  code only needs plain JSON mode.
- Per-connection vision-capability flag — the existing `FeatureTestService` vision probe
  already validates this at test time.
- MEAI OpenTelemetry/logging middleware (see Telemetry decision).

## Risks & nuances

1. **`IChatClient` disposal leak.** The single genuinely-new behavior. If eviction does not
   dispose, the underlying `OpenAIClient`/HTTP pipeline leaks. Covered by the disposal
   change in commit 1; worth an explicit test that `InvalidateCache` disposes.
2. **Azure api-version string → enum.** SK accepted a free string (`"2024-10-21"`); the
   Azure SDK uses an `AzureOpenAIClientOptions` service-version enum. Map known strings to
   the enum; fall back to SDK default + Debug log when unrecognized. Low practical risk —
   no Azure connection is configured in the primary deployment — but the public codebase
   supports Azure, so the mapping must exist.
3. **`double` → `float` temperature type change.** Done end-to-end (no cast); see the
   commit-1 temperature subsection. No data migration; behavior-preserving on the wire.
   Forces ~8 mechanical test-literal suffix edits (covered by the hybrid contract).
4. **Token counts `long?` → `int?`.** Safe for realistic token volumes; clamp/cast in
   `CreateResult`.
5. **Anthropic package disambiguation.** Must reference `Anthropic.SDK` (tghamm), not the
   similarly named `Anthropic` (tryAGI) package.
6. **`TreatWarningsAsErrors`.** The AI project treats warnings as errors; the obsolete
   `AsChatClient` would fail the build — `AsIChatClient` is mandatory.

## Verification plan

- Per-commit: `dotnet build` clean (warnings-as-errors) and the AI test suite green at each
  commit (each commit independently builds + passes).
- Commit 1: existing AI tests pass **unmodified**; test-leak audit agent reports no required
  test changes.
- Commits 2–3: manual smoke via the Settings UI "Test" flow against a real OpenRouter key
  and a real Anthropic key (model refresh + a completion + a vision call where supported).
- Migrations: none required (enum stored as int; rename preserves value 2).

## Commit sequence (single PR → `develop`)

1. `refactor(ai): replace Semantic Kernel with Microsoft.Extensions.AI`
2. `feat(ai): rename LocalOpenAI provider to OpenAICompatible and add OpenRouter`
3. `feat(ai): add Anthropic (Claude) provider`
