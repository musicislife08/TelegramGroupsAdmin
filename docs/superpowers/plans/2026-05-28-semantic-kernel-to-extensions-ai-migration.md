# Semantic Kernel → Microsoft.Extensions.AI Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Semantic-Kernel-backed chat implementation with `Microsoft.Extensions.AI` (`IChatClient`) behind the unchanged `IChatService` interface, then add OpenRouter and Anthropic (Claude) providers — preceded by wiring AI config through the Data-DTO/mapping layer it was never connected to.

**Architecture:** All Semantic Kernel use sits behind one file (`SemanticKernelChatService.cs`). The migration rewrites that file against MEAI's `IChatClient`, leaving `IChatService`, `ChatCompletionResult`, `ChatCompletionOptions`, and all consumers untouched. AI provider config persists as a JSONB column; commit 1 routes it through `AIProviderConfigData` + a new `AIProviderConfigMappings` (matching the established `UserApiConfig` pattern) so the migration's data-layer edits are live, not inert. New providers are additive `BuildClient` branches.

**Tech Stack:** .NET 10, `Microsoft.Extensions.AI` 10.6.0, `Microsoft.Extensions.AI.OpenAI` 10.6.0, `OpenAI` 2.10.0, `Azure.AI.OpenAI` 2.1.0, official `Anthropic` SDK 12.24.1 (beta), PostgreSQL JSONB config, NUnit + bUnit + NSubstitute, Central Package Management.

**Source spec:** `docs/superpowers/specs/2026-05-28-semantic-kernel-to-extensions-ai-migration-design.md`

**Branch:** `refactor/semantic-kernel-to-extensions-ai` (already checked out). PR targets `develop`. Four commits, one per major section below. Conventional commits; commit messages end with the `Co-Authored-By` trailer.

---

## File Structure

### Commit 1 — AI config Data-DTO/mapping
| Action | File | Responsibility |
|---|---|---|
| Create | `TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs` | `ToData()`/`ToModel()` for the four pairs; `Features` keys `(int)`↔`(AIFeatureType)` |
| Modify | `TelegramGroupsAdmin.Data/Models/Configs/AIProviderConfigData.cs` | Int-keyed `Features` dict; add missing `ProfileScan` (key 5) default |
| Modify | `TelegramGroupsAdmin.Configuration/Repositories/SystemConfigRepository.cs` | Route AI config get/save through the DTO + mapping |
| Create | `TelegramGroupsAdmin.Data/Migrations/<ts>_RemapAIFeatureConfigKeysToInt.cs` | One-time JSONB remap of stored feature keys: names → ints |
| Create | `TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigMappingsTests.cs` | Round-trip + int-key wire-format coverage |
| Create | `TelegramGroupsAdmin.IntegrationTests/Configuration/AIFeatureKeyMigrationTests.cs` | Verifies the name→int conversion on a seeded row |

### Commit 2 — SK → MEAI swap
| Action | File | Responsibility |
|---|---|---|
| Modify | `Directory.Packages.props` | Drop SK packages, add MEAI/OpenAI/Azure package versions |
| Modify | `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj` | Swap package references, drop `SKEXP0010` |
| Modify | `TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj` | Remove dead SK package refs |
| Rename+Rewrite | `TelegramGroupsAdmin.AI/Services/SemanticKernelChatService.cs` → `ChatService.cs` | MEAI `IChatClient` implementation + disposal |
| Modify | `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs` | Register `ChatService` |
| Modify | `TelegramGroupsAdmin.AI/Services/ChatCompletionResult.cs` | Token fields `int?` → `long?` |
| Modify | `TelegramGroupsAdmin.AI/Services/ChatCompletionOptions.cs` | `Temperature` `double?` → `float?` |
| Modify | `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs` | `RecordOpenAiCall` token params → `long` |
| Modify | `TelegramGroupsAdmin.Configuration/Models/AIFeatureConfig.cs` | `Temperature` `double=0.2` → `float=1.0f` |
| Modify | `TelegramGroupsAdmin.Data/Models/Configs/AIFeatureConfigData.cs` | `Temperature` `double=0.2` → `float=1.0f` |
| Modify | `TelegramGroupsAdmin.Configuration/Models/AIConnection.cs` | Delete `AzureApiVersion` |
| Modify | `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs` | Delete `AzureApiVersion` |
| Modify | `TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs` | Drop `AzureApiVersion` from the mapping |
| Modify | `TelegramGroupsAdmin/Services/MemoryMetrics.cs` | Rename kernel gauge → chat_client |
| Modify | `TelegramGroupsAdmin.Core/QueryConstants.cs` | Doc-comment rename |
| Modify | `TelegramGroupsAdmin.AI/CLAUDE.md` | Update project-role/design-rule text |
| Modify | `TelegramGroupsAdmin/Components/Shared/ContentDetection/AIFeatureCard.razor` | Temperature field `double` → `float` |
| Modify | `TelegramGroupsAdmin/Components/Shared/AddAIConnectionDialog.razor` | Drop `AzureApiVersion` default-set |
| Modify | `TelegramGroupsAdmin/Components/Shared/Settings/AIConnectionCard.razor` | Remove API-version field + state |
| Modify | tests | Mechanical `double`→`float` suffixes, default-constant, `AzureApiVersion` removal |

### Commit 3 — OpenRouter + rename
| Action | File | Responsibility |
|---|---|---|
| Modify | `TelegramGroupsAdmin.Configuration/Models/AIProviderType.cs` | Rename `LocalOpenAI`→`OpenAICompatible` (=2), add `OpenRouter`=3 |
| Modify | `TelegramGroupsAdmin.AI/Services/ChatService.cs` | `OpenAICompatible` + `OpenRouter` branches |
| Modify | `TelegramGroupsAdmin.AI/Services/AIServiceFactory.cs` | OpenRouter model discovery |
| Modify | `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs` | Provider-int doc comment |
| Modify | UI (`AddAIConnectionDialog.razor`, `AIConnectionCard.razor`) | Rename item + add OpenRouter |

### Commit 4 — Anthropic
| Action | File | Responsibility |
|---|---|---|
| Modify | `Directory.Packages.props`, `TelegramGroupsAdmin.AI.csproj` | Add `Anthropic` 12.24.1 |
| Modify | `TelegramGroupsAdmin.Configuration/Models/AIProviderType.cs` | Add `Anthropic`=4 |
| Modify | `TelegramGroupsAdmin.AI/Services/ChatService.cs` | `Anthropic` branch |
| Modify | `TelegramGroupsAdmin.AI/Services/AIServiceFactory.cs` | Anthropic `/v1/models` discovery |
| Modify | UI | Add Anthropic provider item |

---

## Commit 1 — Wire AI config through the Data-DTO/mapping layer

**Why first:** AI config is the one config never wired through the `*Data`/`*Mappings` layer that #453/PR #465 established. `SystemConfigRepository` serializes the domain `AIProviderConfig` directly, and makes commit 2's `*Data` edits live. See the spec's "Persistence-layer gap".

**Critical correction (verified empirically 2026-05-28):** `System.Text.Json` serializes the `AIFeatureType`-keyed `Features` dictionary with **enum-name keys** (`{"SpamDetection": …}`), *not* numeric keys — confirmed by a throwaway probe. Existing production data therefore stores feature keys as names. This violates the project's "enums are always ints" rule, whose purpose is rename-safety: today renaming an `AIFeatureType` member (the open #282, SpamDetection→ContentDetection) would silently orphan stored config. This commit corrects that by storing feature keys as **ints** (matching the approved spec, which specifies numeric keys), via an int-keyed DTO + a one-time data migration of the single global config row (`chat_id = 0`). No back-compat shim (per project rules) — the migration rewrites the row once. The `Provider` field (an enum *value*) already serializes as an int and is unaffected.

### Task 1.1: Fix the `AIProviderConfigData` default drift

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/AIProviderConfigData.cs:21-27`

- [ ] **Step 1: Add the missing `ProfileScan` default key**

The domain `AIProviderConfig.Features` default has six entries (keys 0–5) but the DTO default has only five (0–4). Add key 5. Replace:

```csharp
    public Dictionary<int, AIFeatureConfigData> Features { get; set; } = new()
    {
        [0] = new(), // SpamDetection
        [1] = new(), // Translation
        [2] = new() { RequiresVision = true }, // ImageAnalysis
        [3] = new() { RequiresVision = true }, // VideoAnalysis
        [4] = new() // PromptBuilder
    };
```

with:

```csharp
    public Dictionary<int, AIFeatureConfigData> Features { get; set; } = new()
    {
        [0] = new(), // SpamDetection
        [1] = new(), // Translation
        [2] = new() { RequiresVision = true }, // ImageAnalysis
        [3] = new() { RequiresVision = true }, // VideoAnalysis
        [4] = new(), // PromptBuilder
        [5] = new() { RequiresVision = true } // ProfileScan
    };
