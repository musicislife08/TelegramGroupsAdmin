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
| `Anthropic` | `12.24.1` | **official Anthropic SDK, beta** (versioned 10+) | implements `IChatClient` via `AsIChatClient(model)`; commit 3 only |

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
  (all `long?`). No cast — `ChatCompletionResult`'s token fields change to `long?` to match,
  and `ApiMetrics.RecordOpenAiCall` takes `long` (see below).
- Finish reason: `response.FinishReason` is `ChatFinishReason?` (struct; `.ToString()`).
- Vision: build `new ChatMessage(ChatRole.User, [ new TextContent(text), new
  DataContent(bytes, mimeType), ... ])`. `DataContent(ReadOnlyMemory<byte>, string
  mediaType)` — `byte[]` converts implicitly.
- **Official `Anthropic` SDK (v10+):** `new AnthropicClient { ApiKey = key }.AsIChatClient(modelId)`
  returns an `IChatClient` **bound to the model** at construction — uniform with the OpenAI
  family (no per-request `ChatOptions.ModelId` needed). Namespaces `Anthropic` +
  `Anthropic.Models.Messages`. **Beta:** breaking changes possible in minor/patch releases;
  pin the exact version.
- **Package-name disambiguation (three distinct packages):** use **`Anthropic` v10+**
  (official Anthropic; current `12.24.1`). Do **not** use `Anthropic.SDK` (tghamm community)
  or `tryAGI.Anthropic` (the former `Anthropic` ≤3.x, now relocated).
- Azure api-version: **no longer pinned.** The 2.x `AzureOpenAIClient` defaults to the
  latest service version the SDK knows about, and that default advances with each SDK
  upgrade — i.e. it floats. The `AzureApiVersion` field (config, data, UI, cache key) is
  **removed** rather than mapped to the SDK's version enum. See commit 1 and Risks.

## Guiding principle

`IChatService`, `ChatCompletionResult`, `ChatCompletionOptions`, `ImageInput`, and every
consumer of those types are **unchanged** by commit 1. The migration is confined to the
implementation behind `IChatService` plus provider-config plumbing. New providers in
commits 2–3 are additive.

---

## Commit 1 — Swap SK → MEAI (refactor; no logic changes)

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
  `CachedClient(IChatClient Client, string ModelId)`. Composite cache key as today, **minus
  the now-removed `AzureApiVersion` component**. Same `GetOrAdd`, same
  `InvalidateCache(connectionId?)` semantics. Cache hit/miss metrics are renamed from the
  `"kernel"` tag value to `"chat_client"` (see Naming below).
- **Disposal (new):** `InvalidateCache` and the clear-all path must `Dispose()` each
  removed `IChatClient`. Use `TryRemove` then dispose; clear-all enumerates and disposes
  before/after `Clear()`.
- `BuildKernel` → `BuildClient`, returning `IChatClient`:
  - **OpenAI:** `new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient()`.
  - **AzureOpenAI:** `new AzureOpenAIClient(new Uri(endpoint), new
    ApiKeyCredential(apiKey)).GetChatClient(deploymentName).AsIChatClient()` — no
    service-version argument; the SDK default applies and floats with SDK upgrades.
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
  - `CreateResult`: read `response.Text`/message content, `response.Usage?.*` (`long?`,
    assigned straight into the now-`long?` token fields — no cast),
    `response.FinishReason?.ToString()`, `response.ModelId ?? fallback`.
- Keep the existing `Stopwatch` + `try/catch` + `ApiMetrics.RecordOpenAiCall(...)`
  instrumentation **exactly as-is** (see Telemetry decision below).

### Temperature: type change (`double` → `float`) + new default (`0.2` → `1.0`)
Two small changes to the temperature field, made together in the same files. Neither is a
logic change — the type change is a declaration swap, and the default is a single constant.

**Type:** MEAI's `ChatOptions.Temperature` is `float?` where SK used `double`. Rather than
cast at the boundary, change the type across the chain so the abstraction matches its
implementation.

**Default:** raise the default temperature from `0.2` to `1.0`. Rationale: `1.0` is the only
value safe across all model classes. Reasoning models (GPT-5+) reject any `temperature ≠ 1`
with a hard error, and Anthropic requires `temperature = 1` whenever Claude extended thinking
is enabled. `0.2` therefore becomes an active failure mode as newer OpenAI models and the new
Anthropic provider come into use. `1.0` also matches every provider's own default. Temperature
remains per-feature configurable, so deterministic classification on standard models is still
available by explicitly setting a lower value.