```

- [ ] **Step 2: Build to confirm no syntax error**

Run: `dotnet build TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj`
Expected: Build succeeded.

### Task 1.2: Create the mapping

**Files:**
- Create: `TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs`

- [ ] **Step 1: Write the mapping file**

Follow the C# `extension(...)` member style used by the sibling mappings (e.g. `TelegramBotConfigMappings.cs`, `UserApiConfigMappings.cs`). Write `TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class AIProviderConfigMappings
{
    extension(AIProviderConfigData data)
    {
        public AIProviderConfig ToModel() => new()
        {
            Connections = data.Connections.Select(c => c.ToModel()).ToList(),
            Features = data.Features.ToDictionary(
                kvp => (AIFeatureType)kvp.Key,
                kvp => kvp.Value.ToModel())
        };
    }

    extension(AIProviderConfig model)
    {
        public AIProviderConfigData ToData() => new()
        {
            Connections = model.Connections.Select(c => c.ToData()).ToList(),
            Features = model.Features.ToDictionary(
                kvp => (int)kvp.Key,
                kvp => kvp.Value.ToData())
        };
    }

    extension(AIConnectionData data)
    {
        public AIConnection ToModel() => new()
        {
            Id = data.Id,
            Provider = (AIProviderType)data.Provider,
            Enabled = data.Enabled,
            AzureEndpoint = data.AzureEndpoint,
            AzureApiVersion = data.AzureApiVersion,
            LocalEndpoint = data.LocalEndpoint,
            LocalRequiresApiKey = data.LocalRequiresApiKey,
            AvailableModels = data.AvailableModels.Select(m => m.ToModel()).ToList(),
            ModelsLastFetched = data.ModelsLastFetched
        };
    }

    extension(AIConnection model)
    {
        public AIConnectionData ToData() => new()
        {
            Id = model.Id,
            Provider = (int)model.Provider,
            Enabled = model.Enabled,
            AzureEndpoint = model.AzureEndpoint,
            AzureApiVersion = model.AzureApiVersion,
            LocalEndpoint = model.LocalEndpoint,
            LocalRequiresApiKey = model.LocalRequiresApiKey,
            AvailableModels = model.AvailableModels.Select(m => m.ToData()).ToList(),
            ModelsLastFetched = model.ModelsLastFetched
        };
    }

    extension(AIFeatureConfigData data)
    {
        public AIFeatureConfig ToModel() => new()
        {
            ConnectionId = data.ConnectionId,
            Model = data.Model,
            MaxTokens = data.MaxTokens,
            Temperature = data.Temperature,
            AzureDeploymentName = data.AzureDeploymentName,
            RequiresVision = data.RequiresVision
        };
    }

    extension(AIFeatureConfig model)
    {
        public AIFeatureConfigData ToData() => new()
        {
            ConnectionId = model.ConnectionId,
            Model = model.Model,
            MaxTokens = model.MaxTokens,
            Temperature = model.Temperature,
            AzureDeploymentName = model.AzureDeploymentName,
            RequiresVision = model.RequiresVision
        };
    }

    extension(AIModelInfoData data)
    {
        public AIModelInfo ToModel() => new()
        {
            Id = data.Id,
            SizeBytes = data.SizeBytes
        };
    }

    extension(AIModelInfo model)
    {
        public AIModelInfoData ToData() => new()
        {
            Id = model.Id,
            SizeBytes = model.SizeBytes
        };
    }
}
```

> Note: `AzureApiVersion` is mapped here in commit 1 (it still exists on both types). Commit 2 deletes the property and removes these two lines.

- [ ] **Step 2: Build the Configuration project**

Run: `dotnet build TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj`
Expected: Build succeeded. (`extension(...)` is already used by sibling files, so the C# version supports it.)

### Task 1.3: Round-trip + backward-read tests (TDD)

**Files:**
- Create: `TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigMappingsTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigMappingsTests.cs`:

```csharp
using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class AIProviderConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static AIProviderConfig BuildPopulatedModel() => new()
    {
        Connections =
        [
            new AIConnection
            {
                Id = "openai-prod",
                Provider = AIProviderType.OpenAI,
                Enabled = true,
                AvailableModels =
                [
                    new AIModelInfo { Id = "gpt-4o" },
                    new AIModelInfo { Id = "llama3.2", SizeBytes = 7365960704 }
                ],
                ModelsLastFetched = DateTimeOffset.UnixEpoch
            },
            new AIConnection
            {
                Id = "azure-prod",
                Provider = AIProviderType.AzureOpenAI,
                Enabled = false,
                AzureEndpoint = "https://my-resource.openai.azure.com",
                AzureApiVersion = "2024-10-21"
            }
        ],
        Features = new()
        {
            [AIFeatureType.SpamDetection] = new() { ConnectionId = "openai-prod", Model = "gpt-4o", Temperature = 0.3, MaxTokens = 600 },
            [AIFeatureType.Translation] = new() { ConnectionId = "openai-prod", Model = "gpt-4o-mini" },
            [AIFeatureType.ImageAnalysis] = new() { RequiresVision = true },
            [AIFeatureType.VideoAnalysis] = new() { RequiresVision = true },
            [AIFeatureType.PromptBuilder] = new(),
            [AIFeatureType.ProfileScan] = new() { RequiresVision = true, AzureDeploymentName = "vision-deploy" }
        }
    };

    [Test]
    public void ToData_ThenToModel_RoundTripsAllFields()
    {
        var original = BuildPopulatedModel();

        var roundTripped = original.ToData().ToModel();

        Assert.That(roundTripped.Connections, Has.Count.EqualTo(2));
        Assert.That(roundTripped.Connections[0].Id, Is.EqualTo("openai-prod"));
        Assert.That(roundTripped.Connections[0].Provider, Is.EqualTo(AIProviderType.OpenAI));
        Assert.That(roundTripped.Connections[0].AvailableModels, Has.Count.EqualTo(2));
        Assert.That(roundTripped.Connections[0].AvailableModels[1].SizeBytes, Is.EqualTo(7365960704));
        Assert.That(roundTripped.Connections[0].ModelsLastFetched, Is.EqualTo(DateTimeOffset.UnixEpoch));
        Assert.That(roundTripped.Connections[1].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
        Assert.That(roundTripped.Connections[1].AzureEndpoint, Is.EqualTo("https://my-resource.openai.azure.com"));

        Assert.That(roundTripped.Features, Has.Count.EqualTo(6));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.3));
        Assert.That(roundTripped.Features[AIFeatureType.SpamDetection].MaxTokens, Is.EqualTo(600));
        Assert.That(roundTripped.Features[AIFeatureType.ProfileScan].RequiresVision, Is.True);
        Assert.That(roundTripped.Features[AIFeatureType.ProfileScan].AzureDeploymentName, Is.EqualTo("vision-deploy"));
    }

    [Test]
    public void ToData_SerializesFeatureKeysAsIntegers()
    {
        // The whole point of the DTO: feature keys persist as ints ("0".."5"), NOT enum
        // names. This is what makes a future AIFeatureType rename (#282) migration-free.
        var json = JsonSerializer.Serialize(BuildPopulatedModel().ToData(), JsonOptions);

        Assert.That(json, Does.Contain("\"0\":"));   // SpamDetection
        Assert.That(json, Does.Contain("\"5\":"));   // ProfileScan
        Assert.That(json, Does.Not.Contain("SpamDetection"));
        Assert.That(json, Does.Not.Contain("ProfileScan"));
    }

    [Test]
    public void IntKeyedJson_RoundTripsThroughDtoToModel()
    {
        // New stored format (int keys) reads back through the DTO to the domain model.
        var stored = JsonSerializer.Serialize(BuildPopulatedModel().ToData(), JsonOptions);

        var model = JsonSerializer.Deserialize<AIProviderConfigData>(stored, JsonOptions)!.ToModel();

        Assert.That(model.Features, Has.Count.EqualTo(6));
        Assert.That(model.Features[AIFeatureType.SpamDetection].Model, Is.EqualTo("gpt-4o"));
        Assert.That(model.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.3));
        Assert.That(model.Features[AIFeatureType.ProfileScan].RequiresVision, Is.True);
        Assert.That(model.Connections[1].Provider, Is.EqualTo(AIProviderType.AzureOpenAI));
    }
}
```

> Note: there is deliberately **no** "serializes identically to the old domain path" test — the old path wrote enum-*name* keys and the new path writes *int* keys; they are intentionally different, which is exactly why the data migration (Task 1.5) is required. Existing name-keyed rows are converted by that migration, covered by an integration test there.

- [ ] **Step 2: Run the tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AIProviderConfigMappingsTests`
Expected: all three pass. `ToData_SerializesFeatureKeysAsIntegers` is the load-bearing one — if it finds `SpamDetection` in the JSON, the DTO is still name-keyed; fix the DTO/mapping to use `Dictionary<int, AIFeatureConfigData>` with `(int)`/`(AIFeatureType)` key casts.

### Task 1.4: Route the repository through the mapping

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Repositories/SystemConfigRepository.cs:598` and `:614`

- [ ] **Step 1: Add the mappings using directive**

Confirm `using TelegramGroupsAdmin.Configuration.Mappings;` is present at the top of `SystemConfigRepository.cs` (the `UserApiConfig` path already uses these mappings, so it is likely there). If absent, add it.

- [ ] **Step 2: Route the read path**

Replace (in `GetAIProviderConfigAsync`):

```csharp
            return JsonSerializer.Deserialize<AIProviderConfig>(configRecord.AIProviderConfig, _jsonOptions);
```

with:

```csharp
            return JsonSerializer.Deserialize<AIProviderConfigData>(configRecord.AIProviderConfig, _jsonOptions)?.ToModel();
```

- [ ] **Step 3: Route the write path**

Replace (in `SaveAIProviderConfigAsync`):

```csharp
        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);
```

with:

```csharp
        // Serialize via the Data DTO (mirrors the UserApiConfig pattern)
        var jsonConfig = JsonSerializer.Serialize(config.ToData(), _jsonOptions);
```

- [ ] **Step 4: Build + run the existing AI config tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AIProviderConfig`
Expected: `AIProviderConfigTests`, `AIProviderConfigMappingsTests` pass.

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~AIProviderConfigIntegrationTests` (background — slow Testcontainers suite)
Expected: green. These tests write via the repository (now int-keyed) and read back, so they round-trip cleanly. The *existing-data* (name→int) conversion is validated separately in Task 1.5.

### Task 1.5: Data migration — convert stored feature keys from names to ints

**Files:**
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_RemapAIFeatureConfigKeysToInt.cs` (via `dotnet ef`)
- Create: `TelegramGroupsAdmin.IntegrationTests/Configuration/AIFeatureKeyMigrationTests.cs`

Existing production data stores the `features` object with enum-*name* keys (`{"SpamDetection": …}`). The int-keyed DTO cannot deserialize those (`"SpamDetection"` is not an `int` key). A one-time migration rewrites the single global config row. No back-compat read shim (per project rules).

- [ ] **Step 1: Generate an empty migration**

Run: `dotnet ef migrations add RemapAIFeatureConfigKeysToInt -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`
This produces an empty `Up`/`Down` (no model change — the DTO is serialized into a JSONB string column, not an EF entity). Verify it generated cleanly; if `dotnet ef` reports pending model changes unrelated to this work, STOP and report — do not bundle unrelated schema drift.

- [ ] **Step 2: Fill in the Up/Down with the JSONB key remap**

Edit the generated migration so `Up` rewrites name keys → int keys and `Down` reverses it. The column is `configs.ai_provider_config` (jsonb); AI config is the global row but the `WHERE` guard covers any row that has a `features` object. Both directions are idempotent (already-correct keys fall through the `ELSE`):

```csharp
public partial class RemapAIFeatureConfigKeysToInt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE configs
            SET ai_provider_config = jsonb_set(
                ai_provider_config,
                '{features}',
                (
                    SELECT COALESCE(jsonb_object_agg(
                        CASE elem.key
                            WHEN 'SpamDetection' THEN '0'
                            WHEN 'Translation'   THEN '1'
                            WHEN 'ImageAnalysis' THEN '2'
                            WHEN 'VideoAnalysis' THEN '3'
                            WHEN 'PromptBuilder' THEN '4'
                            WHEN 'ProfileScan'   THEN '5'
                            ELSE elem.key
                        END, elem.value), '{}'::jsonb)
                    FROM jsonb_each(ai_provider_config -> 'features') AS elem
                ))
            WHERE ai_provider_config IS NOT NULL
              AND ai_provider_config ? 'features';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE configs
            SET ai_provider_config = jsonb_set(
                ai_provider_config,
                '{features}',
                (
                    SELECT COALESCE(jsonb_object_agg(
                        CASE elem.key
                            WHEN '0' THEN 'SpamDetection'
                            WHEN '1' THEN 'Translation'
                            WHEN '2' THEN 'ImageAnalysis'
                            WHEN '3' THEN 'VideoAnalysis'
                            WHEN '4' THEN 'PromptBuilder'
                            WHEN '5' THEN 'ProfileScan'
                            ELSE elem.key
                        END, elem.value), '{}'::jsonb)
                    FROM jsonb_each(ai_provider_config -> 'features') AS elem
                ))
            WHERE ai_provider_config IS NOT NULL
              AND ai_provider_config ? 'features';
            """);
    }
}
```

Review the generated file per the Data project's CLAUDE.md (no DROP/CREATE, no `DISABLE TRIGGER ALL` — this is a pure `UPDATE`, so neither applies). Keep the whole statement in the single `Sql(...)` call for transactional atomicity.

- [ ] **Step 3: Write an integration test for the conversion (TDD)**

Write `TelegramGroupsAdmin.IntegrationTests/Configuration/AIFeatureKeyMigrationTests.cs`. It seeds a `configs` row with OLD name-keyed JSON, executes the same remap SQL, and asserts the keys became ints and values survived. Use the existing integration-test base/fixture pattern in that project (match how sibling tests obtain a `DbContext`/connection against the Testcontainers Postgres — read one sibling test first to mirror setup/teardown exactly):

```csharp
// Mirror the existing fixture/base used by AIProviderConfigIntegrationTests for
// container + DbContext acquisition. Pseudocode for the body:
//
// 1. Insert a configs row (chat_id = 0) with ai_provider_config =
//    '{"connections":[],"features":{"SpamDetection":{"model":"gpt-4o","maxTokens":600,
//      "temperature":0.2,"requiresVision":false,"connectionId":null,"azureDeploymentName":null},
//      "ProfileScan":{"model":"gpt-4o-mini","maxTokens":500,"temperature":0.2,
//      "requiresVision":true,"connectionId":null,"azureDeploymentName":null}}}'
//    via ExecuteSqlRawAsync.
// 2. Execute the EXACT Up SQL from the migration via ExecuteSqlRawAsync.
// 3. Read ai_provider_config back; assert the features object now has keys "0" and "5"
//    and NOT "SpamDetection"/"ProfileScan", and that a value (e.g. maxTokens 600) survived.
// 4. Then assert it now deserializes through the int-keyed DTO:
//    JsonSerializer.Deserialize<AIProviderConfigData>(json, opts).ToModel() yields
//    Features[AIFeatureType.SpamDetection].MaxTokens == 600.
```

The reviewer/implementer must flesh this out against the real fixture; the assertions above are the contract. Keep the seeded JSON's property names camelCased to match `_jsonOptions`.

- [ ] **Step 4: Apply migrations locally and run the migration test**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only` (applies migrations to the local dev DB and exits cleanly).
Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~AIFeatureKeyMigrationTests` (background — Testcontainers).
Expected: migration applies without error; the conversion test passes.

### Task 1.6: Commit

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, no warnings (warnings-as-errors on the AI project).

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs \
        TelegramGroupsAdmin.Data/Models/Configs/AIProviderConfigData.cs \
        TelegramGroupsAdmin.Configuration/Repositories/SystemConfigRepository.cs \
        TelegramGroupsAdmin.Data/Migrations/ \
        TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigMappingsTests.cs \
        TelegramGroupsAdmin.IntegrationTests/Configuration/AIFeatureKeyMigrationTests.cs
git commit -F- <<'EOF'
refactor(config): wire AI provider config through Data-DTO/mapping layer

Finishes the #453 / PR #465 sweep for the one config it missed. AI config was
serializing the domain AIProviderConfig directly; route it through
AIProviderConfigData + a new AIProviderConfigMappings, mirroring the
UserApiConfig pattern in the same repository.