Files:
- `TelegramGroupsAdmin.AI/Services/ChatCompletionOptions.cs:16`: `double?` → `float?`.
- `TelegramGroupsAdmin.Configuration/Models/AIFeatureConfig.cs:26`: `double = 0.2` →
  `float = 1.0f`.
- `TelegramGroupsAdmin.Data/Models/Configs/AIFeatureConfigData.cs:26`: `double = 0.2` →
  `float = 1.0f` (persisted JSONB; **no migration** — JSON has one number type, and the new
  default only affects feature configs that have never had a temperature explicitly set;
  existing stored values are preserved on read).
- The `AIFeatureConfig` ↔ `AIFeatureConfigData` mapping assigns `Temperature` directly —
  both `float`, no cast.
- `TelegramGroupsAdmin/Components/Shared/ContentDetection/AIFeatureCard.razor:120,291`:
  the bound `MudNumericField` becomes `T="float"` (its `Min`/`Max`/`Step` attribute values
  become `float`); `OnTemperatureChanged(double value)` → `(float value)`. The UI keeps
  `Max="2.0"` for now — making the range provider-aware is deferred to a follow-on issue
  (see Out of scope).
- Type change is invisible on the wire (`0.2f`/`0.2d` both serialize to `"0.2"`); the
  default change just means a never-configured feature now starts at `1.0` instead of `0.2`.

### Token counts: `int` → `long` (match the interface, no cast)
MEAI's `UsageDetails` exposes token counts as `long?`. Adopt that type through our own
surface rather than casting back to `int?`:
- `TelegramGroupsAdmin.AI/Services/ChatCompletionResult.cs:17,22,27`: `TotalTokens`,
  `PromptTokens`, `CompletionTokens` → `long?`.