Also corrects a latent persistence bug: System.Text.Json serialized the
AIFeatureType-keyed Features dictionary with enum-NAME keys ("SpamDetection"),
which welds stored config to member names and would break on an AIFeatureType
rename (#282). The DTO now stores feature keys as ints ("0".."5") per the
project's enums-are-ints rule; a one-time data migration rewrites the existing
global config row (name keys -> int keys, idempotent). Provider (an enum value)
already serialized as an int. Restores the ProfileScan (key 5) default the DTO
was missing.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Commit 2 — Swap SK → MEAI (refactor; no logic changes)

This is the core. After it, `IChatService` and all consumers are unchanged; only the implementation and provider-config plumbing change. Includes the temperature `double`→`float` type change (+ default `0.2`→`1.0`) and token `int`→`long` widening, made together.

### Task 2.1: Package surface

**Files:**
- Modify: `Directory.Packages.props:47-48`
- Modify: `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj:9,17`
- Modify: `TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj`

- [ ] **Step 1: Update central package versions**

In `Directory.Packages.props`, replace these two lines:

```xml
    <PackageVersion Include="Microsoft.SemanticKernel" Version="1.74.0" />
    <PackageVersion Include="Microsoft.SemanticKernel.Abstractions" Version="1.74.0" />
```

with:

```xml
    <PackageVersion Include="Microsoft.Extensions.AI" Version="10.6.0" />
    <PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="10.6.0" />
    <PackageVersion Include="OpenAI" Version="2.10.0" />
    <PackageVersion Include="Azure.AI.OpenAI" Version="2.1.0" />
```

Also bump two existing abstraction packages from `10.0.7` → `10.0.8` — `Microsoft.Extensions.AI` 10.6.0 declares a `10.0.8` floor on both, so leaving them at 10.0.7 produces `NU1109` package-downgrade errors under Central Package Management (verified against the MEAI nuspec; `Microsoft.Extensions.Caching.Hybrid`/ContentDetection already pull 10.0.8 transitively):

```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.8" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
```

- [ ] **Step 2: Update the AI project references**

In `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`, remove the `SKEXP0010` NoWarn line:

```xml
    <!-- Suppress Semantic Kernel experimental API warnings for custom endpoint support -->
    <NoWarn>SKEXP0010</NoWarn>
```

and replace the package reference:

```xml
    <PackageReference Include="Microsoft.SemanticKernel" />
```

with:

```xml
    <PackageReference Include="Microsoft.Extensions.AI" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
    <PackageReference Include="OpenAI" />
    <PackageReference Include="Azure.AI.OpenAI" />
```

- [ ] **Step 3: Remove dead SK refs from ComponentTests csproj**

Find the two `Microsoft.SemanticKernel*` `PackageReference` lines in `TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj` and delete them.

Run: `grep -n "SemanticKernel" TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj`
Expected after deletion: no output.

> Do not build yet — `SemanticKernelChatService.cs` still references SK and will fail until Task 2.4.

### Task 2.2: Token counts `int?` → `long?`

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/ChatCompletionResult.cs:17,22,27`
- Modify: `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs:65`

- [ ] **Step 1: Widen the result token fields**

In `ChatCompletionResult.cs`, change the three token properties from `int?` to `long?`:

```csharp
    /// <summary>
    /// Total tokens used (prompt + completion) if available
    /// </summary>
    public long? TotalTokens { get; init; }

    /// <summary>
    /// Prompt tokens used if available
    /// </summary>
    public long? PromptTokens { get; init; }

    /// <summary>
    /// Completion tokens used if available
    /// </summary>
    public long? CompletionTokens { get; init; }
```

- [ ] **Step 2: Widen the metric recording signature**

In `ApiMetrics.cs:65`, change:

```csharp
    public void RecordOpenAiCall(string feature, string model, int promptTokens, int completionTokens, double durationMs, bool success)
```

to:

```csharp
    public void RecordOpenAiCall(string feature, string model, long promptTokens, long completionTokens, double durationMs, bool success)
```

The method body is unchanged — `_openAiTokensTotal` is already `Counter<long>`, and `_openAiTokensTotal.Add(promptTokens, ...)` now takes a `long` directly (today it implicitly widens an `int`).

### Task 2.3: Temperature `double` → `float` and default `0.2` → `1.0`

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/ChatCompletionOptions.cs:16`
- Modify: `TelegramGroupsAdmin.Configuration/Models/AIFeatureConfig.cs:26`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/AIFeatureConfigData.cs:26`

- [ ] **Step 1: Options type**

In `ChatCompletionOptions.cs`, change:

```csharp
    public double? Temperature { get; init; }
```

to:

```csharp
    public float? Temperature { get; init; }
```

- [ ] **Step 2: Domain feature-config default**

In `AIFeatureConfig.cs`, change:

```csharp
    public double Temperature { get; set; } = 0.2;
```

to:

```csharp
    public float Temperature { get; set; } = 1.0f;
```

- [ ] **Step 3: DTO feature-config default**

In `AIFeatureConfigData.cs`, change:

```csharp
    public double Temperature { get; set; } = 0.2;
```

to:

```csharp
    public float Temperature { get; set; } = 1.0f;
```

> The mapping (`AIProviderConfigMappings.cs`) assigns `Temperature` directly — both `float` now, no cast. No migration: `0.2f`/`0.2d` both serialize to `"0.2"`; the default change only affects feature configs that never had a temperature explicitly set.

### Task 2.4: Rewrite the chat service (rename + MEAI)

**Files:**
- Rename: `TelegramGroupsAdmin.AI/Services/SemanticKernelChatService.cs` → `TelegramGroupsAdmin.AI/Services/ChatService.cs`
- Modify: `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs:12`

- [ ] **Step 1: Rename the file via git**

Run: `git mv TelegramGroupsAdmin.AI/Services/SemanticKernelChatService.cs TelegramGroupsAdmin.AI/Services/ChatService.cs`

- [ ] **Step 2: Replace the entire file contents**

Overwrite `TelegramGroupsAdmin.AI/Services/ChatService.cs` with:

```csharp
using System.ClientModel;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.AI.Services;

/// <summary>
/// Implementation of IChatService using Microsoft.Extensions.AI (IChatClient).
/// Supports OpenAI, Azure OpenAI, and OpenAI-compatible local endpoints.
/// Static client cache persists across scoped instances for reuse; evicted
/// clients are disposed (IChatClient : IDisposable).
/// </summary>
public class ChatService : IChatService
{
    // Static cache - persists across scoped instances for client reuse.
    // Thread safety: ConcurrentDictionary + GetOrAdd provides atomic access.
    // Cache bounds: expected <10 entries (connections × models). Entries are
    // disposed on eviction via InvalidateCache().
    private static readonly ConcurrentDictionary<string, CachedClient> ClientCache = new();
    private readonly ISystemConfigRepository _configRepository;
    private readonly ILogger<ChatService> _logger;
    private readonly ApiMetrics _apiMetrics;
    private readonly CacheMetrics _cacheMetrics;

    public static int CachedClientCount => ClientCache.Count;

    public ChatService(
        ISystemConfigRepository configRepository,
        ILogger<ChatService> logger,
        ApiMetrics apiMetrics,
        CacheMetrics cacheMetrics)
    {
        _configRepository = configRepository;
        _logger = logger;
        _apiMetrics = apiMetrics;
        _cacheMetrics = cacheMetrics;
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting chat completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw; // Let caller handle the exception
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetVisionCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI vision call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, [new TextContent(userPrompt), new DataContent(imageData, mimeType)])
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting vision completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw; // Let caller handle the exception
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetVisionCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<ImageInput> images,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI multi-image vision call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var contents = new List<AIContent> { new TextContent(userPrompt) };
            foreach (var image in images)
            {
                contents.Add(new DataContent(image.Data, image.MimeType));
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, contents)
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting multi-image vision completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsFeatureAvailableAsync(AIFeatureType feature, CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return false;

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
            return false;

        var connection = config.Connections.SingleOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null || !connection.Enabled)
            return false;

        // Check API key for non-local providers
        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
            var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void InvalidateCache(string? connectionId = null)
    {
        if (connectionId == null)
        {
            foreach (var entry in ClientCache.Values)
            {
                entry.Client.Dispose();
            }
            ClientCache.Clear();
            _logger.LogDebug("Cleared all cached AI chat clients");
        }
        else
        {
            // Remove all cache entries for this connection (keys are delimited with "|")
            var keysToRemove = ClientCache.Keys.Where(k => k.StartsWith(connectionId + "|")).ToList();
            foreach (var key in keysToRemove)
            {
                if (ClientCache.TryRemove(key, out var removed))
                {
                    removed.Client.Dispose();
                }
            }
            _logger.LogDebug("Invalidated cached AI chat client for connection {ConnectionId}", connectionId);
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> TestCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = await GetOrCreateTestClientAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (clientInfo == null)
        {
            _logger.LogDebug("Test client not available for connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test completion call to {Model}", clientInfo.ModelId);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var chatOptions = CreateChatOptions(options);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            _logger.LogDebug("MEAI Response - Text: '{Text}', ModelId: {ModelId}, FinishReason: {FinishReason}",
                response.Text.Length > QueryConstants.DefaultLogTruncationLength
                    ? response.Text[..QueryConstants.DefaultLogTruncationLength]
                    : response.Text,
                response.ModelId ?? "(null)",
                response.FinishReason?.ToString() ?? "(null)");

            var result = CreateResult(response, clientInfo.ModelId);
            _logger.LogDebug("Test completion returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test completion failed for {Model}: {Message}",
                clientInfo.ModelId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> TestVisionCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = await GetOrCreateTestClientAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (clientInfo == null)
        {
            _logger.LogDebug("Test client not available for vision call, connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test vision call to {Model} with {ImageSize} bytes",
                clientInfo.ModelId, imageData.Length);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, [new TextContent(userPrompt), new DataContent(imageData, mimeType)])
            };

            var chatOptions = CreateChatOptions(options);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            var result = CreateResult(response, clientInfo.ModelId);
            _logger.LogDebug("Test vision returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test vision call failed for {Model}: {Message}",
                clientInfo.ModelId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get or create a client for testing a specific connection+model combo.
    /// Does not use feature config - uses provided model/deployment directly.
    /// </summary>
    private async Task<CachedClient?> GetOrCreateTestClientAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return null;

        var connection = config.Connections.SingleOrDefault(c => c.Id == connectionId);
        if (connection == null || !connection.Enabled)
        {
            _logger.LogWarning("Test connection {ConnectionId} not found or disabled", connectionId);
            return null;
        }

        var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for test connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        var testFeatureConfig = new AIFeatureConfig
        {
            ConnectionId = connectionId,
            Model = model,
            AzureDeploymentName = azureDeploymentName
        };

        var cacheKey = GenerateCacheKey(connection, testFeatureConfig, apiKey);

        if (ClientCache.TryGetValue(cacheKey, out var cachedClient))
        {
            _cacheMetrics.RecordHit("chat_client");
            return cachedClient;
        }

        _cacheMetrics.RecordMiss("chat_client");
        cachedClient = ClientCache.GetOrAdd(cacheKey, _ =>
        {
            var client = BuildClient(connection, testFeatureConfig, apiKey);
            var modelId = connection.Provider == AIProviderType.AzureOpenAI
                ? azureDeploymentName ?? model
                : model;

            _logger.LogDebug("Created and cached test client for connection {ConnectionId}, model {Model}",
                connection.Id, modelId);

            return new CachedClient(client, modelId);
        });

        return cachedClient;
    }

    /// <summary>
    /// Get or create a cached IChatClient for the specified feature.
    /// </summary>
    private async Task<ClientLookupResult?> GetOrCreateClientAsync(AIFeatureType feature, CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return null;

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
            return null;

        var connection = config.Connections.SingleOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null || !connection.Enabled)
            return null;

        var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        var cacheKey = GenerateCacheKey(connection, featureConfig, apiKey);

        var conn = connection;
        var featConfig = featureConfig;
        var key = apiKey;

        try
        {
            if (ClientCache.TryGetValue(cacheKey, out var cachedClient))
            {
                _cacheMetrics.RecordHit("chat_client");
            }
            else
            {
                _cacheMetrics.RecordMiss("chat_client");
                cachedClient = ClientCache.GetOrAdd(cacheKey, _ =>
                {
                    var client = BuildClient(conn, featConfig, key);
                    var modelId = conn.Provider == AIProviderType.AzureOpenAI
                        ? featConfig.AzureDeploymentName ?? featConfig.Model
                        : featConfig.Model;

                    _logger.LogDebug("Created and cached client for connection {ConnectionId}, model {Model}",
                        conn.Id, modelId);

                    return new CachedClient(client, modelId);
                });
            }

            return new ClientLookupResult(cachedClient, featureConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create client for connection {ConnectionId}", connection.Id);
            throw;
        }
    }

    /// <summary>
    /// Generate a cache key that changes when relevant config changes.
    /// </summary>
    /// <remarks>
    /// MaxTokens and Temperature are intentionally NOT included - they are per-request
    /// ChatOptions, not client configuration; the client is reused across requests.
    /// </remarks>
    private static string GenerateCacheKey(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        return string.Join("|",
            connection.Id,
            connection.Provider.ToString(),
            featureConfig.Model ?? "",
            featureConfig.AzureDeploymentName ?? "",
            connection.AzureEndpoint ?? "",
            connection.LocalEndpoint ?? "",
            apiKey ?? "");
    }

    /// <summary>
    /// Build an IChatClient for the given connection and feature config.
    /// Transport is left unset so the OpenAI SDK uses its default shared transport
    /// (System.ClientModel HttpClientPipelineTransport.Shared), pooling the HTTP handler
    /// across all clients — no IHttpClientFactory dependency, no per-client handler.
    /// Static: no instance state is needed.
    /// </summary>
    private static IChatClient BuildClient(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        switch (connection.Provider)
        {
            case AIProviderType.OpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OpenAI API key is required");

                return new OpenAIClient(new ApiKeyCredential(apiKey))
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();

            case AIProviderType.AzureOpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Azure OpenAI API key is required");
                if (string.IsNullOrWhiteSpace(connection.AzureEndpoint))
                    throw new InvalidOperationException("Azure endpoint is required");
                if (string.IsNullOrWhiteSpace(featureConfig.AzureDeploymentName))
                    throw new InvalidOperationException("Azure deployment name is required");

                return new AzureOpenAIClient(
                        new Uri(connection.AzureEndpoint),
                        new ApiKeyCredential(apiKey))
                    .GetChatClient(featureConfig.AzureDeploymentName)
                    .AsIChatClient();

            case AIProviderType.LocalOpenAI:
                if (string.IsNullOrWhiteSpace(connection.LocalEndpoint))
                    throw new InvalidOperationException("Local endpoint is required");

                // Ollama and other keyless providers - use placeholder API key
                var localApiKey = string.IsNullOrWhiteSpace(apiKey) ? "not-required" : apiKey;

                return new OpenAIClient(
                        new ApiKeyCredential(localApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(connection.LocalEndpoint) })
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();

            default:
                throw new InvalidOperationException($"Unsupported AI provider type: {connection.Provider}");
        }
    }

    /// <summary>
    /// Apply feature config defaults to caller-provided options.
    /// Caller-specified values take precedence over config defaults.
    /// </summary>
    private static ChatCompletionOptions ApplyFeatureConfigDefaults(ChatCompletionOptions? options, AIFeatureConfig featureConfig)
    {
        return new ChatCompletionOptions
        {
            MaxTokens = options?.MaxTokens ?? featureConfig.MaxTokens,
            Temperature = options?.Temperature ?? featureConfig.Temperature,
            JsonMode = options?.JsonMode ?? false
        };
    }

    /// <summary>
    /// Create MEAI ChatOptions from our options.
    /// </summary>
    private static ChatOptions CreateChatOptions(ChatCompletionOptions? options)
    {
        var chatOptions = new ChatOptions();

        if (options?.MaxTokens.HasValue == true)
            chatOptions.MaxOutputTokens = options.MaxTokens.Value;

        if (options?.Temperature.HasValue == true)
            chatOptions.Temperature = options.Temperature.Value;

        if (options?.JsonMode == true)
            chatOptions.ResponseFormat = ChatResponseFormat.Json;

        return chatOptions;
    }

    /// <summary>
    /// Create result from the MEAI ChatResponse.
    /// </summary>
    private static ChatCompletionResult? CreateResult(ChatResponse response, string fallbackModelId)
    {
        var content = response.Text;
        if (string.IsNullOrEmpty(content))
            return null;

        return new ChatCompletionResult
        {
            Content = content,
            Model = response.ModelId ?? fallbackModelId,
            TotalTokens = response.Usage?.TotalTokenCount,
            PromptTokens = response.Usage?.InputTokenCount,
            CompletionTokens = response.Usage?.OutputTokenCount,
            FinishReason = response.FinishReason?.ToString()
        };
    }

    /// <summary>
    /// Cached IChatClient with its resolved model id.
    /// </summary>
    private sealed record CachedClient(IChatClient Client, string ModelId);

    /// <summary>
    /// Client lookup result including feature config defaults for ChatOptions.
    /// </summary>
    private sealed record ClientLookupResult(CachedClient Client, AIFeatureConfig FeatureConfig);
}
```

- [ ] **Step 3: Update DI registration**

In `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs`, replace:

```csharp
        // AI services (Semantic Kernel multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository), kernel cache is static
        services.AddScoped<IChatService, SemanticKernelChatService>();
```

with:

```csharp
        // AI services (Microsoft.Extensions.AI multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository); the IChatClient cache is static
        services.AddScoped<IChatService, ChatService>();
```

### Task 2.5: Remove `AzureApiVersion` (no longer pinned)

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/AIConnection.cs:29-32`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs:29-32`
- Modify: `TelegramGroupsAdmin.Configuration/Mappings/AIProviderConfigMappings.cs`

- [ ] **Step 1: Delete the domain property**

In `AIConnection.cs`, delete:

```csharp
    /// <summary>
    /// Azure OpenAI API version (default: 2024-10-21)
    /// </summary>
    public string? AzureApiVersion { get; set; } = "2024-10-21";
```

- [ ] **Step 2: Delete the DTO property**

In `AIConnectionData.cs`, delete:

```csharp
    /// <summary>
    /// Azure OpenAI API version
    /// </summary>
    public string? AzureApiVersion { get; set; } = "2024-10-21";
```

- [ ] **Step 3: Drop the mapping lines**

In `AIProviderConfigMappings.cs`, delete the `AzureApiVersion = data.AzureApiVersion,` line from `AIConnectionData.ToModel()` and the `AzureApiVersion = model.AzureApiVersion,` line from `AIConnection.ToData()`.

> The cache key already omits `AzureApiVersion` (the rewritten `GenerateCacheKey` never referenced it), and `BuildClient`'s Azure branch passes no `apiVersion`. Old stored JSONB keeps an `azureApiVersion` key; `System.Text.Json` ignores unknown keys on read — no migration.

### Task 2.6: Knock-on renames (metrics gauge, doc comments, CLAUDE.md)

**Files:**
- Modify: `TelegramGroupsAdmin/Services/MemoryMetrics.cs:95-99`
- Modify: `TelegramGroupsAdmin.Core/QueryConstants.cs:21,27`
- Modify: `TelegramGroupsAdmin.AI/CLAUDE.md`

- [ ] **Step 1: Rename the cache gauge**

In `MemoryMetrics.cs`, replace:

```csharp
        // --- Semantic Kernel cache (static) ---
        meter.CreateObservableGauge(
            "tga.cache.kernel.count",
            () => SemanticKernelChatService.CachedKernelCount,
            description: "Number of cached Semantic Kernel instances");
```

with:

```csharp
        // --- Chat client cache (static) ---
        meter.CreateObservableGauge(
            "tga.cache.chat_client.count",
            () => ChatService.CachedClientCount,
            description: "Number of cached Microsoft.Extensions.AI IChatClient instances");
```

- [ ] **Step 2: Fix QueryConstants doc comments**

Run: `grep -n "SemanticKernelChatService" TelegramGroupsAdmin.Core/QueryConstants.cs`
For each line found (around `:21,27`), replace `SemanticKernelChatService` with `ChatService` in the doc-comment text.

- [ ] **Step 3: Update the AI project CLAUDE.md**

In `TelegramGroupsAdmin.AI/CLAUDE.md`, replace the Project Role line:

```markdown
Owns AI service abstractions and implementations: chat completion via Semantic Kernel, AI-based translation, AI feature factories, and AI feature test runners.
```

with:

```markdown
Owns AI service abstractions and implementations: chat completion via Microsoft.Extensions.AI (IChatClient), AI-based translation, AI feature factories, and AI feature test runners.
```

Replace the two Design-Rules bullets that mention Semantic Kernel:

```markdown
- The Semantic Kernel kernel cache in `SemanticKernelChatService` is `static` and persists across scoped instances — do not turn it into instance state.
- This project owns the `Microsoft.SemanticKernel` package reference. Do not add it to `Core` or anywhere else.
```

with:

```markdown
- The IChatClient cache in `ChatService` is `static` and persists across scoped instances — do not turn it into instance state. Evicted clients are disposed (IChatClient : IDisposable).
- This project owns the `Microsoft.Extensions.AI*` package references. Do not add them to `Core` or anywhere else.
```

### Task 2.7: UI temperature field + AzureApiVersion removal

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/AIFeatureCard.razor:120-127,291-294`
- Modify: `TelegramGroupsAdmin/Components/Shared/AddAIConnectionDialog.razor:120-128`
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/AIConnectionCard.razor`

- [ ] **Step 1: AIFeatureCard temperature field type**

In `AIFeatureCard.razor`, change the temperature `MudNumericField` from `double` to `float`:

```razor
                <MudNumericField T="float"
                                 Value="@FeatureConfig.Temperature"
                                 ValueChanged="@OnTemperatureChanged"
                                 Label="Temperature"
                                 Variant="Variant.Outlined"
                                 Min="0.0f" Max="2.0f" Step="0.1f"
                                 Format="F1"
                                 Style="flex: 1;" />
```

And the handler:

```csharp
    private void OnTemperatureChanged(float value)
    {
        FeatureConfig.Temperature = value;
        _testResult = null; // Clear test result - config changed, must re-test
    }
```

> `Max="2.0f"` is retained intentionally — provider-aware min/max is deferred to issue #480.

- [ ] **Step 2: AddAIConnectionDialog — drop the AzureApiVersion default-set**

In `AddAIConnectionDialog.razor`, replace the `Submit()` connection construction:

```csharp
        var connection = new AIConnection
        {
            Id = _connectionId.Trim().ToLowerInvariant(),
            Provider = _provider,
            Enabled = _enabled,
            // Set defaults based on provider type
            AzureApiVersion = _provider == AIProviderType.AzureOpenAI ? "2024-10-21" : null,
            LocalRequiresApiKey = false
        };
```

with:

```csharp
        var connection = new AIConnection
        {
            Id = _connectionId.Trim().ToLowerInvariant(),
            Provider = _provider,
            Enabled = _enabled,
            LocalRequiresApiKey = false
        };
```

- [ ] **Step 3: AIConnectionCard — remove the API-version field + state**

In `AIConnectionCard.razor`, delete the API-version `MudTextField` inside the Azure block:

```razor
            <MudTextField @bind-Value="_azureApiVersion"
                         Label="API Version"
                         Variant="Variant.Outlined"
                         HelperText="e.g., 2024-02-01" />
```

Delete the field declaration:

```csharp
    private string? _azureApiVersion;
```

Delete the load line in `OnParametersSet`:

```csharp
        _azureApiVersion = Connection.AzureApiVersion;
```

Delete the save line in `SaveAsync`:

```csharp
            Connection.AzureApiVersion = _azureApiVersion;
```

### Task 2.8: Mechanical test edits

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigTests.cs:71-77,150-156,352`
- Modify: `TelegramGroupsAdmin.UnitTests/Configuration/AIProviderConfigMappingsTests.cs` (the `0.3` literal)
- Modify: `TelegramGroupsAdmin.IntegrationTests/Configuration/AIProviderConfigIntegrationTests.cs`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/AIConnectionCardTests.cs:18-37,113-126`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/AIFeatureCardTests.cs:32-39`

- [ ] **Step 1: AIProviderConfigTests — default-temperature test**

Rename the test and assert the new constant. Replace:

```csharp
    public void AIFeatureConfig_DefaultTemperature_Is0Point2()
    {
```
...
```csharp
        Assert.That(config.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(0.2));
```

with:

```csharp
    public void AIFeatureConfig_DefaultTemperature_Is1Point0()
    {
```
...
```csharp
        Assert.That(config.Features[AIFeatureType.SpamDetection].Temperature, Is.EqualTo(1.0f));
```

- [ ] **Step 2: AIProviderConfigTests — delete the Azure-api-version default test**

Delete the entire test method:

```csharp
    public void AIConnection_DefaultAzureApiVersion_Is2024_10_21()
    {
        // ...
        Assert.That(connection.AzureApiVersion, Is.EqualTo("2024-10-21"));
    }
```

(including its `[Test]` attribute and any leading doc comment).

- [ ] **Step 3: AIProviderConfigTests — fix the round-trip Azure assertion + temperature literal**

In the serialization round-trip test that sets `AzureApiVersion = "2024-10-01"` (around `:339`), delete the `AzureApiVersion = "2024-10-01"` line from the test connection setup and delete the assertion:

```csharp
            Assert.That(deserialized.Connections[0].AzureApiVersion, Is.EqualTo("2024-10-01"));
```

For the `Temperature = 0.5` literal (around `:231`), change to `Temperature = 0.5f`.

- [ ] **Step 4: AIProviderConfigMappingsTests — float literal**

In the mappings test written in Task 1.3, the `Temperature = 0.3` literals now assign to a `float` property. Change `Temperature = 0.3` → `Temperature = 0.3f` and the assertions `Is.EqualTo(0.3)` → `Is.EqualTo(0.3f)`.

- [ ] **Step 5: AIProviderConfigIntegrationTests — temperature suffixes + Azure-version removal**

For each `Temperature = 0.2/0.3/0.1/0.5` literal (lines ~109, 275, 282, 289, 296, 303), append `f` (e.g. `Temperature = 0.2f`). For the assertion at ~`:320` `Is.EqualTo(0.2)` → `Is.EqualTo(0.2f)`. Delete each `AzureApiVersion = "2024-10-21"` line (lines ~190, 227, 338).

> Leave the `LocalEndpoint = "http://localhost:8080/v1?api_version=2024-01-01&timeout=30"` strings (lines ~615, 626) untouched — those are endpoint query params, unrelated to the removed field.

- [ ] **Step 6: AIConnectionCardTests — drop the azureApiVersion param + assertion**

In `CreateConnection`, delete the parameter `string? azureApiVersion = null,` and the object-initializer line `AzureApiVersion = azureApiVersion,`. In `ShowsAzureFields_ForAzureProvider`, delete the `azureApiVersion: "2024-02-01"` argument and the assertion `Assert.That(cut.Markup, Does.Contain("API Version"));` (the field no longer renders).

- [ ] **Step 7: AIFeatureCardTests — temperature param type**

In the test helper at `:32`, change `double temperature = 0.7` → `float temperature = 0.7f`.

### Task 2.9: Build, test, and verify

- [ ] **Step 1: Build the solution (warnings-as-errors)**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings. If a `[Experimental]`/obsolete warning appears (e.g. accidental `AsChatClient`), fix the call — do not suppress.

- [ ] **Step 2: Run the AI behavioral + config test suites**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~AIContentCheck|FullyQualifiedName~ExamEvaluationService|FullyQualifiedName~ProfileScoringEngine|FullyQualifiedName~AIProviderConfig"`
Expected: green, with no change to assertion intent (only the mechanical edits above).

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~AIConnectionCard|FullyQualifiedName~AIFeatureCard|FullyQualifiedName~FeatureTestService"`
Expected: green.

- [ ] **Step 3: Add the disposal test (TDD for the one new behavior)**

The spec's Risk #1 calls for an explicit test that eviction disposes. `ChatService` builds concrete OpenAI clients internally, so a fake `IChatClient` can't be injected through the public path. Rather than add a production test seam (keep production clean), reach the private static `ClientCache` via reflection, seed a substituted `IChatClient`, and assert `InvalidateCache` disposes and removes it. Write `TelegramGroupsAdmin.UnitTests/AI/ChatServiceCacheTests.cs`:

```csharp
using System.Collections;
using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.UnitTests.AI;

[TestFixture]
public class ChatServiceCacheTests
{
    private static IDictionary GetCache()
    {
        var field = typeof(ChatService).GetField("ClientCache",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IDictionary)field.GetValue(null)!;
    }

    private static object NewCachedClient(IChatClient client)
    {
        // CachedClient is a private nested record: CachedClient(IChatClient Client, string ModelId)
        var type = typeof(ChatService).GetNestedType("CachedClient", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, client, "test-model")!;
    }

    private static ChatService CreateService() => new(
        Substitute.For<ISystemConfigRepository>(),
        NullLogger<ChatService>.Instance,
        new ApiMetrics(),
        new CacheMetrics());

    [TearDown]
    public void ClearStaticCache()
    {
        // ClientCache is static — clear it between tests to prevent cross-test pollution.
        var cache = GetCache();
        cache.Clear();
    }

    [Test]
    public void InvalidateCache_DisposesEvictedClient()
    {
        var fakeClient = Substitute.For<IChatClient>();
        var cache = GetCache();
        var key = "dispose-test|OpenAI|gpt-4o||||";
        cache[key] = NewCachedClient(fakeClient);

        CreateService().InvalidateCache("dispose-test");

        fakeClient.Received(1).Dispose();
        Assert.That(cache.Contains(key), Is.False);
    }
}
```

`ApiMetrics`/`CacheMetrics` are concrete `sealed` classes with parameterless constructors, so `new ApiMetrics()` / `new CacheMetrics()` work directly. The unique connection-id prefix (`dispose-test`) prevents cross-test bleed on the static cache.

- [ ] **Step 4: Run the disposal test**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~ChatServiceCacheTests`
Expected: PASS (`fakeClient.Received(1).Dispose()` confirms eviction disposes). If the private field/record name differs after the rewrite, adjust the reflection names to match `ChatService.cs`.

> **Resolved (code review):** the disposal test asserts `InvalidateCache` calls `Dispose()` on the evicted client. Package-internals analysis showed MEAI's OpenAI/Azure `IChatClient.Dispose()` is currently a **no-op** (no resource/socket leak), so the eviction `Dispose()` is defensive forward-compat (e.g. the Anthropic client in commit 4 may hold real resources). The clients use the OpenAI SDK's **default shared transport** (`HttpClientPipelineTransport.Shared`) — no `IHttpClientFactory` and no per-client cached `HttpClient` (which would have been the "don't cache factory clients" antipattern). `CachedClient` stays `(IChatClient Client, string ModelId)`.

- [ ] **Step 5: Test-leak audit (dispatch agent)**

Dispatch a subagent to scan the test projects for any leaked SK type and confirm no further mechanical changes are needed:

> Prompt: "Search `TelegramGroupsAdmin.UnitTests`, `TelegramGroupsAdmin.ComponentTests`, and `TelegramGroupsAdmin.IntegrationTests` for any reference to Semantic Kernel types in test setup or mocks: `ChatHistory`, `Kernel`, `OpenAIPromptExecutionSettings`, `IChatCompletionService`, `Microsoft.SemanticKernel`. Also flag any remaining `double` temperature literals assigned to the now-`float` `Temperature` property, and any remaining `AzureApiVersion` references. Report file:line for each finding, or confirm none. Do not edit."

Expected: no SK-type references; any flagged temperature/azure literals get the mechanical fix.

### Task 2.10: Commit

- [ ] **Step 1: Stage and commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(ai): replace Semantic Kernel with Microsoft.Extensions.AI

Rewrite the chat implementation behind the unchanged IChatService against
MEAI's IChatClient. SemanticKernelChatService -> ChatService; static kernel
cache -> static IChatClient cache with disposal on eviction. Clients use the
OpenAI SDK's default shared transport (HttpClientPipelineTransport.Shared) for
handler pooling. Azure api-version is no longer pinned (the 2.x SDK floats the
service version) - AzureApiVersion removed everywhere.

Carries two type changes made in the same files (neither a logic change):
- Temperature double -> float end-to-end; default 0.2 -> 1.0 (1.0 is the only
  value safe across reasoning models and Claude extended thinking).
- Token counts int? -> long? to match UsageDetails (implicit widening).

Cache metrics tag and gauge renamed kernel -> chat_client. Manual ApiMetrics
instrumentation retained (MEAI OpenTelemetry middleware loses feature
attribution).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Commit 3 — Rename `LocalOpenAI` → `OpenAICompatible`, add OpenRouter

### Task 3.1: Enum + data-doc

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/AIProviderType.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs:5,15`

- [ ] **Step 1: Update the enum with explicit values**

Replace the body of `AIProviderType.cs` enum with:

```csharp
public enum AIProviderType
{
    /// <summary>
    /// OpenAI API (api.openai.com)
    /// </summary>
    OpenAI = 0,

    /// <summary>
    /// Azure OpenAI Service (custom endpoint + deployment)
    /// </summary>
    AzureOpenAI = 1,

    /// <summary>
    /// OpenAI-compatible endpoints (Ollama, LM Studio, vLLM, …)
    /// </summary>
    OpenAICompatible = 2,

    /// <summary>
    /// OpenRouter aggregator (https://openrouter.ai/api/v1)
    /// </summary>
    OpenRouter = 3
}
```

> Value 2 is preserved across the `LocalOpenAI`→`OpenAICompatible` rename → no data migration. Never renumber.

- [ ] **Step 2: Update the DTO doc comment**

In `AIConnectionData.cs`, update the class doc comment and the `Provider` field comment to read `0=OpenAI, 1=AzureOpenAI, 2=OpenAICompatible, 3=OpenRouter`.

- [ ] **Step 3: Let the compiler find every `LocalOpenAI` reference**

Run: `dotnet build 2>&1 | grep -i "LocalOpenAI" || echo "none-in-build-output"`
Then run: `grep -rn "LocalOpenAI" --include=*.cs --include=*.razor . | grep -v obj/`
Expected references to update: `ChatService.cs` (BuildClient + two API-key guards), `AIServiceFactory.cs`, `AIConnectionCard.razor`, `AddAIConnectionDialog.razor`, `AIFeatureCard.razor`, and the test files. Replace each `AIProviderType.LocalOpenAI` with `AIProviderType.OpenAICompatible`.

### Task 3.2: Client construction (OpenAICompatible + OpenRouter)

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/ChatService.cs` (`BuildClient`, API-key guards)

- [ ] **Step 1: Rename the LocalOpenAI branch and add OpenRouter**

In `BuildClient`, change the `case AIProviderType.LocalOpenAI:` to `case AIProviderType.OpenAICompatible:` (body unchanged), and add an OpenRouter branch before `default:`:

```csharp
            case AIProviderType.OpenRouter:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OpenRouter API key is required");

                var openRouterEndpoint = string.IsNullOrWhiteSpace(connection.LocalEndpoint)
                    ? "https://openrouter.ai/api/v1"
                    : connection.LocalEndpoint;

                return new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) })
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();
```

- [ ] **Step 2: Update the two API-key guards**

In `IsFeatureAvailableAsync` and `GetOrCreateClientAsync`/`GetOrCreateTestClientAsync`, the guard `connection.Provider != AIProviderType.LocalOpenAI` becomes `connection.Provider != AIProviderType.OpenAICompatible` (only the renamed value is keyless; OpenAI/Azure/OpenRouter all require keys). After Step 3.1 the compiler already forces these renames — confirm the semantics: OpenRouter must require a key, which it does because it is not the `OpenAICompatible` value.

### Task 3.3: Model discovery (OpenRouter)

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/AIServiceFactory.cs:104-148`

- [ ] **Step 1: Keep Azure skip; map endpoints for the rest**

In `FetchModelsAsync`, replace the endpoint selection so OpenRouter routes to its base URL while OpenAICompatible keeps its configured endpoint:

```csharp
        // Determine endpoint based on provider
        var endpoint = connection.Provider switch
        {
            AIProviderType.OpenAI => "https://api.openai.com",
            AIProviderType.OpenRouter => string.IsNullOrWhiteSpace(connection.LocalEndpoint)
                ? "https://openrouter.ai/api/v1"
                : connection.LocalEndpoint,
            _ => connection.LocalEndpoint!
        };

        return await FetchOpenAICompatibleModelsAsync(endpoint, apiKey, cancellationToken);
```

> OpenRouter exposes the standard `/v1/models` shape, so the existing `FetchOpenAICompatibleModelsAsync` handles it. Richer OpenRouter metadata (pricing, context length) is out of scope.

### Task 3.4: UI (rename + OpenRouter item)

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/AddAIConnectionDialog.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/AIConnectionCard.razor`

- [ ] **Step 1: AddAIConnectionDialog — rename item, add OpenRouter**

Replace the `LocalOpenAI` `MudSelectItem`:

```razor
                <MudSelectItem Value="AIProviderType.LocalOpenAI">
                    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                        <MudIcon Icon="@Icons.Material.Filled.Computer" Size="Size.Small" />
                        <span>Local / OpenAI-Compatible</span>
                    </MudStack>
                </MudSelectItem>
```

with:

```razor
                <MudSelectItem Value="AIProviderType.OpenAICompatible">
                    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                        <MudIcon Icon="@Icons.Material.Filled.Computer" Size="Size.Small" />
                        <span>OpenAI-Compatible (Ollama, LM Studio, vLLM, …)</span>
                    </MudStack>
                </MudSelectItem>
                <MudSelectItem Value="AIProviderType.OpenRouter">
                    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                        <MudIcon Icon="@Icons.Material.Filled.Hub" Size="Size.Small" />
                        <span>OpenRouter</span>
                    </MudStack>
                </MudSelectItem>
```

Update the two helper switches:

```csharp
    private static string GetProviderHelperText(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => "Standard OpenAI API - requires API key from platform.openai.com",
        AIProviderType.AzureOpenAI => "Azure OpenAI Service - requires endpoint URL and API key from Azure Portal",
        AIProviderType.OpenAICompatible => "Ollama, LM Studio, vLLM, or other OpenAI-compatible APIs",
        AIProviderType.OpenRouter => "OpenRouter aggregator - requires an API key from openrouter.ai",
        _ => ""
    };

    private static string GetIdPlaceholder(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => "openai-main",
        AIProviderType.AzureOpenAI => "azure-prod",
        AIProviderType.OpenAICompatible => "local-ollama",
        AIProviderType.OpenRouter => "openrouter-main",
        _ => "my-connection"
    };
```

- [ ] **Step 2: AIConnectionCard — handle the renamed + new provider**

Replace the `else if (Connection.Provider == AIProviderType.LocalOpenAI)` block condition with `AIProviderType.OpenAICompatible`. For OpenRouter (key required, no endpoint field needed since it defaults), add an endpoint field only if you want to allow overriding — to keep scope minimal, OpenRouter shows just the API key field (no extra block needed; it falls through to the shared API-key field). Update the three provider switches (`GetProviderDisplayName`, `GetProviderIcon`, `GetApiKeyPlaceholder`, `GetApiKeyHelperText`) and the `isReady`/disabled logic that referenced `LocalOpenAI`:

```csharp
    private static string GetProviderDisplayName(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => "OpenAI (api.openai.com)",
        AIProviderType.AzureOpenAI => "Azure OpenAI Service",
        AIProviderType.OpenAICompatible => "OpenAI-compatible",
        AIProviderType.OpenRouter => "OpenRouter",
        _ => provider.ToString()
    };

    private static string GetProviderIcon(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => Icons.Material.Filled.Cloud,
        AIProviderType.AzureOpenAI => Icons.Material.Filled.CloudQueue,
        AIProviderType.OpenAICompatible => Icons.Material.Filled.Computer,
        AIProviderType.OpenRouter => Icons.Material.Filled.Hub,
        _ => Icons.Material.Filled.Psychology
    };

    private static string GetApiKeyPlaceholder(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => "sk-...",
        AIProviderType.AzureOpenAI => "Azure API key",
        AIProviderType.OpenAICompatible => "Optional API key",
        AIProviderType.OpenRouter => "sk-or-...",
        _ => "API key"
    };

    private static string GetApiKeyHelperText(AIProviderType provider) => provider switch
    {
        AIProviderType.OpenAI => "Get your API key from https://platform.openai.com/api-keys",
        AIProviderType.AzureOpenAI => "Azure OpenAI Service API key from Azure Portal",
        AIProviderType.OpenAICompatible => "Leave empty if your provider doesn't require authentication",
        AIProviderType.OpenRouter => "Get your API key from https://openrouter.ai/keys",
        _ => ""
    };
```

Update the API-key disabled binding and `isReady` computation: replace each `Connection.Provider == AIProviderType.LocalOpenAI` with `AIProviderType.OpenAICompatible` (OpenRouter is key-required, so it must NOT be in the keyless branch).

### Task 3.5: Build, test, commit

- [ ] **Step 1: Build + targeted tests**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AIProvider` and `dotnet test TelegramGroupsAdmin.ComponentTests --filter FullyQualifiedName~AIConnectionCard`
Expected: green. Update any test asserting the old `LocalOpenAI` display string ("Local") to the new "OpenAI-compatible" text — `AIConnectionCardTests.DisplaysProviderName` uses `[TestCase(AIProviderType.LocalOpenAI, "Local")]`; change to `[TestCase(AIProviderType.OpenAICompatible, "OpenAI-compatible")]` and add `[TestCase(AIProviderType.OpenRouter, "OpenRouter")]`.

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(ai): rename LocalOpenAI provider to OpenAICompatible and add OpenRouter

Rename the enum member (value 2 preserved -> no data migration) and add
OpenRouter (=3) as an OpenAI-compatible provider defaulting to
https://openrouter.ai/api/v1, with API-key required and /v1/models discovery.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Commit 4 — Anthropic (Claude), direct

### Task 4.1: Package

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`

- [ ] **Step 1: Add the official Anthropic SDK version**

In `Directory.Packages.props`, under the AI/ML group, add:

```xml
    <PackageVersion Include="Anthropic" Version="12.24.1" />
```

- [ ] **Step 2: Reference it from the AI project**

In `TelegramGroupsAdmin.AI.csproj`, add:

```xml
    <PackageReference Include="Anthropic" />
```

> Use the **official `Anthropic`** package (v10+, currently 12.24.1) — not `Anthropic.SDK` (tghamm) or `tryAGI.Anthropic`. It is beta; the exact version is pinned.

### Task 4.2: Enum

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/AIProviderType.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/AIConnectionData.cs`

- [ ] **Step 1: Append Anthropic = 4**

Add to the enum:

```csharp
    /// <summary>
    /// Anthropic (Claude), direct (api.anthropic.com)
    /// </summary>
    Anthropic = 4
```

Update the `AIConnectionData` provider doc comment to include `4=Anthropic`.

### Task 4.3: Client construction

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/ChatService.cs` (`BuildClient`)

- [ ] **Step 1: Add the Anthropic branch**

Add `using Anthropic;` (and `using Anthropic.Models.Messages;` only if needed by the API) to `ChatService.cs`, then add before `default:` in `BuildClient`:

```csharp
            case AIProviderType.Anthropic:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Anthropic API key is required");

                return new AnthropicClient { ApiKey = apiKey }.AsIChatClient(featureConfig.Model);
```

> The official SDK's `AsIChatClient(modelId)` returns an `IChatClient` bound to the model at construction — uniform with the OpenAI family, no per-request `ChatOptions.ModelId`. Vision flows through the existing `DataContent` path. Prompt caching is out of scope (issue #481).

- [ ] **Step 2: Verify the exact construction symbol**

This is the one genuine beta-surface touch. Before relying on it, confirm the namespace + `AsIChatClient` overload against the installed package:

Run: `dotnet build TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`
Expected: Build succeeded. If `AnthropicClient` or `.AsIChatClient(string)` does not resolve, pause and check the package's API surface (the official SDK README / `Anthropic` namespace) rather than guessing — the rest of the migration is insulated from this by the `IChatService` boundary.

### Task 4.4: Model discovery (raw REST)

**Files:**
- Modify: `TelegramGroupsAdmin.AI/Services/AIServiceFactory.cs`

- [ ] **Step 1: Branch Anthropic in RefreshModelsAsync's fetch**

Anthropic is not OpenAI-compatible for model listing. In `FetchModelsAsync`, branch before the OpenAI-compatible call:

```csharp
        if (connection.Provider == AIProviderType.Anthropic)
        {
            return await FetchAnthropicModelsAsync(apiKey, cancellationToken);
        }

        // Determine endpoint based on provider
        var endpoint = connection.Provider switch
        {
            AIProviderType.OpenAI => "https://api.openai.com",
            AIProviderType.OpenRouter => string.IsNullOrWhiteSpace(connection.LocalEndpoint)
                ? "https://openrouter.ai/api/v1"
                : connection.LocalEndpoint,
            _ => connection.LocalEndpoint!
        };

        return await FetchOpenAICompatibleModelsAsync(endpoint, apiKey, cancellationToken);
```

- [ ] **Step 2: Add the Anthropic fetch method + response records**

Add to `AIServiceFactory.cs`:

```csharp
    /// <summary>
    /// Fetch models from the Anthropic Messages API (/v1/models).
    /// Uses raw REST (x-api-key + anthropic-version headers) to keep model
    /// discovery off the SDK's beta surface, consistent with every other provider.
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchAnthropicModelsAsync(
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Cannot fetch Anthropic models: API key not configured");
            return [];
        }

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        try
        {
            var response = await client.GetAsync("https://api.anthropic.com/v1/models", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Anthropic models: {StatusCode}", response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<AnthropicModelsResponse>(content, JsonOptions);

            return modelsResponse?.Data?
                .Select(m => new AIModelInfo { Id = m.Id })
                .OrderBy(m => m.Id)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Anthropic models");
            return [];
        }
    }
```

And add to the response-records block at the bottom:

```csharp
    private record AnthropicModelsResponse(AnthropicModelData[]? Data);
    private record AnthropicModelData(string Id);
```

> `anthropic-version: 2023-06-01` is the stable Messages-API version header. The `{ data: [{ id, display_name, created_at }] }` shape maps `id` → `AIModelInfo.Id`. Only `id` is captured — the API's `display_name` is intentionally not modelled (friendlier model names are tracked provider-agnostically as model-list collapsing in #499; capturing an Anthropic-only field here would be dead code).

### Task 4.5: UI (Anthropic item)

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/AddAIConnectionDialog.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/AIConnectionCard.razor`

- [ ] **Step 1: AddAIConnectionDialog — add the item + helpers**

Add a `MudSelectItem`:

```razor
                <MudSelectItem Value="AIProviderType.Anthropic">
                    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                        <MudIcon Icon="@Icons.Material.Filled.Psychology" Size="Size.Small" />
                        <span>Anthropic (Claude)</span>
                    </MudStack>
                </MudSelectItem>
```

Add `AIProviderType.Anthropic` cases to `GetProviderHelperText` ("Anthropic Claude - requires an API key from console.anthropic.com") and `GetIdPlaceholder` ("anthropic-main").

- [ ] **Step 2: AIConnectionCard — add Anthropic cases**

Add `AIProviderType.Anthropic` cases to `GetProviderDisplayName` ("Anthropic (Claude)"), `GetProviderIcon` (`Icons.Material.Filled.Psychology`), `GetApiKeyPlaceholder` ("sk-ant-..."), `GetApiKeyHelperText` ("Get your API key from https://console.anthropic.com/settings/keys"). Anthropic requires a key and needs no provider-specific endpoint field (falls through to the shared API-key field).

### Task 4.6: Build, test, commit

- [ ] **Step 1: Build + tests**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AIProvider` and `dotnet test TelegramGroupsAdmin.ComponentTests --filter FullyQualifiedName~AIConnectionCard`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(ai): add Anthropic (Claude) provider

Add Anthropic (=4) via the official Anthropic SDK
(new AnthropicClient { ApiKey = key }.AsIChatClient(model)) - an IChatClient
bound to the model, uniform with the OpenAI family. Model discovery uses the
raw /v1/models REST call (x-api-key + anthropic-version) to keep discovery off
the SDK's beta surface, consistent with every other provider. Vision flows
through the shared DataContent path; prompt caching is out of scope (#481).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Final verification (before opening the PR)

- [ ] `dotnet build` clean (warnings-as-errors) at HEAD.
- [ ] Full AI suites green: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AI` and the ComponentTests AI filters.
- [ ] Integration: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~AIProviderConfigIntegrationTests` (background — suite is slow; see context-keep "Run tests in background").
- [ ] Manual smoke via Settings UI "Test" flow against a real OpenRouter key and a real Anthropic key: add connection → refresh models → run a completion → run a vision call where supported. (Cannot be automated — requires live keys.)
- [ ] Exactly one EF migration exists (`RemapAIFeatureConfigKeysToInt`, commit 1 — converts stored AIFeatureType keys names→ints). The `AIProviderType` rename in commit 3 needs no migration (value 2 preserved). The temperature/token type changes need no migration (JSON number format unchanged).
- [ ] Open PR to `develop` with `Closes #481`? No — #481 is prompt caching, out of scope. File a tracking issue for the AI config-mapping gap referencing #453, and link it in the PR body. PR body lists the four commits and notes the discovery.

## Follow-on issues to file (not in this PR)
- AI config-mapping gap completion — reference #453 as "the config it missed" (this PR closes it; file for traceability or note in the PR that it extends #453).
- Provider-aware temperature min/max in the UI — already tracked as #480.
- Anthropic prompt caching — already tracked as #481.