- `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs:65`: `RecordOpenAiCall` `int promptTokens,
  int completionTokens` → `long`. The instrument is already `Counter<long>`, so the inner
  `.Add(...)` calls are unchanged (this just removes today's implicit `int`→`long` widening).
  The AI service is the only caller.
- Everything else compiles unchanged because `int`→`long` is an *implicit widening*: the
  `FeatureTestService` token strings, the `AITranslationService` log, and the test literals
  (`TotalTokens = 5`, etc. in `FeatureTestServiceTests` / `AIContentCheckTests`) all keep
  working with no edits.

### Remove `AzureApiVersion` (api-version no longer pinned)
The 2.x SDK floats the service version with each upgrade, so the stored pin is dead. Remove
the field everywhere:
- `TelegramGroupsAdmin.Configuration/Models/AIConnection.cs:32`: delete the property.
- `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs:32`: delete the property
  (old stored JSONB keeps an `azureApiVersion` key; System.Text.Json ignores it on read —
  no migration).
- The `AIConnection` ↔ `AIConnectionData` mapping: drop the `AzureApiVersion` assignment.
- `ChatService.GenerateCacheKey` (was `SemanticKernelChatService.cs:588`): remove the
  `AzureApiVersion` key component.
- Azure client construction: no `apiVersion` argument (was `SemanticKernelChatService.cs:623`).
- `TelegramGroupsAdmin/Components/Shared/AddAIConnectionDialog.razor:126`: drop the
  `AzureApiVersion = "2024-10-21"` default-set.
- `TelegramGroupsAdmin/Components/Shared/Settings/AIConnectionCard.razor:39,119,131,152`:
  remove the api-version `MudTextField`, the `_azureApiVersion` field, and its load/save.
- Tests: delete `AIProviderConfigTests:150` (`AIConnection_DefaultAzureApiVersion_Is2024_10_21`)
  and the `AzureApiVersion` assertion at `:352`; remove the `azureApiVersion` param + cases
  from `AIConnectionCardTests` (`:23,33,116`). (The `?api_version=` strings in
  `AIProviderConfigIntegrationTests:615,626` are `LocalEndpoint` query params — unrelated,
  leave them.)

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

### Naming note (cache metric tag) — decided
`CacheMetrics.RecordHit/Miss` is currently called with the literal `"kernel"`. **Renamed to
`"chat_client"`** for accuracy (bounded-cardinality tag, dashboard-facing), alongside the
`tga.cache.kernel.count` → `tga.cache.chat_client.count` gauge rename. No backward-compat
alias (per project rules); any Grafana panel querying the old tag value or gauge name is
updated to match.

### Verification (hybrid contract)
No logic changes in commit 1, so green tests prove parity. The only test edits are mechanical
reflections of the type swap and the default constant:
- Behavioral AI tests **must pass with no change to assertion intent**: `AIContentCheckTests`,
  `ExamEvaluationServiceTests`, `ProfileScoringEngineTests` (UnitTests),
  `FeatureTestServiceTests` (ComponentTests).
- **Mechanical test edits:** the `double` → `float` change forces numeric-literal suffix
  updates (`0.2` → `0.2f`, `Is.EqualTo(0.2)` → `Is.EqualTo(0.2f)`); the default-constant
  test `AIProviderConfigTests:71` (`AIFeatureConfig_DefaultTemperature_Is0Point2` →
  `_Is1Point0`, asserting `1.0f`) updates to the new constant. These touch
  `AIProviderConfigTests`, `AIProviderConfigIntegrationTests`, and `AIFeatureCardTests`.
  Assertion *intent* unchanged. (`ExamEvaluationServiceTests:607` asserts `Is.Null` —
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
- Add the **official `Anthropic`** SDK (`12.24.1`) to `Directory.Packages.props` and the AI
  csproj. Pin the exact version (beta — see Risks).

### Client construction
- `ChatService.BuildClient`: `Anthropic` branch →
  `new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model)` — an `IChatClient` **bound
  to the model** at construction, uniform with the OpenAI family. No per-request
  `ChatOptions.ModelId` handling and no model-binding special-case (the official SDK removes
  the asymmetry the community package would have introduced). API key required.
- Vision flows through the same `DataContent` path. Prompt caching is **out of scope**
  (future enhancement — Claude supports it, but it needs explicit cache markers + threshold
  awareness).

### Model discovery
- `AIServiceFactory.FetchModelsAsync`: `Anthropic` branch calls
  `GET https://api.anthropic.com/v1/models` directly with Anthropic auth headers
  (`x-api-key: <key>`, `anthropic-version: <date>`) and parses the
  `{ data: [{ id, display_name, created_at }] }` shape into `AIModelInfo`. The raw REST call
  is **preferred over the SDK's native models-listing service** specifically to keep model
  discovery off the SDK's beta surface (see Risk 5) — `FetchModelsAsync` already uses
  `HttpClient` directly for every other provider, so this is consistent. Mirrors "query the
  API for the model list" as done for OpenAI, with provider-specific auth.

### UI
- Add an "Anthropic (Claude)" provider item to `AddAIConnectionDialog.razor` with helper
  text + placeholder; `AIConnectionCard.razor` shows api-key field and the
  refresh-models button (now backed by the Anthropic listing).

---

## Out of scope (YAGNI)

- Gemini / AWS Bedrock / Groq / DeepSeek as first-class providers — reachable today via the
  `OpenAICompatible` endpoint or OpenRouter.
- **Anthropic prompt caching** (tracked: issue #481). Not automatic (unlike OpenAI); requires
  explicit `cache_control` markers per request, on the SDK's **native** message types — which
  MEAI's `IChatClient` doesn't expose, so it'd mean dropping to the native path or a MEAI
  escape hatch (Anthropic-specific; OpenAI/OpenRouter don't use it). Structure *is* favorable
  (verified against `AIPromptBuilder.cs`): stable system prompt (`baseTechnical` + the long,
  rarely-changed admin `customRulesPrompt` + veto guidance) as the cacheable prefix, dynamic
  `<message_history>` + target message as the uncached tail. **But model-floor-gated, and the
  likely usage doesn't clear it:** a configured chat's system prompt (~1,100–1,300 tokens for
  a TSP-sized rules block) clears the 1,024-token floor (Sonnet 4.6/4.5, etc.) but *not* the
  4,096-token floor — and Anthropic spam-checking would most likely run on an Opus-level model
  (4,096 floor), so caching wouldn't trigger at current prompt sizes. It'd need a much larger
  stable rules/exemplar corpus or a smaller-floor model. Out of this PR regardless. (Distinct
  from MEAI's `UseDistributedCache()`, a client-side whole-response cache, not prefix caching.)
- Cost-per-feature dashboards / OpenRouter pricing surfacing.
- **Structured-output JSON-schema enforcement** (`ChatResponseFormat.ForJsonSchema` /
  `GetResponseAsync<T>()`). Plain JSON mode (`JsonMode` → `ChatResponseFormat.Json`) is a
  *formatting* guarantee, not a *shape* guarantee; schema enforcement would decode-constrain
  the model to an exact shape (enums, required fields, numeric types) and let us drop
  defensive parsing like the markdown-fence strip in `AITranslationService.cs:88`. Explicitly
  **not pursued**: plain JSON mode has run ~7 months in production with zero observed parse
  failures, so this solves a problem that hasn't appeared. It's also gated by per-provider
  support (OpenAI/Azure native strict schema; Anthropic only via forced tool use;
  OpenRouter/local model-dependent), so a real implementation would be per-provider with
  graceful fallback. **Revisit trigger:** if adding weaker models (via OpenRouter/local) ever
  produces parse failures on the structured calls. No tracking issue filed — no observed
  problem.
- Per-connection vision-capability flag — the existing `FeatureTestService` vision probe
  already validates this at test time.
- MEAI OpenTelemetry/logging middleware (see Telemetry decision).
- **Provider-aware temperature min/max in the UI** — Anthropic accepts `0.0–1.0`, OpenAI
  `0.0–2.0`; the UI currently hardcodes `Max="2.0"`, so a user could set a value Claude
  rejects. Tracked as follow-on issue
  [#480](https://github.com/musicislife08/TelegramGroupsAdmin/issues/480), not fixed in this PR.

## Risks & nuances

1. **`IChatClient` disposal leak.** The single genuinely-new behavior. If eviction does not
   dispose, the underlying `OpenAIClient`/HTTP pipeline leaks. Covered by the disposal
   change in commit 1; worth an explicit test that `InvalidateCache` disposes.
2. **Azure api-version no longer pinned.** The stored `AzureApiVersion` pin is removed; the
   2.x SDK's default service version applies and floats with SDK upgrades (matches the
   "keep up with the SDK" cadence). Behavior change only for a deployment that deliberately
   pinned an older Azure API surface — none does in the primary deployment, and the floating
   default is the intended behavior. The version-pinning *knob* is gone; full per-version
   control is not a goal (re-add via a future issue if ever needed).
3. **`double` → `float` temperature type change.** Done end-to-end (no cast); see the
   commit-1 temperature subsection. No data migration; behavior-preserving on the wire.
   Forces ~8 mechanical test-literal suffix edits (covered by the hybrid contract).
4. **Token counts adopt `long?` (no cast).** `ChatCompletionResult` token fields and
   `ApiMetrics.RecordOpenAiCall` move to `long`/`long?` to match `UsageDetails`. Implicit
   widening means no other code or tests change. See the commit-1 token-counts subsection.
5. **Anthropic package — beta, but minimally exposed.** Use the **official `Anthropic` SDK
   v10+** (`12.24.1`), documented beta (breaking changes may land in minor/patch). **Our
   exposure to that churn is near-zero**: we touch only two of the SDK's native symbols —
   the `AnthropicClient` constructor and the `.AsIChatClient(model)` bridge — and everything
   downstream is the **GA-stable `Microsoft.Extensions.AI.Abstractions` `IChatClient`
   contract** the SDK conforms to. The `IChatService` boundary further confines any breakage
   to `BuildClient`. So this beta dependency is safe to track with the rest for our use
   cases; pin the exact version and the only realistic upgrade cost is the occasional
   two-line `BuildClient` fix. The one spot with genuine native-surface exposure is model
   discovery (see commit 3) — prefer the raw REST `/v1/models` call over the SDK's native
   listing to keep even that insulated. Do not confuse with `Anthropic.SDK` (tghamm
   community) or `tryAGI.Anthropic` (former `Anthropic` ≤3.x).
6. **`TreatWarningsAsErrors`.** The AI project treats warnings as errors; the obsolete
   `AsChatClient` would fail the build — `AsIChatClient` is mandatory.

## Verification plan

- Per-commit: `dotnet build` clean (warnings-as-errors) and the AI test suite green at each
  commit (each commit independently builds + passes).
- Commit 1: behavioral AI tests pass with unchanged assertion intent; the only test edits
  are the mechanical `double`→`float` suffixes and the default-constant test
  (`_Is0Point2` → `_Is1Point0`); test-leak audit agent reports no SK-type changes.
- Commits 2–3: manual smoke via the Settings UI "Test" flow against a real OpenRouter key
  and a real Anthropic key (model refresh + a completion + a vision call where supported).
- Migrations: none required (enum stored as int; rename preserves value 2).

## Commit sequence (single PR → `develop`)

1. `refactor(ai): replace Semantic Kernel with Microsoft.Extensions.AI`
   — also carries the temperature `double`→`float` type change and the `0.2`→`1.0` default
   constant (both noted in the commit body; neither is a logic change).
2. `feat(ai): rename LocalOpenAI provider to OpenAICompatible and add OpenRouter`
3. `feat(ai): add Anthropic (Claude) provider`
