# Config Layer Restoration & AI Relocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore project-layer separation by moving `ConfigService` and AI services out of `Core` into their proper homes (`Configuration` and a new `AI` project), expand `IConfigRepository` to typed methods that own JSON/mapping/encryption end-to-end, wire the dead mapping layer for all 8 configs, move bot-token encryption from service into repository, and emit audit events on every save/delete.

**Architecture:** Final dependency graph is bottom-up: `Data → Core → Configuration → AI → ContentDetection → BackgroundJobs → Telegram → Host`. The `Core → Configuration` reference (the inversion that forced `ConfigService` into `Core`) is removed. Services own no data-layer concerns (no EF Core, no Data Protection, no DTOs, no JSON); repositories own all of it. `ConfigType` enum and generic `GetAsync<T>(ConfigType, ...)` retire in favor of typed methods like `GetWelcomeAsync(long chatId)`.

**Tech Stack:** .NET 10, EF Core 10 (PostgreSQL), Microsoft.Extensions.Caching.Hybrid, Microsoft.AspNetCore.DataProtection, Microsoft.SemanticKernel, NUnit + NSubstitute, TestContainers (PostgreSQL 18).

**Spec:** `docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md` (commit `62e7db38`).

**Branch:** `refactor/restore-core-relocate-config-and-ai`. Single PR, 7 commits. Intermediate commits may not build (the user's "intermediate-broken-OK" rule applies to this branch); final state must be green.

---

## File Structure

### New project

- `TelegramGroupsAdmin.AI/` — new csproj at peer level with `ContentDetection`/`BackgroundJobs`. References `Configuration`, `Core`, `Data`. Owns `Microsoft.SemanticKernel` package reference (moved from `Core`).
- `TelegramGroupsAdmin.AI/Services/` — destination folder for all 7 AI service files + 6 value-type companions relocated from `Core/Services/AI/`.
- `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs` — new, owns `AddAIServices()` registration extension.
- `TelegramGroupsAdmin.AI/CLAUDE.md` — short reference describing the project's role.

### Configuration project additions

- `TelegramGroupsAdmin.Configuration/Models/BotProtectionConfig.cs` — moved from `Telegram/Models/BotProtectionConfig.cs`, namespace flipped to `TelegramGroupsAdmin.Configuration.Models`.
- `TelegramGroupsAdmin.Configuration/Models/InviteCommandConfig.cs` — moved from `Telegram/Models/InviteCommandConfig.cs`, namespace flipped to `TelegramGroupsAdmin.Configuration.Models`.
- `TelegramGroupsAdmin.Configuration/Services/IConfigService.cs` — moved from `Core/Services/IConfigService.cs`, rewritten to typed surface.
- `TelegramGroupsAdmin.Configuration/Services/ConfigService.cs` — moved from `Core/Services/ConfigService.cs`, rewritten to typed surface that delegates to typed repo + audit.
- `TelegramGroupsAdmin.Configuration/Mappings/LogConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/BotProtectionConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/TelegramBotConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/ServiceMessageDeletionConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/WarningSystemConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/InviteCommandConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/BanCelebrationConfigMappings.cs` — new.
- `TelegramGroupsAdmin.Configuration/Mappings/ModerationConfigMappings.cs` — new (wraps Warning + Invite into the multiplexed JSON column).
- `TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj` — adds `Microsoft.AspNetCore.DataProtection` and `Microsoft.Extensions.Caching.Hybrid` (moved from `Core`); adds project ref to `TelegramGroupsAdmin.Core` (the inversion fix); removes `InternalsVisibleTo TelegramGroupsAdmin.UnitTests` if no longer needed and instead adds `InternalsVisibleTo TelegramGroupsAdmin.IntegrationTests` if integration tests need access (they shouldn't — public API only).
- `TelegramGroupsAdmin.Configuration/ConfigurationExtensions.cs` — register `IConfigService → ConfigService` (moved from Core's `ServiceCollectionExtensions`).

### Configuration project modifications

- `TelegramGroupsAdmin.Configuration/Repositories/IConfigRepository.cs` — gains 8× `GetXxxAsync` + 8× `GetEffectiveXxxAsync` + 8× `SaveXxxAsync` + 8× `DeleteXxxAsync` typed methods + `GetBotTokenAsync` + `SaveBotTokenAsync`. Old `GetAsync(long)` / `UpsertAsync(ConfigRecordDto)` / `DeleteAsync(long)` removed in commit 7.
- `TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs` — adds typed methods (Save/Get/GetEffective/Delete per config + bot token) with internal mapping → JSON serialization → upsert; per-config typed `Merge*` private methods replace the reflection-based `MergeConfigs<T>` from `ConfigService`. Constructor gains `IDataProtectionProvider` + `ILogger<ConfigRepository>`.
- `TelegramGroupsAdmin.Configuration/ConfigType.cs` — DELETED in commit 7.
- `TelegramGroupsAdmin.Configuration/Models/ConfigRecord.cs` — DELETED in commit 7 (unused).

### Data project additions

- `TelegramGroupsAdmin.Data/Models/Configs/WarningSystemConfigData.cs` — new.
- `TelegramGroupsAdmin.Data/Models/Configs/InviteCommandConfigData.cs` — new.
- `TelegramGroupsAdmin.Data/Models/Configs/BanCelebrationConfigData.cs` — new.
- `TelegramGroupsAdmin.Data/Models/Configs/ModerationConfigData.cs` — new wrapper DTO (`{ WarningSystem: WarningSystemConfigData, InviteCommand: InviteCommandConfigData }`).

### Core project changes

- `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj` — removes project ref to `Configuration` (the inversion fix); removes packages `Microsoft.SemanticKernel`, `Microsoft.Extensions.Caching.Hybrid`, `Microsoft.AspNetCore.DataProtection` (moved to AI / Configuration).
- `TelegramGroupsAdmin.Core/Services/ConfigService.cs` — moved out (commit 5).
- `TelegramGroupsAdmin.Core/Services/IConfigService.cs` — moved out (commit 5).
- `TelegramGroupsAdmin.Core/Services/AI/*` — all 7 service files + 6 value types moved out (commit 2).
- `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs` — drops `IConfigService` registration (moves to Configuration), drops AI registrations (move to AI).

### Telegram project changes

- `TelegramGroupsAdmin.Telegram/Models/BotProtectionConfig.cs` — DELETED (moved to Configuration in commit 3).
- `TelegramGroupsAdmin.Telegram/Models/InviteCommandConfig.cs` — DELETED (moved to Configuration in commit 3).
- `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj` — adds project ref to new `TelegramGroupsAdmin.AI` (so existing AI consumers in Telegram keep compiling).

### Test additions

#### Unit tests (`TelegramGroupsAdmin.UnitTests/Configuration/`)

- `LogConfigMappingsTests.cs` — round-trip.
- `BotProtectionConfigMappingsTests.cs` — round-trip.
- `TelegramBotConfigMappingsTests.cs` — round-trip.
- `ServiceMessageDeletionConfigMappingsTests.cs` — round-trip.
- `WarningSystemConfigMappingsTests.cs` — round-trip.
- `InviteCommandConfigMappingsTests.cs` — round-trip.
- `BanCelebrationConfigMappingsTests.cs` — round-trip.
- `ModerationConfigMappingsTests.cs` — round-trip (wrapper).
- `ConfigRepositoryMergeTests.cs` — per-config merge unit tests (5 cases × 8 configs).
- `ConfigServiceTests.cs` — typed-method tests with mocked `IConfigRepository` + `IAuditService` + `HybridCache`.

#### Integration tests (`TelegramGroupsAdmin.IntegrationTests/Configuration/`)

- `ConfigRepositoryIntegrationTests.cs` — save/get round-trip + GetEffective merge for all 8 configs + bot-token encryption assertion.
- `ConfigServiceIntegrationTests.cs` — full save path → assert `audit_logs` row count grows.

#### Component test mocks (`TelegramGroupsAdmin.ComponentTests/Components/`)

- `WelcomeSystemConfigTests.cs` — mock setup updated.
- `BotGeneralSettingsTests.cs` — mock setup updated.
- `BanCelebrationSettingsTests.cs` — mock setup updated.
- `BanCelebrationChatSettingsTests.cs` — mock setup updated.
- `ServiceMessageDeletionSettingsTests.cs` — mock setup updated.
- `ExamReviewCardTests.cs` — mock setup updated.

### Solution file

- `TelegramGroupsAdmin.sln` — register the new `TelegramGroupsAdmin.AI` project with a fresh GUID and platform configurations matching the existing pattern.

---

## Mapping Convention Reference

This codebase uses C# 12 `extension(...)` syntax for mapping methods (see `WelcomeConfigMappings.cs:16-66` for the established pattern). Every new `*ConfigMappings` file MUST use this syntax, NOT classic static extension methods. Pattern:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class XxxConfigMappings
{
    extension(XxxConfigData data)
    {
        public XxxConfig ToModel() => new()
        {
            // map every property
        };
    }

    extension(XxxConfig model)
    {
        public XxxConfigData ToData() => new()
        {
            // map every property (inverse)
        };
    }
}
```

---

## Per-Config Inventory (Reference)

The 8 configs flowing through `ConfigService` (excluding `ContentDetection` which has its own typed path):

| Config | Model namespace | DTO column | Multiplexed? |
|---|---|---|---|
| `WelcomeConfig` | `TelegramGroupsAdmin.Configuration.Models.Welcome` | `welcome_config` | No |
| `LogConfig` | `TelegramGroupsAdmin.Configuration.Models` | `log_config` | No |
| `BotProtectionConfig` | `TelegramGroupsAdmin.Configuration.Models` (after move) | `bot_protection_config` | No |
| `TelegramBotConfig` | `TelegramGroupsAdmin.Configuration.Models` | `telegram_bot_config` | No |
| `ServiceMessageDeletionConfig` | `TelegramGroupsAdmin.Configuration` (root) | `service_message_deletion_config` | No |
| `WarningSystemConfig` | `TelegramGroupsAdmin.Configuration` (root) | `moderation_config` | YES (with InviteCommand) |
| `InviteCommandConfig` | `TelegramGroupsAdmin.Configuration.Models` (after move) | `moderation_config` | YES (with WarningSystem) |
| `BanCelebrationConfig` | `TelegramGroupsAdmin.Configuration` (root) | `ban_celebration_config` | No |

Plus: bot token in `telegram_bot_token_encrypted` column (encrypted, no chat scope).

---

## Task 1: Scaffold the new TelegramGroupsAdmin.AI project

**Commit:** `chore(ai): scaffold TelegramGroupsAdmin.AI project`

**Files:**
- Create: `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`
- Create: `TelegramGroupsAdmin.AI/CLAUDE.md`
- Modify: `TelegramGroupsAdmin.sln`

- [ ] **Step 1: Create the AI project directory and csproj**

Create `TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Suppress Semantic Kernel experimental API warnings for custom endpoint support -->
    <NoWarn>SKEXP0010</NoWarn>
    <SuppressNETCoreSdkPreviewMessage>true</SuppressNETCoreSdkPreviewMessage>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.SemanticKernel" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TelegramGroupsAdmin.Configuration\TelegramGroupsAdmin.Configuration.csproj" />
    <ProjectReference Include="..\TelegramGroupsAdmin.Core\TelegramGroupsAdmin.Core.csproj" />
    <ProjectReference Include="..\TelegramGroupsAdmin.Data\TelegramGroupsAdmin.Data.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the AI project CLAUDE.md**

Create `TelegramGroupsAdmin.AI/CLAUDE.md`:

```markdown
# TelegramGroupsAdmin.AI - AI Reference

## Project Role

Owns AI service abstractions and implementations: chat completion via Semantic Kernel, AI-based translation, AI feature factories, and AI feature test runners.

## Dependencies

References: `Configuration` (for `AIProviderConfig` and friends), `Core` (for metrics + `IAuditService`), `Data` (for shared primitives).
Consumed by: `Telegram`, `Host` (`TelegramGroupsAdmin`).

## Design Rules

- All AI services are `Scoped` (matching the lifetime of `ISystemConfigRepository`, which they consume).
- The Semantic Kernel kernel cache in `SemanticKernelChatService` is `static` and persists across scoped instances — do not turn it into instance state.
- This project owns the `Microsoft.SemanticKernel` package reference. Do not add it to `Core` or anywhere else.
```

- [ ] **Step 3: Register the new project in the solution file**

Find an existing project line in `TelegramGroupsAdmin.sln` (e.g., `TelegramGroupsAdmin.ContentDetection`) and add a sibling block. Generate a fresh GUID (e.g., `D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB`) and add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TelegramGroupsAdmin.AI", "TelegramGroupsAdmin.AI\TelegramGroupsAdmin.AI.csproj", "{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}"
EndProject
```

Then add 12 platform configuration lines under `GlobalSection(ProjectConfigurationPlatforms) = postSolution`:

```
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|Any CPU.Build.0 = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|x64.ActiveCfg = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|x64.Build.0 = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|x86.ActiveCfg = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Debug|x86.Build.0 = Debug|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|Any CPU.ActiveCfg = Release|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|Any CPU.Build.0 = Release|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|x64.ActiveCfg = Release|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|x64.Build.0 = Release|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|x86.ActiveCfg = Release|Any CPU
{D9F1A6B2-4E7C-4B5A-8C9E-1234567890AB}.Release|x86.Build.0 = Release|Any CPU
```

- [ ] **Step 4: Verify the new project builds standalone**

Run: `dotnet build TelegramGroupsAdmin.AI/TelegramGroupsAdmin.AI.csproj`
Expected: build succeeds, zero warnings.

- [ ] **Step 5: Verify the solution still builds**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: build succeeds (the new project is empty and inert).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.AI/ TelegramGroupsAdmin.sln
git commit -F- <<'EOF'
chore(ai): scaffold TelegramGroupsAdmin.AI project

New peer-level project at the same layer as ContentDetection and
BackgroundJobs. Owns AI service abstractions and Semantic Kernel
package reference. Empty until services are relocated in the next
commit.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 2: Relocate AI services from Core to AI

**Commit:** `refactor(ai): relocate AI services from Core to AI project`

This commit moves 13 files (7 services + 6 value types) and flips namespaces. Build will be **broken** at the end of this commit because consumers in Telegram still import `TelegramGroupsAdmin.Core.Services.AI`. That's expected per the spec's "intermediate-broken-OK" rule.

**Files:**
- Move (13 files): `TelegramGroupsAdmin.Core/Services/AI/*.cs` → `TelegramGroupsAdmin.AI/Services/*.cs`
- Modify: `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj` (remove `Microsoft.SemanticKernel` package)
- Modify: `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs` (drop AI service registrations)
- Create: `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj` (add ref to AI project so the broken-build is at namespace level only, not project-ref level)

- [ ] **Step 1: Move all 13 AI files via git mv**

Run, in order:

```bash
mkdir -p TelegramGroupsAdmin.AI/Services
git mv TelegramGroupsAdmin.Core/Services/AI/AIFeatureStatus.cs           TelegramGroupsAdmin.AI/Services/AIFeatureStatus.cs
git mv TelegramGroupsAdmin.Core/Services/AI/AIServiceFactory.cs          TelegramGroupsAdmin.AI/Services/AIServiceFactory.cs
git mv TelegramGroupsAdmin.Core/Services/AI/AITranslationService.cs      TelegramGroupsAdmin.AI/Services/AITranslationService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/ChatCompletionOptions.cs     TelegramGroupsAdmin.AI/Services/ChatCompletionOptions.cs
git mv TelegramGroupsAdmin.Core/Services/AI/ChatCompletionResult.cs      TelegramGroupsAdmin.AI/Services/ChatCompletionResult.cs
git mv TelegramGroupsAdmin.Core/Services/AI/FeatureTestResult.cs         TelegramGroupsAdmin.AI/Services/FeatureTestResult.cs
git mv TelegramGroupsAdmin.Core/Services/AI/FeatureTestService.cs        TelegramGroupsAdmin.AI/Services/FeatureTestService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/IAIServiceFactory.cs         TelegramGroupsAdmin.AI/Services/IAIServiceFactory.cs
git mv TelegramGroupsAdmin.Core/Services/AI/IAITranslationService.cs     TelegramGroupsAdmin.AI/Services/IAITranslationService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/IChatService.cs              TelegramGroupsAdmin.AI/Services/IChatService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/IFeatureTestService.cs       TelegramGroupsAdmin.AI/Services/IFeatureTestService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/ImageInput.cs                TelegramGroupsAdmin.AI/Services/ImageInput.cs
git mv TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs TelegramGroupsAdmin.AI/Services/SemanticKernelChatService.cs
git mv TelegramGroupsAdmin.Core/Services/AI/TranslationResult.cs         TelegramGroupsAdmin.AI/Services/TranslationResult.cs
rmdir TelegramGroupsAdmin.Core/Services/AI
```

- [ ] **Step 2: Flip namespaces on all 13 moved files**

In every moved file, replace `namespace TelegramGroupsAdmin.Core.Services.AI;` with `namespace TelegramGroupsAdmin.AI.Services;`. Use Edit tool on each file individually — do not use sed.

For each of the 13 files: read → replace the `namespace` line → save.

- [ ] **Step 3: Remove SemanticKernel package from Core csproj**

Edit `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`. Remove the line `<PackageReference Include="Microsoft.SemanticKernel" />`. Also remove the `<NoWarn>SKEXP0010</NoWarn>` line (no longer needed in Core).

After removal, the Core csproj `<PropertyGroup>` should look like:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <SuppressNETCoreSdkPreviewMessage>true</SuppressNETCoreSdkPreviewMessage>
</PropertyGroup>
```

- [ ] **Step 4: Drop AI service registrations from Core's ServiceCollectionExtensions**

Edit `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`. Remove these lines:

```csharp
using TelegramGroupsAdmin.Core.Services.AI;
```

```csharp
// AI services (Semantic Kernel multi-provider support)
// IChatService is Scoped (matches ISystemConfigRepository), kernel cache is static
services.AddScoped<IChatService, SemanticKernelChatService>();
services.AddScoped<IAIServiceFactory, AIServiceFactory>();
services.AddScoped<IAITranslationService, AITranslationService>();
services.AddScoped<IFeatureTestService, FeatureTestService>();
```

- [ ] **Step 5: Create AI's ServiceCollectionExtensions with the relocated registrations**

Create `TelegramGroupsAdmin.AI/Extensions/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.AI.Services;

namespace TelegramGroupsAdmin.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIServices(this IServiceCollection services)
    {
        // AI services (Semantic Kernel multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository), kernel cache is static
        services.AddScoped<IChatService, SemanticKernelChatService>();
        services.AddScoped<IAIServiceFactory, AIServiceFactory>();
        services.AddScoped<IAITranslationService, AITranslationService>();
        services.AddScoped<IFeatureTestService, FeatureTestService>();
        return services;
    }
}
```

- [ ] **Step 6: Add AI project ref to Telegram csproj**

Edit `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj`. In the `<ItemGroup>` containing `<ProjectReference>` items, add:

```xml
<ProjectReference Include="..\TelegramGroupsAdmin.AI\TelegramGroupsAdmin.AI.csproj" />
```

This makes the AI assembly available to Telegram so consumer namespace updates in commit 6 only need `using` changes, not csproj changes.

- [ ] **Step 7: Confirm broken build is in expected state**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: build FAILS with "type or namespace name 'AI' does not exist in 'TelegramGroupsAdmin.Core.Services'" errors at consumer call sites (Telegram services that import `TelegramGroupsAdmin.Core.Services.AI`).

This is the expected intermediate-broken state per the spec's commit table. Do NOT fix the consumers in this commit — they are addressed in commit 6 along with the rest of the consumer sweep.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(ai): relocate AI services from Core to AI project

Move 7 AI service files + 6 value types from Core/Services/AI/ to
AI/Services/. Namespace flip from TelegramGroupsAdmin.Core.Services.AI
to TelegramGroupsAdmin.AI.Services. AI's ServiceCollectionExtensions
now owns the four AI registrations; Core's drops them. Microsoft.SemanticKernel
package moves from Core's csproj to AI's. Telegram csproj gains a
ProjectReference to AI.

Build is intentionally broken at this commit — consumer using-statement
updates land in commit 6 alongside the rest of the consumer sweep.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 3: Add missing DTOs, mappings, and move POCO models

**Commit:** `feat(config): add missing DTOs and mappings + unit tests`

This commit is **green-only additions** — it adds new files and moves two POCO models that have no Telegram dependencies. Existing call sites continue to work because old `ConfigService` API is untouched here.

**Files (creates):**
- `TelegramGroupsAdmin.Data/Models/Configs/WarningSystemConfigData.cs`
- `TelegramGroupsAdmin.Data/Models/Configs/InviteCommandConfigData.cs`
- `TelegramGroupsAdmin.Data/Models/Configs/BanCelebrationConfigData.cs`
- `TelegramGroupsAdmin.Data/Models/Configs/ModerationConfigData.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/LogConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/BotProtectionConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/TelegramBotConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/ServiceMessageDeletionConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/WarningSystemConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/InviteCommandConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/BanCelebrationConfigMappings.cs`
- `TelegramGroupsAdmin.Configuration/Mappings/ModerationConfigMappings.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/LogConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/BotProtectionConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/TelegramBotConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/ServiceMessageDeletionConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/WarningSystemConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/InviteCommandConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/BanCelebrationConfigMappingsTests.cs`
- `TelegramGroupsAdmin.UnitTests/Configuration/ModerationConfigMappingsTests.cs`

**Files (moves):**
- Move: `TelegramGroupsAdmin.Telegram/Models/BotProtectionConfig.cs` → `TelegramGroupsAdmin.Configuration/Models/BotProtectionConfig.cs`
- Move: `TelegramGroupsAdmin.Telegram/Models/InviteCommandConfig.cs` → `TelegramGroupsAdmin.Configuration/Models/InviteCommandConfig.cs`

### 3a — Move the two POCO models from Telegram to Configuration

- [ ] **Step 1: git mv the BotProtectionConfig and InviteCommandConfig files**

```bash
git mv TelegramGroupsAdmin.Telegram/Models/BotProtectionConfig.cs TelegramGroupsAdmin.Configuration/Models/BotProtectionConfig.cs
git mv TelegramGroupsAdmin.Telegram/Models/InviteCommandConfig.cs TelegramGroupsAdmin.Configuration/Models/InviteCommandConfig.cs
```

- [ ] **Step 2: Flip BotProtectionConfig namespace**

Edit `TelegramGroupsAdmin.Configuration/Models/BotProtectionConfig.cs`. Replace `namespace TelegramGroupsAdmin.Telegram.Models;` with `namespace TelegramGroupsAdmin.Configuration.Models;`.

- [ ] **Step 3: Flip InviteCommandConfig namespace**

Edit `TelegramGroupsAdmin.Configuration/Models/InviteCommandConfig.cs`. Replace `namespace TelegramGroupsAdmin.Telegram.Models;` with `namespace TelegramGroupsAdmin.Configuration.Models;`.

(Consumer using-statement updates for these moves land in commit 6.)

### 3b — Add the four new DTOs in Data project

- [ ] **Step 4: Create WarningSystemConfigData**

Create `TelegramGroupsAdmin.Data/Models/Configs/WarningSystemConfigData.cs`:

```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of WarningSystemConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// Multiplexed inside the moderation_config column via ModerationConfigData.
/// </summary>
public class WarningSystemConfigData
{
    public bool AutoBanEnabled { get; set; }
    public int AutoBanThreshold { get; set; }
    public string AutoBanReason { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Create InviteCommandConfigData**

Create `TelegramGroupsAdmin.Data/Models/Configs/InviteCommandConfigData.cs`:

```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of InviteCommandConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// Multiplexed inside the moderation_config column via ModerationConfigData.
/// </summary>
public class InviteCommandConfigData
{
    public bool Enabled { get; set; } = true;
    public bool DeleteCommandMessage { get; set; } = true;
    public int DeleteResponseAfterSeconds { get; set; } = 30;
}
```

- [ ] **Step 6: Create BanCelebrationConfigData**

Create `TelegramGroupsAdmin.Data/Models/Configs/BanCelebrationConfigData.cs`:

```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BanCelebrationConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// </summary>
public class BanCelebrationConfigData
{
    public bool Enabled { get; set; }
    public bool TriggerOnAutoBan { get; set; } = true;
    public bool TriggerOnManualBan { get; set; } = true;
    public bool SendToBannedUser { get; set; } = true;
}
```

- [ ] **Step 7: Create ModerationConfigData wrapper**

Create `TelegramGroupsAdmin.Data/Models/Configs/ModerationConfigData.cs`:

```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Wrapper DTO multiplexing two configs inside the moderation_config JSONB column.
/// JSON shape: { "warningSystem": { ... }, "inviteCommand": { ... } }
/// Both children are nullable so partially-populated rows continue to deserialize.
/// </summary>
public class ModerationConfigData
{
    public WarningSystemConfigData? WarningSystem { get; set; }
    public InviteCommandConfigData? InviteCommand { get; set; }
}
```

### 3c — Add the eight new mapping files

For each mapping file below, follow the established `extension(...)` syntax pattern documented above (see `WelcomeConfigMappings.cs` for the canonical reference).

- [ ] **Step 8: Create LogConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/LogConfigMappings.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class LogConfigMappings
{
    extension(LogConfigData data)
    {
        public LogConfig ToModel() => new()
        {
            DefaultLevel = (LogLevel)data.DefaultLevel,
            Overrides = data.Overrides.ToDictionary(kv => kv.Key, kv => (LogLevel)kv.Value),
            LastModified = data.LastModified
        };
    }

    extension(LogConfig model)
    {
        public LogConfigData ToData() => new()
        {
            DefaultLevel = (int)model.DefaultLevel,
            Overrides = model.Overrides.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
            LastModified = model.LastModified
        };
    }
}
```

- [ ] **Step 9: Create BotProtectionConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/BotProtectionConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class BotProtectionConfigMappings
{
    extension(BotProtectionConfigData data)
    {
        public BotProtectionConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            AutoBanBots = data.AutoBanBots,
            AllowAdminInvitedBots = data.AllowAdminInvitedBots,
            WhitelistedBots = data.WhitelistedBots.ToList(),
            LogBotEvents = data.LogBotEvents
        };
    }

    extension(BotProtectionConfig model)
    {
        public BotProtectionConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            AutoBanBots = model.AutoBanBots,
            AllowAdminInvitedBots = model.AllowAdminInvitedBots,
            WhitelistedBots = model.WhitelistedBots.ToList(),
            LogBotEvents = model.LogBotEvents
        };
    }
}
```

(Note: read `TelegramGroupsAdmin.Data/Models/Configs/BotProtectionConfigData.cs` first to verify property names line up exactly. If `BotProtectionConfigData` has additional properties not on the model, decide per-property whether they belong on the model or are legacy fields.)

- [ ] **Step 10: Create TelegramBotConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/TelegramBotConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class TelegramBotConfigMappings
{
    extension(TelegramBotConfigData data)
    {
        public TelegramBotConfig ToModel() => new()
        {
            BotEnabled = data.BotEnabled
        };
    }

    extension(TelegramBotConfig model)
    {
        public TelegramBotConfigData ToData() => new()
        {
            BotEnabled = model.BotEnabled
        };
    }
}
```

- [ ] **Step 11: Create ServiceMessageDeletionConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/ServiceMessageDeletionConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class ServiceMessageDeletionConfigMappings
{
    extension(ServiceMessageDeletionConfigData data)
    {
        public ServiceMessageDeletionConfig ToModel() => new()
        {
            DeleteJoinMessages = data.DeleteJoinMessages,
            DeleteLeaveMessages = data.DeleteLeaveMessages,
            DeletePhotoChanges = data.DeletePhotoChanges,
            DeleteTitleChanges = data.DeleteTitleChanges,
            DeletePinNotifications = data.DeletePinNotifications,
            DeleteChatCreationMessages = data.DeleteChatCreationMessages
        };
    }

    extension(ServiceMessageDeletionConfig model)
    {
        public ServiceMessageDeletionConfigData ToData() => new()
        {
            DeleteJoinMessages = model.DeleteJoinMessages,
            DeleteLeaveMessages = model.DeleteLeaveMessages,
            DeletePhotoChanges = model.DeletePhotoChanges,
            DeleteTitleChanges = model.DeleteTitleChanges,
            DeletePinNotifications = model.DeletePinNotifications,
            DeleteChatCreationMessages = model.DeleteChatCreationMessages
        };
    }
}
```

- [ ] **Step 12: Create WarningSystemConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/WarningSystemConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class WarningSystemConfigMappings
{
    extension(WarningSystemConfigData data)
    {
        public WarningSystemConfig ToModel() => new()
        {
            AutoBanEnabled = data.AutoBanEnabled,
            AutoBanThreshold = data.AutoBanThreshold,
            AutoBanReason = data.AutoBanReason
        };
    }

    extension(WarningSystemConfig model)
    {
        public WarningSystemConfigData ToData() => new()
        {
            AutoBanEnabled = model.AutoBanEnabled,
            AutoBanThreshold = model.AutoBanThreshold,
            AutoBanReason = model.AutoBanReason
        };
    }
}
```

- [ ] **Step 13: Create InviteCommandConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/InviteCommandConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class InviteCommandConfigMappings
{
    extension(InviteCommandConfigData data)
    {
        public InviteCommandConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            DeleteCommandMessage = data.DeleteCommandMessage,
            DeleteResponseAfterSeconds = data.DeleteResponseAfterSeconds
        };
    }

    extension(InviteCommandConfig model)
    {
        public InviteCommandConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            DeleteCommandMessage = model.DeleteCommandMessage,
            DeleteResponseAfterSeconds = model.DeleteResponseAfterSeconds
        };
    }
}
```

- [ ] **Step 14: Create BanCelebrationConfigMappings**

Create `TelegramGroupsAdmin.Configuration/Mappings/BanCelebrationConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class BanCelebrationConfigMappings
{
    extension(BanCelebrationConfigData data)
    {
        public BanCelebrationConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            TriggerOnAutoBan = data.TriggerOnAutoBan,
            TriggerOnManualBan = data.TriggerOnManualBan,
            SendToBannedUser = data.SendToBannedUser
        };
    }

    extension(BanCelebrationConfig model)
    {
        public BanCelebrationConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            TriggerOnAutoBan = model.TriggerOnAutoBan,
            TriggerOnManualBan = model.TriggerOnManualBan,
            SendToBannedUser = model.SendToBannedUser
        };
    }
}
```

- [ ] **Step 15: Create ModerationConfigMappings (wrapper)**

Create `TelegramGroupsAdmin.Configuration/Mappings/ModerationConfigMappings.cs`:

```csharp
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

/// <summary>
/// Mapping helpers for the ModerationConfigData wrapper that multiplexes
/// WarningSystemConfig and InviteCommandConfig inside the moderation_config column.
/// Used by ConfigRepository.SaveWarningSystemAsync / SaveInviteCommandAsync to
/// merge updates without clobbering the sibling config in the same JSON blob.
/// </summary>
public static class ModerationConfigMappings
{
    extension(ModerationConfigData data)
    {
        /// <summary>
        /// Returns a copy of the wrapper with WarningSystem replaced.
        /// </summary>
        public ModerationConfigData WithWarningSystem(WarningSystemConfigData? warningSystem) => new()
        {
            WarningSystem = warningSystem,
            InviteCommand = data.InviteCommand
        };

        /// <summary>
        /// Returns a copy of the wrapper with InviteCommand replaced.
        /// </summary>
        public ModerationConfigData WithInviteCommand(InviteCommandConfigData? inviteCommand) => new()
        {
            WarningSystem = data.WarningSystem,
            InviteCommand = inviteCommand
        };
    }
}
```

### 3d — Add round-trip unit tests for each new mapping

For each new mapping, write a round-trip test verifying `model → ToData() → JsonSerializer.Serialize → JsonSerializer.Deserialize → ToModel()` returns a model equal to the original. Use NUnit (the established framework) and the same `[TestFixture]` style as `WelcomeConfigMappingsTests.cs`.

- [ ] **Step 16: Write LogConfigMappingsTests as the failing test (TDD)**

Create `TelegramGroupsAdmin.UnitTests/Configuration/LogConfigMappingsTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class LogConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var model = new LogConfig
        {
            DefaultLevel = LogLevel.Warning,
            Overrides = new Dictionary<string, LogLevel>
            {
                ["TelegramGroupsAdmin.Telegram"] = LogLevel.Debug,
                ["Microsoft.EntityFrameworkCore"] = LogLevel.Error
            },
            LastModified = new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<LogConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.DefaultLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(roundTripped.Overrides, Has.Count.EqualTo(2));
            Assert.That(roundTripped.Overrides["TelegramGroupsAdmin.Telegram"], Is.EqualTo(LogLevel.Debug));
            Assert.That(roundTripped.Overrides["Microsoft.EntityFrameworkCore"], Is.EqualTo(LogLevel.Error));
            Assert.That(roundTripped.LastModified, Is.EqualTo(model.LastModified));
        });
    }

    [Test]
    public void ToData_DefaultLogLevel_MapsToInformationInt()
    {
        var model = new LogConfig { DefaultLevel = LogLevel.Information };
        var data = model.ToData();
        Assert.That(data.DefaultLevel, Is.EqualTo(2));
    }
}
```

- [ ] **Step 17: Run LogConfigMappingsTests — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~LogConfigMappingsTests"`
Expected: 2 tests pass. (Mappings were added in step 8; the test goes green immediately.)

If they don't pass, debug the mapping — the test is the source of truth.

- [ ] **Step 18: Repeat the round-trip-test pattern for the remaining 7 mappings**

For each of:
- `BotProtectionConfigMappingsTests`
- `TelegramBotConfigMappingsTests`
- `ServiceMessageDeletionConfigMappingsTests`
- `WarningSystemConfigMappingsTests`
- `InviteCommandConfigMappingsTests`
- `BanCelebrationConfigMappingsTests`
- `ModerationConfigMappingsTests` (test `WithWarningSystem` / `WithInviteCommand` preserve the sibling)

…create `TelegramGroupsAdmin.UnitTests/Configuration/<TestClass>.cs` following the `LogConfigMappingsTests` shape: one full-population round-trip test that asserts every property survives, plus 1-2 edge cases (defaults, lists/dictionaries, nullable boundaries).

For example, `ModerationConfigMappingsTests.cs`:

```csharp
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class ModerationConfigMappingsTests
{
    [Test]
    public void WithWarningSystem_PreservesInviteCommand()
    {
        var original = new ModerationConfigData
        {
            WarningSystem = new WarningSystemConfigData { AutoBanThreshold = 3 },
            InviteCommand = new InviteCommandConfigData { Enabled = true, DeleteResponseAfterSeconds = 60 }
        };

        var updated = original.WithWarningSystem(new WarningSystemConfigData { AutoBanThreshold = 5 });

        Assert.Multiple(() =>
        {
            Assert.That(updated.WarningSystem!.AutoBanThreshold, Is.EqualTo(5));
            Assert.That(updated.InviteCommand!.Enabled, Is.True);
            Assert.That(updated.InviteCommand!.DeleteResponseAfterSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public void WithInviteCommand_PreservesWarningSystem()
    {
        var original = new ModerationConfigData
        {
            WarningSystem = new WarningSystemConfigData { AutoBanThreshold = 3 },
            InviteCommand = new InviteCommandConfigData { Enabled = false }
        };

        var updated = original.WithInviteCommand(new InviteCommandConfigData { Enabled = true });

        Assert.Multiple(() =>
        {
            Assert.That(updated.InviteCommand!.Enabled, Is.True);
            Assert.That(updated.WarningSystem!.AutoBanThreshold, Is.EqualTo(3));
        });
    }
}
```

- [ ] **Step 19: Run all new mapping unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~Configuration"`
Expected: all new tests pass; existing `WelcomeConfigMappingsTests` continues to pass.

- [ ] **Step 20: Verify the full solution still builds**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: build still FAILS at AI consumer call sites from commit 2 (using-statement updates land in commit 6); but the additions in this commit themselves don't add new build errors.

If new errors appear from the BotProtectionConfig/InviteCommandConfig moves — note them but do NOT fix consumers here. Those `using` updates land in commit 6.

- [ ] **Step 21: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(config): add missing DTOs and mappings + unit tests

Adds 4 DTOs to Data/Models/Configs/ (WarningSystem, InviteCommand,
BanCelebration, plus ModerationConfigData wrapper for the multiplexed
moderation_config column). Adds 8 *ConfigMappings.cs files in
Configuration/Mappings/ wiring model ↔ DTO via the C# 12 extension(...)
pattern. Adds round-trip unit tests for each mapping.

Also moves BotProtectionConfig and InviteCommandConfig POCO models from
Telegram/Models/ to Configuration/Models/. These models have no Telegram
dependencies (primitives only) and need to live in Configuration so
IConfigRepository typed methods can return them. Consumer using-statement
updates land in commit 6.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 4: Expand IConfigRepository to typed methods + integration tests

**Commit:** `refactor(config): expand IConfigRepository with typed methods + integration tests`

This commit grows `IConfigRepository` to own all data-layer concerns end-to-end: typed save/get/effective/delete per config, internal mapping + JSON serialization, per-config typed merge, and bot-token encryption. Old anemic methods (`GetAsync(long)`, `UpsertAsync(ConfigRecordDto)`, `DeleteAsync(long)`) **stay** for now because `ConfigService` still uses them — they get removed in commit 7. Build stays **green**.

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Repositories/IConfigRepository.cs`
- Modify: `TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs`
- Modify: `TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj` (add `Microsoft.AspNetCore.DataProtection`)
- Create: `TelegramGroupsAdmin.UnitTests/Configuration/ConfigRepositoryMergeTests.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigRepositoryIntegrationTests.cs`

### 4a — Add the package and expand the interface

- [ ] **Step 1: Add Microsoft.AspNetCore.DataProtection to Configuration csproj**

Edit `TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj`. In the `<ItemGroup>` containing `<PackageReference>` items, add:

```xml
<PackageReference Include="Microsoft.AspNetCore.DataProtection" />
```

(Note: do NOT add `Microsoft.Extensions.Caching.Hybrid` here yet — that lands in commit 5 when `ConfigService` itself moves. Also do NOT add a project ref to `Core` here — that also lands in commit 5.)

- [ ] **Step 2: Expand IConfigRepository with typed methods**

Edit `TelegramGroupsAdmin.Configuration/Repositories/IConfigRepository.cs`. **Add** the typed methods alongside the existing methods (do not remove the existing methods yet — they're removed in commit 7). Add `using` statements for the model namespaces as needed.

The typed surface to add (full text per the spec, Section "IConfigRepository (Configuration project)"):

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

public interface IConfigRepository
{
    // ---- Existing anemic methods (REMOVED in commit 7, kept for ConfigService compat) ----
    Task<ConfigRecordDto?> GetAsync(long chatId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default);
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
    Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);
    Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default);
    Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default);
    Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default);

    // ---- New typed reads (no audit, no info logs) ----
    ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default);
    ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default);

    ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default);
    ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default);

    ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default);
    ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default);

    ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default);
    ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default);

    ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);
    ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);

    ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default);
    ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default);

    ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default);
    ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default);

    ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default);
    ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default);

    // ---- New typed mutations (ChatIdentity for log context) ----
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveLogAsync(ChatIdentity chat, LogConfig config, CancellationToken ct = default);
    Task DeleteLogAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, CancellationToken ct = default);
    Task DeleteBotProtectionAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, CancellationToken ct = default);
    Task DeleteTelegramBotAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, CancellationToken ct = default);
    Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, CancellationToken ct = default);
    Task DeleteWarningSystemAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, CancellationToken ct = default);
    Task DeleteInviteCommandAsync(ChatIdentity chat, CancellationToken ct = default);

    Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, CancellationToken ct = default);
    Task DeleteBanCelebrationAsync(ChatIdentity chat, CancellationToken ct = default);

    // ---- Bot token (encrypted, no chat scope) ----
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, CancellationToken ct = default);
}
```

### 4b — Implement typed methods on ConfigRepository

- [ ] **Step 3: Update ConfigRepository constructor and add shared infrastructure**

Edit `TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs`. Replace the primary-constructor signature with one that injects `IDataProtectionProvider` and `ILogger<ConfigRepository>`. Add `JsonSerializerOptions` field.

Top of the file should look like:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Repositories;

public class ConfigRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigRepository> logger) : IConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ... existing GetAsync / UpsertAsync / DeleteAsync / GetByChatIdAsync / *InviteLink methods stay unchanged ...
```

(The existing methods continue to work — `contextFactory` is now a primary-constructor parameter. Do NOT delete them.)

- [ ] **Step 4: Implement SaveWelcomeAsync per the spec template**

Add to `ConfigRepository.cs`:

```csharp
public async Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(config);

    await using var context = await contextFactory.CreateDbContextAsync(ct);

    var dto = config.ToData();
    var json = JsonSerializer.Serialize(dto, JsonOptions);

    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
    if (record is null)
    {
        record = new ConfigRecordDto { ChatId = chat.Id };
        await context.Configs.AddAsync(record, ct);
    }
    record.WelcomeConfig = json;
    record.UpdatedAt = DateTimeOffset.UtcNow;

    await context.SaveChangesAsync(ct);
    logger.LogInformation("Saved Welcome config for {Chat}", chat.DisplayName);
}
```

- [ ] **Step 5: Implement GetWelcomeAsync per the spec template**

```csharp
public async ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var json = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == chatId)
        .Select(c => c.WelcomeConfig)
        .FirstOrDefaultAsync(ct);

    if (string.IsNullOrEmpty(json)) return null;

    try
    {
        var dto = JsonSerializer.Deserialize<WelcomeConfigData>(json, JsonOptions);
        return dto?.ToModel();
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Failed to deserialize Welcome config for chat {ChatId}", chatId);
        return null;
    }
}
```

- [ ] **Step 6: Implement DeleteWelcomeAsync**

```csharp
public async Task DeleteWelcomeAsync(ChatIdentity chat, CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
    if (record is null) return;

    record.WelcomeConfig = null;
    record.UpdatedAt = DateTimeOffset.UtcNow;
    await context.SaveChangesAsync(ct);
    logger.LogInformation("Deleted Welcome config for {Chat}", chat.DisplayName);
}
```

- [ ] **Step 7: Implement GetEffectiveWelcomeAsync per the spec template (one DB roundtrip)**

```csharp
public async ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var rows = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == 0 || c.ChatId == chatId)
        .Select(c => new { c.ChatId, c.WelcomeConfig })
        .ToListAsync(ct);

    var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.WelcomeConfig;
    var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.WelcomeConfig;

    var globalModel = DeserializeWelcome(globalJson, scope: "global");
    var chatModel = DeserializeWelcome(chatJson, scope: $"chat {chatId}");

    return MergeWelcome(globalModel, chatModel);
}

private WelcomeConfig? DeserializeWelcome(string? json, string scope)
{
    if (string.IsNullOrEmpty(json)) return null;
    try
    {
        return JsonSerializer.Deserialize<WelcomeConfigData>(json, JsonOptions)?.ToModel();
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Failed to deserialize Welcome config for {Scope}", scope);
        return null;
    }
}

private static WelcomeConfig? MergeWelcome(WelcomeConfig? global, WelcomeConfig? chat)
{
    if (chat is null) return global;
    if (global is null) return chat;

    // Per-property merge: chat overrides global where the chat value differs from a fresh-default.
    // Mirrors the semantics of the legacy ConfigService.MergeConfigs<T> reflection helper but typed.
    var defaults = new WelcomeConfig();
    return new WelcomeConfig
    {
        Enabled = chat.Enabled != defaults.Enabled ? chat.Enabled : global.Enabled,
        Mode = chat.Mode != defaults.Mode ? chat.Mode : global.Mode,
        TimeoutSeconds = chat.TimeoutSeconds != defaults.TimeoutSeconds ? chat.TimeoutSeconds : global.TimeoutSeconds,
        MaxKicksBeforeBan = chat.MaxKicksBeforeBan != defaults.MaxKicksBeforeBan ? chat.MaxKicksBeforeBan : global.MaxKicksBeforeBan,
        JoinSecurity = chat.JoinSecurity, // nested config — child overrides parent at the JoinSecurity level
        MainWelcomeMessage = !string.IsNullOrEmpty(chat.MainWelcomeMessage) ? chat.MainWelcomeMessage : global.MainWelcomeMessage,
        DmChatTeaserMessage = !string.IsNullOrEmpty(chat.DmChatTeaserMessage) ? chat.DmChatTeaserMessage : global.DmChatTeaserMessage,
        AcceptButtonText = !string.IsNullOrEmpty(chat.AcceptButtonText) ? chat.AcceptButtonText : global.AcceptButtonText,
        DenyButtonText = !string.IsNullOrEmpty(chat.DenyButtonText) ? chat.DenyButtonText : global.DenyButtonText,
        DmButtonText = !string.IsNullOrEmpty(chat.DmButtonText) ? chat.DmButtonText : global.DmButtonText,
        ExamConfig = chat.ExamConfig ?? global.ExamConfig,
        TrustedBypass = chat.TrustedBypass
    };
}
```

(If you discover that the legacy `ConfigService.MergeConfigs<T>` reflection-based behavior differs from this property-by-property merge for any specific config in a way that breaks an existing call site, document the difference in a comment and prefer matching the legacy behavior to avoid silent semantic regressions. Spec acceptance criteria: per-config merge tested in isolation.)

- [ ] **Step 8: Repeat the four-method pattern (Save/Get/Delete/GetEffective) for the remaining 7 configs**

For each of the 7 remaining configs — `Log`, `BotProtection`, `TelegramBot`, `ServiceMessageDeletion`, `WarningSystem`, `InviteCommand`, `BanCelebration` — copy the Welcome four-method shape from steps 4-7 and adapt:

- Column property name on `ConfigRecordDto` (see the per-config inventory table at the top of this plan).
- DTO type name.
- Mapping methods (`ToData()` / `ToModel()`).
- Per-config merge implementation (write each one by hand — DO NOT extract to a generic helper, and DO NOT use reflection. The whole point is moving away from the reflection-based merge).

**Special case — Moderation column multiplexing for `WarningSystem` and `InviteCommand`:**

These two configs share the `ModerationConfig` JSON column. Each Save method must read the existing `ModerationConfigData` (if any), update only its own field, serialize the wrapper, and write back. Pattern:

```csharp
public async Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(config);

    await using var context = await contextFactory.CreateDbContextAsync(ct);

    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
    var existingWrapper = ParseModerationWrapper(record?.ModerationConfig);
    var updated = existingWrapper.WithWarningSystem(config.ToData());
    var json = JsonSerializer.Serialize(updated, JsonOptions);

    if (record is null)
    {
        record = new ConfigRecordDto { ChatId = chat.Id };
        await context.Configs.AddAsync(record, ct);
    }
    record.ModerationConfig = json;
    record.UpdatedAt = DateTimeOffset.UtcNow;

    await context.SaveChangesAsync(ct);
    logger.LogInformation("Saved WarningSystem config for {Chat}", chat.DisplayName);
}

private ModerationConfigData ParseModerationWrapper(string? json)
{
    if (string.IsNullOrEmpty(json)) return new ModerationConfigData();
    try
    {
        return JsonSerializer.Deserialize<ModerationConfigData>(json, JsonOptions) ?? new ModerationConfigData();
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Failed to deserialize moderation_config wrapper; treating as empty");
        return new ModerationConfigData();
    }
}

public async ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var json = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == chatId)
        .Select(c => c.ModerationConfig)
        .FirstOrDefaultAsync(ct);

    var wrapper = ParseModerationWrapper(json);
    return wrapper.WarningSystem?.ToModel();
}

public async Task DeleteWarningSystemAsync(ChatIdentity chat, CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct);
    if (record is null) return;

    var wrapper = ParseModerationWrapper(record.ModerationConfig);
    var updated = wrapper.WithWarningSystem(null);
    record.ModerationConfig = updated.WarningSystem is null && updated.InviteCommand is null
        ? null
        : JsonSerializer.Serialize(updated, JsonOptions);
    record.UpdatedAt = DateTimeOffset.UtcNow;
    await context.SaveChangesAsync(ct);
    logger.LogInformation("Deleted WarningSystem config for {Chat}", chat.DisplayName);
}
```

`SaveInviteCommandAsync` / `GetInviteCommandAsync` / `DeleteInviteCommandAsync` follow the same shape, swapping `WarningSystem` ↔ `InviteCommand`. `GetEffectiveInviteCommandAsync` and `GetEffectiveWarningSystemAsync` deserialize both rows' wrappers and merge the relevant child.

- [ ] **Step 9: Implement bot token methods (encrypted column, no chat scope)**

Add to `ConfigRepository.cs`, mirroring `SystemConfigRepository.GetUserApiHashAsync` / `SetUserApiHashAsync` exactly:

```csharp
public async ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default)
{
    await using var context = await contextFactory.CreateDbContextAsync(ct);
    var encrypted = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == 0)
        .Select(c => c.TelegramBotTokenEncrypted)
        .FirstOrDefaultAsync(ct);

    if (string.IsNullOrEmpty(encrypted)) return null;

    try
    {
        var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
        return protector.Unprotect(encrypted);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to decrypt Telegram bot token");
        return null;
    }
}

public async Task SaveBotTokenAsync(string botToken, CancellationToken ct = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(botToken);

    await using var context = await contextFactory.CreateDbContextAsync(ct);

    var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
    var encrypted = protector.Protect(botToken);

    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == 0, ct);
    if (record is null)
    {
        record = new ConfigRecordDto { ChatId = 0 };
        await context.Configs.AddAsync(record, ct);
    }
    record.TelegramBotTokenEncrypted = encrypted;
    record.UpdatedAt = DateTimeOffset.UtcNow;

    await context.SaveChangesAsync(ct);
    logger.LogInformation("Saved Telegram bot token (encrypted)");
}
```

(The `DataProtectionPurposes.TelegramBotToken` constant and the cipher format do NOT change — existing encrypted tokens in production continue to decrypt without migration.)

- [ ] **Step 10: Update DI registration to inject the new dependencies**

`ConfigRepository` is registered in `TelegramGroupsAdmin.Configuration/ConfigurationExtensions.cs:21` as `services.AddScoped<IConfigRepository, ConfigRepository>();`. No change needed — DI auto-resolves the new constructor parameters from the existing service collection (`IDataProtectionProvider` is registered globally; `ILogger<>` is auto-resolved).

### 4c — Add per-config merge unit tests

- [ ] **Step 11: Write the failing per-config merge unit tests**

Create `TelegramGroupsAdmin.UnitTests/Configuration/ConfigRepositoryMergeTests.cs`. Because the merge methods are `private static`, expose them via `internal` or a dedicated test seam — simplest path: change the per-config private merge methods (e.g., `MergeWelcome`) to `internal static` and add `[InternalsVisibleTo("TelegramGroupsAdmin.UnitTests")]` to `TelegramGroupsAdmin.Configuration.csproj` (it's already there per current state — verify).

Test file scaffold:

```csharp
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class ConfigRepositoryMergeTests
{
    // ---- Welcome ----
    [Test]
    public void MergeWelcome_ChatNull_ReturnsGlobal()
    {
        var global = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "global hi" };
        Assert.That(ConfigRepository.MergeWelcome(global, null), Is.SameAs(global));
    }

    [Test]
    public void MergeWelcome_GlobalNull_ReturnsChat()
    {
        var chat = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "chat hi" };
        Assert.That(ConfigRepository.MergeWelcome(null, chat), Is.SameAs(chat));
    }

    [Test]
    public void MergeWelcome_BothNull_ReturnsNull()
    {
        Assert.That(ConfigRepository.MergeWelcome(null, null), Is.Null);
    }

    [Test]
    public void MergeWelcome_ChatOverridesNonDefault_GlobalFallthrough()
    {
        var global = new WelcomeConfig { Enabled = false, MainWelcomeMessage = "global", TimeoutSeconds = 60 };
        var chat = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "", TimeoutSeconds = 0 };
        var merged = ConfigRepository.MergeWelcome(global, chat)!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Enabled, Is.True, "chat overrode");
            Assert.That(merged.MainWelcomeMessage, Is.EqualTo("global"), "chat default falls through to global");
            Assert.That(merged.TimeoutSeconds, Is.EqualTo(60), "chat default falls through to global");
        });
    }

    // ---- Repeat the same 4-test pattern (chat-null, global-null, both-null, chat-overrides) for:
    //      Log, BotProtection, TelegramBot, ServiceMessageDeletion, WarningSystem,
    //      InviteCommand, BanCelebration. ~5 cases × 8 configs ≈ 40 tests.
    //      Each block uses meaningful per-config field assertions, not boilerplate.
}
```

(Write the full ~40 tests in this file. Do not skip any config — the spec acceptance criteria require per-config merge coverage.)

- [ ] **Step 12: Run merge unit tests — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~ConfigRepositoryMergeTests"`
Expected: all merge tests pass.

If a test fails, the merge implementation is wrong — fix the implementation, not the test.

### 4d — Add integration tests against real PostgreSQL

- [ ] **Step 13: Write the failing integration tests for save/get round-trip**

Create `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigRepositoryIntegrationTests.cs`. Follow the pattern from `AIProviderConfigIntegrationTests.cs` (DI setup, ephemeral data-protection keys, `MigrationTestHelper`). Skeleton:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

[TestFixture]
public class ConfigRepositoryIntegrationTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IConfigRepository? _repo;

    private static readonly ChatIdentity GlobalChat = new(0, "global");
    private static readonly ChatIdentity TestChat = new(123456789L, "Test Chat");

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();
        var keyDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}"));
        services.AddDataProtection().SetApplicationName("TelegramGroupsAdmin.Tests").PersistKeysToFileSystem(keyDir);

        var dataSource = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build();
        services.AddSingleton(dataSource);
        services.AddDbContextFactory<AppDbContext>((_, opt) => opt.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IConfigRepository, ConfigRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repo = _serviceProvider.GetRequiredService<IConfigRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is IAsyncDisposable d) await d.DisposeAsync();
        if (_testHelper is not null) await _testHelper.DropDatabaseAsync();
    }

    [Test]
    public async Task SaveAndGet_Welcome_RoundTripPreservesAllFields()
    {
        var original = new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.DmWelcome,
            TimeoutSeconds = 120,
            MaxKicksBeforeBan = 3,
            MainWelcomeMessage = "Welcome to the chat!",
            AcceptButtonText = "Accept",
            DenyButtonText = "Deny",
            DmButtonText = "DM"
        };

        await _repo!.SaveWelcomeAsync(TestChat, original);
        var retrieved = await _repo.GetWelcomeAsync(TestChat.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Enabled, Is.True);
            Assert.That(retrieved.Mode, Is.EqualTo(WelcomeMode.DmWelcome));
            Assert.That(retrieved.TimeoutSeconds, Is.EqualTo(120));
            Assert.That(retrieved.MainWelcomeMessage, Is.EqualTo("Welcome to the chat!"));
            // ... assert every property survived ...
        });
    }

    // Add SaveAndGet_<Config>_RoundTripPreservesAllFields for the other 7 configs.

    [Test]
    public async Task GetEffective_Welcome_OnlyGlobal_ReturnsGlobal()
    {
        await _repo!.SaveWelcomeAsync(GlobalChat, new WelcomeConfig { MainWelcomeMessage = "global" });
        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);
        Assert.That(effective?.MainWelcomeMessage, Is.EqualTo("global"));
    }

    [Test]
    public async Task GetEffective_Welcome_OnlyChat_ReturnsChat()
    {
        await _repo!.SaveWelcomeAsync(TestChat, new WelcomeConfig { MainWelcomeMessage = "chat" });
        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);
        Assert.That(effective?.MainWelcomeMessage, Is.EqualTo("chat"));
    }

    [Test]
    public async Task GetEffective_Welcome_BothPresent_ChatOverrides()
    {
        await _repo!.SaveWelcomeAsync(GlobalChat, new WelcomeConfig { MainWelcomeMessage = "global", TimeoutSeconds = 60 });
        await _repo.SaveWelcomeAsync(TestChat, new WelcomeConfig { MainWelcomeMessage = "chat" });
        var effective = await _repo.GetEffectiveWelcomeAsync(TestChat.Id);
        Assert.Multiple(() =>
        {
            Assert.That(effective?.MainWelcomeMessage, Is.EqualTo("chat"), "chat overrides");
            Assert.That(effective?.TimeoutSeconds, Is.EqualTo(60), "chat default falls through to global");
        });
    }

    // Repeat GetEffective_<Config>_<Scenario> for the other 7 configs.

    [Test]
    public async Task SaveBotToken_RoundTrip_StoresEncryptedReturnsDecrypted()
    {
        const string plain = "1234567890:ABCdefGHI_jklMNOpqrstuVWxyz";

        await _repo!.SaveBotTokenAsync(plain);

        // Verify ciphertext-at-rest by reading the column directly via DbContext.
        await using var ctx = await _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var encrypted = await ctx.Configs.AsNoTracking()
            .Where(c => c.ChatId == 0)
            .Select(c => c.TelegramBotTokenEncrypted)
            .FirstOrDefaultAsync();

        Assert.That(encrypted, Is.Not.Null);
        Assert.That(encrypted, Is.Not.EqualTo(plain), "must be encrypted at rest");

        var roundTripped = await _repo.GetBotTokenAsync();
        Assert.That(roundTripped, Is.EqualTo(plain));
    }

    [Test]
    public async Task SaveWarningSystem_DoesNotClobberInviteCommand()
    {
        // Multiplexed-column safety: writing one moderation child must preserve the other.
        await _repo!.SaveInviteCommandAsync(TestChat, new InviteCommandConfig { Enabled = true, DeleteResponseAfterSeconds = 99 });
        await _repo.SaveWarningSystemAsync(TestChat, new WarningSystemConfig { AutoBanThreshold = 7 });

        var invite = await _repo.GetInviteCommandAsync(TestChat.Id);
        var warning = await _repo.GetWarningSystemAsync(TestChat.Id);

        Assert.Multiple(() =>
        {
            Assert.That(invite?.DeleteResponseAfterSeconds, Is.EqualTo(99), "invite preserved through warning save");
            Assert.That(warning?.AutoBanThreshold, Is.EqualTo(7));
        });
    }
}
```

- [ ] **Step 14: Run integration tests — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj --filter "FullyQualifiedName~ConfigRepositoryIntegrationTests"`
Expected: all integration tests pass.

If a test fails, the repository implementation is wrong — fix the implementation, not the test.

- [ ] **Step 15: Verify the full solution still compiles**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: still has the AI consumer namespace errors from commit 2 (gets fixed in commit 6); but the new typed methods compile cleanly and Configuration project is green.

- [ ] **Step 16: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(config): expand IConfigRepository with typed methods + integration tests

Add typed methods on IConfigRepository (Get/GetEffective/Save/Delete per
config plus GetBotTokenAsync/SaveBotTokenAsync). ConfigRepository now
owns mapping → JSON serialization → upsert end-to-end, with per-config
typed merge replacing the reflection-based MergeConfigs<T>. Bot-token
encryption migrates from ConfigService into the repo, mirroring the
SystemConfigRepository UserApiHash pattern. Configuration csproj gains
Microsoft.AspNetCore.DataProtection.

Old anemic methods (GetAsync(long), UpsertAsync(ConfigRecordDto), etc.)
remain temporarily — ConfigService still uses them. They get removed in
commit 7 once ConfigService has migrated to the typed surface.

Tests: ~40 per-config merge unit tests + ConfigRepositoryIntegrationTests
covering save/get round-trip per config, GetEffective merge scenarios,
bot-token ciphertext-at-rest assertion, and Moderation column multiplex
safety.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 5: Flip Core ↔ Configuration project edges and relocate ConfigService

**Commit:** `refactor(config): flip Core ↔ Configuration project edges and relocate ConfigService`

This commit moves `IConfigService` and `ConfigService` from `Core/Services/` to `Configuration/Services/` and flips the project reference direction. Build will be **broken** at the end — `ConfigService` still calls the old generic API but lives in a new namespace, and consumers in Telegram still import the old namespace. Fixed in commit 6.

**Files:**
- Move: `TelegramGroupsAdmin.Core/Services/IConfigService.cs` → `TelegramGroupsAdmin.Configuration/Services/IConfigService.cs`
- Move: `TelegramGroupsAdmin.Core/Services/ConfigService.cs` → `TelegramGroupsAdmin.Configuration/Services/ConfigService.cs`
- Modify: `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj` (remove `ProjectReference` to Configuration; remove `Microsoft.Extensions.Caching.Hybrid`; remove `Microsoft.AspNetCore.DataProtection`)
- Modify: `TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj` (add `ProjectReference` to Core; add `Microsoft.Extensions.Caching.Hybrid`)
- Modify: `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs` (drop `IConfigService` registration)
- Modify: `TelegramGroupsAdmin.Configuration/ConfigurationExtensions.cs` (add `IConfigService` registration)

- [ ] **Step 1: Move the two service files via git mv**

```bash
mkdir -p TelegramGroupsAdmin.Configuration/Services
git mv TelegramGroupsAdmin.Core/Services/IConfigService.cs   TelegramGroupsAdmin.Configuration/Services/IConfigService.cs
git mv TelegramGroupsAdmin.Core/Services/ConfigService.cs    TelegramGroupsAdmin.Configuration/Services/ConfigService.cs
```

- [ ] **Step 2: Flip namespaces on the moved files**

In both files, replace `namespace TelegramGroupsAdmin.Core.Services;` with `namespace TelegramGroupsAdmin.Configuration.Services;`.

Also update their using-statement blocks: `IConfigService.cs` no longer needs `using TelegramGroupsAdmin.Configuration;` (it IS Configuration now); `ConfigService.cs` likewise. Both will need `using TelegramGroupsAdmin.Configuration.Repositories;` if they use it — verify after the move.

- [ ] **Step 3: Remove Core's ProjectReference to Configuration**

Edit `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`. In the `<ItemGroup>` containing `<ProjectReference>` items, remove the line:

```xml
<ProjectReference Include="..\TelegramGroupsAdmin.Configuration\TelegramGroupsAdmin.Configuration.csproj" />
```

Also remove from the `<PackageReference>` block:

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" />
<PackageReference Include="Microsoft.AspNetCore.DataProtection" />
```

(Configuration now owns both packages — Core no longer needs them.)

- [ ] **Step 4: Add Configuration → Core ProjectReference and HybridCache package**

Edit `TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj`. Add to the `<ItemGroup>` containing `<PackageReference>`:

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" />
```

Add to the `<ItemGroup>` containing `<ProjectReference>`:

```xml
<ProjectReference Include="..\TelegramGroupsAdmin.Core\TelegramGroupsAdmin.Core.csproj" />
```

- [ ] **Step 5: Drop IConfigService registration from Core's ServiceCollectionExtensions**

Edit `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`. Remove the line:

```csharp
// Unified configuration service (database-driven config with global/chat-specific merging)
services.AddScoped<IConfigService, ConfigService>();
```

Also remove the `using` statements for `TelegramGroupsAdmin.Core.Services` if `IConfigService` was the only consumer (verify other services in the file).

- [ ] **Step 6: Add IConfigService registration to ConfigurationExtensions**

Edit `TelegramGroupsAdmin.Configuration/ConfigurationExtensions.cs`. Inside the `AddApplicationConfiguration` method, after the existing repository registrations, add:

```csharp
using TelegramGroupsAdmin.Configuration.Services;

// ... inside AddApplicationConfiguration:

// Unified configuration service (database-driven config with global/chat-specific merging)
services.AddScoped<IConfigService, ConfigService>();
```

- [ ] **Step 7: Confirm the broken-build is in the expected state**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: build FAILS with errors like:
- "type or namespace name 'IConfigService' does not exist in 'TelegramGroupsAdmin.Core.Services'" at Telegram/Razor consumer call sites.
- ConfigService.cs may have its own errors because its body still calls `IConfigRepository.GetAsync(chatId)` / `UpsertAsync(record)` / encryption, but the new file location is now `Configuration/Services/`. The internal reference to `IConfigRepository` should still resolve because both live in the Configuration assembly now — the body of `ConfigService` is rewritten in commit 6, not yet.

These errors are expected. Do NOT fix consumers in this commit.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(config): flip Core ↔ Configuration project edges and relocate ConfigService

Move IConfigService.cs and ConfigService.cs from Core/Services/ to
Configuration/Services/. Namespace flips from TelegramGroupsAdmin.Core.Services
to TelegramGroupsAdmin.Configuration.Services. Project reference direction
inverts: Core no longer references Configuration; Configuration now
references Core. Microsoft.Extensions.Caching.Hybrid and
Microsoft.AspNetCore.DataProtection packages move from Core to Configuration.
DI registration of IConfigService moves from Core's ServiceCollectionExtensions
to ConfigurationExtensions.

ConfigService body is NOT rewritten yet — still uses the old generic API.
Build is intentionally broken at this commit. ConfigService body rewrite
and consumer migration land in commit 6.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 6: Rewire ConfigService to typed surface with audit + migrate all consumers

**Commit:** `refactor(config): rewire ConfigService to typed surface with audit, update consumers`

This is the largest commit. ConfigService body is rewritten to delegate to the typed `IConfigRepository` and emit audit events. The interface drops the generic API. ~30+ consumer sites migrate from `cfg.GetAsync<T>(ConfigType.X, id)` to `cfg.GetXAsync(id)`. Component test mocks update. Build returns to **green** at this commit's end — final PR is buildable from here.

**Files (modified):**
- `TelegramGroupsAdmin.Configuration/Services/IConfigService.cs` — rewritten to typed surface.
- `TelegramGroupsAdmin.Configuration/Services/ConfigService.cs` — body rewrite (delegates to typed repo + audit + cache invalidation).
- All ~30+ consumer files identified in the consumer inventory step below.

### 6a — Inventory consumers

- [ ] **Step 1: Generate the consumer call-site inventory**

Run: `Grep` with pattern `ConfigType\.\|cfg.*\.GetAsync<\|cfg.*\.SaveAsync<\|cfg.*\.GetEffectiveAsync<\|cfg.*\.DeleteAsync\(ConfigType\|configService\..*Async<\|GetTelegramBotTokenAsync\|SaveTelegramBotTokenAsync` across the repo (exclude `bin/`, `obj/`, `Migrations/`).

Expected discoveries (verify against the live repo state — this list is from the spec investigation):

Razor pages:
- `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:560,622`
- `TelegramGroupsAdmin/Components/Shared/BotGeneralSettings.razor:265,269,296,301,324,355`
- `TelegramGroupsAdmin/Components/Shared/Settings/BanCelebrationChatSettings.razor:70,83`
- `TelegramGroupsAdmin/Components/Shared/ServiceMessageDeletionSettings.razor:146,175`

Background jobs:
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/RefreshUserPhotosJob.cs:80`
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/ChatHealthCheckJob.cs:79`
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/FetchUserPhotoJob.cs:54`

Telegram services / handlers:
- `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/TelegramBotPollingHost.cs:93`
- `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs:45,278`
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/InviteCommand.cs:61`
- `TelegramGroupsAdmin.Telegram/Services/BotProtectionService.cs:35`
- `TelegramGroupsAdmin.Telegram/Handlers/MessageEditProcessor.cs:143`
- `TelegramGroupsAdmin.Telegram/Services/TelegramConfigLoader.cs:43`
- (plus all other `IConfigService` consumers in the Telegram project)

App layer:
- `TelegramGroupsAdmin/Services/RuntimeLoggingService.cs:57,75,93`

Component tests:
- `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`
- `TelegramGroupsAdmin.ComponentTests/Components/BotGeneralSettingsTests.cs`
- `TelegramGroupsAdmin.ComponentTests/Components/BanCelebrationSettingsTests.cs`
- `TelegramGroupsAdmin.ComponentTests/Components/BanCelebrationChatSettingsTests.cs`
- `TelegramGroupsAdmin.ComponentTests/Components/ServiceMessageDeletionSettingsTests.cs`
- `TelegramGroupsAdmin.ComponentTests/Components/ExamReviewCardTests.cs`

AI consumer namespace updates (from commit 2):
- All Telegram-project files that previously imported `using TelegramGroupsAdmin.Core.Services.AI;` need to change to `using TelegramGroupsAdmin.AI.Services;`.

BotProtectionConfig / InviteCommandConfig namespace updates (from commit 3):
- All files that previously imported `using TelegramGroupsAdmin.Telegram.Models;` for these two POCOs need to change to `using TelegramGroupsAdmin.Configuration.Models;`.

Save the discovered list to scratch (e.g., `git status --short` plus paste into your worklog).

### 6b — Rewrite IConfigService

- [ ] **Step 2: Rewrite IConfigService to the typed surface**

Replace the entire body of `TelegramGroupsAdmin.Configuration/Services/IConfigService.cs`:

```csharp
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Typed configuration service. Reads use long chatId; mutations require ChatIdentity
/// for log context plus an Actor for audit attribution.
/// </summary>
public interface IConfigService
{
    // --- Reads ---
    ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default);
    ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default);

    ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default);
    ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default);

    ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default);
    ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default);

    ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default);
    ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default);

    ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);
    ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);

    ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default);
    ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default);

    ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default);
    ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default);

    ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default);
    ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default);

    // --- Mutations ---
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveLogAsync(ChatIdentity chat, LogConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteLogAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveBotProtectionAsync(ChatIdentity chat, BotProtectionConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteBotProtectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveTelegramBotAsync(ChatIdentity chat, TelegramBotConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteTelegramBotAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveServiceMessageDeletionAsync(ChatIdentity chat, ServiceMessageDeletionConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteServiceMessageDeletionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveWarningSystemAsync(ChatIdentity chat, WarningSystemConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteWarningSystemAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveInviteCommandAsync(ChatIdentity chat, InviteCommandConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteInviteCommandAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    Task SaveBanCelebrationAsync(ChatIdentity chat, BanCelebrationConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteBanCelebrationAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    // --- Bot token ---
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, Actor initiator, CancellationToken ct = default);

    // --- ContentDetection helpers (delegate to IContentDetectionConfigRepository, retained) ---
    Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default);
}
```

### 6c — Rewrite ConfigService body

- [ ] **Step 3: Rewrite ConfigService body to delegate + audit + cache**

Replace the entire body of `TelegramGroupsAdmin.Configuration/Services/ConfigService.cs`:

```csharp
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Typed configuration service: caches reads via HybridCache, emits audit events
/// on mutations, delegates all data-layer work (mapping, JSON, encryption, merge)
/// to IConfigRepository.
/// </summary>
public class ConfigService(
    IConfigRepository repository,
    IContentDetectionConfigRepository contentDetectionRepository,
    IAuditService auditService,
    HybridCache cache,
    ILogger<ConfigService> logger) : IConfigService
{
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(15) };

    // ---- Welcome ----

    public ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_welcome_{chatId}",
            async _ => await repository.GetWelcomeAsync(chatId, ct),
            CacheOptions, cancellationToken: ct);

    public ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)
        => cache.GetOrCreateAsync($"cfg_effective_welcome_{chatId}",
            async _ => await repository.GetEffectiveWelcomeAsync(chatId, ct),
            CacheOptions, tags: ["effective_welcome"], cancellationToken: ct);

    public async Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, Actor initiator, CancellationToken ct = default)
    {
        await repository.SaveWelcomeAsync(chat, config, ct);
        await EmitAuditAsync("Welcome", chat, initiator, ct);
        await InvalidateAsync("welcome", chat.Id, ct);
        logger.LogInformation("Welcome config saved for {Chat} by {Actor}", chat.DisplayName, initiator.GetDisplayText());
    }

    public async Task DeleteWelcomeAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
    {
        await repository.DeleteWelcomeAsync(chat, ct);
        await EmitAuditAsync("Welcome (deleted)", chat, initiator, ct);
        await InvalidateAsync("welcome", chat.Id, ct);
        logger.LogInformation("Welcome config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.GetDisplayText());
    }

    // ---- Repeat the same six-method block for: Log, BotProtection, TelegramBot,
    //      ServiceMessageDeletion, WarningSystem, InviteCommand, BanCelebration.
    //      Each block is mechanical — only the type name and cache-key prefix change.

    // ---- Bot token ----

    public ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync("cfg_bot_token",
            async _ => await repository.GetBotTokenAsync(ct),
            CacheOptions, cancellationToken: ct);

    public async Task SaveBotTokenAsync(string botToken, Actor initiator, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);
        await repository.SaveBotTokenAsync(botToken, ct);
        await auditService.LogEventAsync(AuditEventType.ConfigurationChanged, initiator, target: null, value: "TelegramBotToken", ct);
        await cache.RemoveAsync("cfg_bot_token", ct);
        logger.LogInformation("Telegram bot token saved by {Actor}", initiator.GetDisplayText());
    }

    // ---- ContentDetection delegates (retained) ----

    public Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default)
        => contentDetectionRepository.GetAllChatConfigsAsync(cancellationToken);

    public Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default)
        => contentDetectionRepository.GetCriticalCheckNamesAsync(chatId, cancellationToken);

    // ---- Helpers ----

    private Task EmitAuditAsync(string configName, ChatIdentity chat, Actor initiator, CancellationToken ct)
        => auditService.LogEventAsync(
            AuditEventType.ConfigurationChanged,
            initiator,
            target: null,
            value: $"{configName} ({chat.DisplayName})",
            ct);

    private async Task InvalidateAsync(string keyPrefix, long chatId, CancellationToken ct)
    {
        await cache.RemoveAsync($"cfg_{keyPrefix}_{chatId}", ct);
        if (chatId != 0)
            await cache.RemoveAsync($"cfg_effective_{keyPrefix}_{chatId}", ct);
        else
            await cache.RemoveByTagAsync($"effective_{keyPrefix}", ct);
    }
}
```

(Write the seven repeated six-method blocks. Each is mechanical — copy the Welcome block and substitute.)

- [ ] **Step 4: Add ConfigService unit tests**

Create `TelegramGroupsAdmin.UnitTests/Configuration/ConfigServiceTests.cs`. Use NSubstitute (the project's existing mocking framework — see `BotGeneralSettingsTests.cs` for examples). Cover:

- Save path: verify `repo.SaveXxxAsync(chat, config, ct)` is called with the right args; verify `auditService.LogEventAsync(ConfigurationChanged, initiator, ...)` fires; verify `cache.RemoveAsync` with the right key + `RemoveByTagAsync` for global saves.
- Get path: verify `cache.GetOrCreateAsync` is invoked (factory delegate calls `repo.GetXxxAsync`).
- Delete path: verify both repo invocation and audit emission.
- Bot token: ciphertext NOT in audit value.

Skeleton:

```csharp
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class ConfigServiceTests
{
    private IConfigRepository _repo = null!;
    private IContentDetectionConfigRepository _cdRepo = null!;
    private IAuditService _audit = null!;
    private HybridCache _cache = null!;
    private ConfigService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IConfigRepository>();
        _cdRepo = Substitute.For<IContentDetectionConfigRepository>();
        _audit = Substitute.For<IAuditService>();
        _cache = Substitute.For<HybridCache>();
        _sut = new ConfigService(_repo, _cdRepo, _audit, _cache, NullLogger<ConfigService>.Instance);
    }

    [Test]
    public async Task SaveWelcomeAsync_DelegatesToRepoAndEmitsAudit()
    {
        var chat = new ChatIdentity(42, "Test Chat");
        var config = new WelcomeConfig { Enabled = true };
        var actor = Actor.FromWebUser("user-1", "test@example.com");

        await _sut.SaveWelcomeAsync(chat, config, actor);

        await _repo.Received(1).SaveWelcomeAsync(chat, config, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => v.Contains("Welcome") && v.Contains("Test Chat")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveWelcomeAsync_GlobalScope_RemovesByTag()
    {
        var chat = new ChatIdentity(0, "global");
        var config = new WelcomeConfig();
        var actor = Actor.SystemSeed;

        await _sut.SaveWelcomeAsync(chat, config, actor);

        await _cache.Received(1).RemoveAsync("cfg_welcome_0", Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByTagAsync("effective_welcome", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveBotTokenAsync_AuditValueDoesNotContainPlaintext()
    {
        var actor = Actor.FromWebUser("user-1", "u@e.com");
        const string secret = "1234567890:SECRET";

        await _sut.SaveBotTokenAsync(secret, actor);

        await _audit.Received(1).LogEventAsync(
            AuditEventType.ConfigurationChanged,
            actor,
            target: null,
            value: Arg.Is<string>(v => !v.Contains(secret)),
            Arg.Any<CancellationToken>());
    }

    // Add the analogous SaveXxxAsync_DelegatesToRepoAndEmitsAudit for the other 7 configs,
    // plus DeleteXxxAsync coverage for each.
}
```

- [ ] **Step 5: Run ConfigService unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~ConfigServiceTests"`
Expected: all tests pass.

### 6d — Sweep call sites

- [ ] **Step 6: Migrate Razor settings consumers**

For each Razor page in the consumer inventory, change the call site. Pattern transformations:

```razor
@* Before *@
@inject IConfigService ConfigService
@using TelegramGroupsAdmin.Configuration  @* for ConfigType *@
...
var config = await ConfigService.GetAsync<WelcomeConfig>(ConfigType.Welcome, Chat?.Identity.Id ?? 0);
...
await ConfigService.SaveAsync(ConfigType.Welcome, Chat?.Identity ?? ChatIdentity.FromId(0), _config);
```

```razor
@* After *@
@inject IConfigService ConfigService
@using TelegramGroupsAdmin.Configuration.Services
@using TelegramGroupsAdmin.Configuration.Models.Welcome
...
var config = await ConfigService.GetWelcomeAsync(Chat?.Identity.Id ?? 0);
...
var actor = WebUser!.ToActor();   // already established pattern (FileScanningSettings.razor:569)
await ConfigService.SaveWelcomeAsync(Chat?.Identity ?? ChatIdentity.FromId(0), _config, actor);
```

Apply the same transformation to:
- `WelcomeSystemConfig.razor` (Welcome)
- `BotGeneralSettings.razor` (TelegramBot + BotProtection + Bot token)
- `BanCelebrationChatSettings.razor` (BanCelebration)
- `ServiceMessageDeletionSettings.razor` (ServiceMessageDeletion)

For `BotGeneralSettings.razor` bot-token specifically: `ConfigService.GetTelegramBotTokenAsync()` becomes `ConfigService.GetBotTokenAsync()`; `ConfigService.SaveTelegramBotTokenAsync(_botToken)` becomes `ConfigService.SaveBotTokenAsync(_botToken, actor)`.

- [ ] **Step 7: Migrate background-job consumers**

For `RefreshUserPhotosJob.cs`, `ChatHealthCheckJob.cs`, `FetchUserPhotoJob.cs`, change `await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0)` to `await configService.GetTelegramBotAsync(0)`. Background jobs are read-only here (no save), so no Actor needed.

- [ ] **Step 8: Migrate Telegram services / handlers**

For each consumer:
- `TelegramBotPollingHost.cs:93`: `GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0)` → `GetTelegramBotAsync(0)`.
- `BanCelebrationService.cs:45,278`: `GetEffectiveAsync<BanCelebrationConfig>(ConfigType.BanCelebration, chatId)` → `GetEffectiveBanCelebrationAsync(chatId)`; `GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id)` → `GetEffectiveWelcomeAsync(chat.Id)`.
- `InviteCommand.cs:61`: `GetEffectiveAsync<InviteCommandConfig>(ConfigType.Moderation, chatId)` → `GetEffectiveInviteCommandAsync(chatId)`.
- `BotProtectionService.cs:35`: `GetEffectiveAsync<BotProtectionConfig>(ConfigType.UrlFilter, chat.Id)` → `GetEffectiveBotProtectionAsync(chat.Id)`.
- `MessageEditProcessor.cs:143`: `GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, ...)` — note this is ContentDetection, which is NOT in the typed-method list because it routes via a separate repository. Verify: the new IConfigService no longer exposes ContentDetection through this generic API. Either (a) inject `IContentDetectionConfigRepository` directly into `MessageEditProcessor` and call `GetEffectiveConfigAsync(chatId)` on it, or (b) add `GetEffectiveContentDetectionAsync(long chatId)` as a thin convenience method on `IConfigService` that delegates to the CD repo. Pick (a) — it's the cleaner separation and matches the spec's principle of "services don't dispatch by enum."
- `TelegramConfigLoader.cs:43`: `GetTelegramBotTokenAsync()` → `GetBotTokenAsync()`.
- All other Telegram-project files importing `TelegramGroupsAdmin.Telegram.Models.BotProtectionConfig` or `InviteCommandConfig` → change `using` to `TelegramGroupsAdmin.Configuration.Models`.
- All Telegram-project files importing `TelegramGroupsAdmin.Core.Services.AI` → change `using` to `TelegramGroupsAdmin.AI.Services`.

- [ ] **Step 9: Migrate RuntimeLoggingService**

For `TelegramGroupsAdmin/Services/RuntimeLoggingService.cs:57,75,93`:
- `GetAsync<LogConfig>(ConfigType.Log, chatId: 0)` → `GetLogAsync(0)`.
- `SaveAsync(ConfigType.Log, ChatIdentity.FromId(0), config)` → `SaveLogAsync(ChatIdentity.FromId(0), config, Actor.System...)`. Pick the right system actor — log-level changes from runtime are typically `Actor.System` or a dedicated identifier. Use `Actor.FromSystem("runtime_logging")` if no constant matches.
- `DeleteAsync(ConfigType.Log, ChatIdentity.FromId(0))` → `DeleteLogAsync(ChatIdentity.FromId(0), Actor.FromSystem("runtime_logging"))`.

- [ ] **Step 10: Update component test mocks**

For each file:
- `WelcomeSystemConfigTests.cs`
- `BotGeneralSettingsTests.cs`
- `BanCelebrationSettingsTests.cs`
- `BanCelebrationChatSettingsTests.cs`
- `ServiceMessageDeletionSettingsTests.cs`
- `ExamReviewCardTests.cs`

…replace mock setups of the form `ConfigService.GetAsync<WelcomeConfig>(...)` → `ConfigService.GetWelcomeAsync(...)`, and `ConfigService.SaveAsync(...)` → `ConfigService.SaveWelcomeAsync(...)` etc. For `BotGeneralSettingsTests.cs:37,140,156`: `ConfigService.GetTelegramBotTokenAsync()` → `ConfigService.GetBotTokenAsync()`.

The mock signature changes are mechanical. No behavior change to assert on.

- [ ] **Step 11: Add ConfigService integration test for audit emission**

Create `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs`. Wire up the full DI graph: `ConfigRepository` (real), `ContentDetectionConfigRepository` (real), `AuditService` (real, backed by `AuditLogRepository`), `HybridCache` (real in-memory).

```csharp
[Test]
public async Task SaveWelcomeAsync_AppendsAuditLogRow()
{
    var chat = new ChatIdentity(7777, "Test Chat");
    var config = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "hi" };
    var actor = Actor.FromWebUser("integration-test-user", "u@example.com");

    var before = await CountAuditLogsAsync();
    await _sut.SaveWelcomeAsync(chat, config, actor);
    var after = await CountAuditLogsAsync();

    Assert.That(after, Is.EqualTo(before + 1));
    var lastEntry = await GetLatestAuditLogAsync();
    Assert.Multiple(() =>
    {
        Assert.That(lastEntry.EventType, Is.EqualTo(AuditEventType.ConfigurationChanged));
        Assert.That(lastEntry.Value, Does.Contain("Welcome"));
        Assert.That(lastEntry.Value, Does.Contain("Test Chat"));
    });
}

// Add the analogous test for the other 7 configs.
```

- [ ] **Step 12: Run full unit + integration test suite**

Run: `dotnet test TelegramGroupsAdmin.sln --filter "FullyQualifiedName~Configuration|FullyQualifiedName~ConfigService"` (background recommended; full suite ~20min).

Expected: all new tests pass; existing tests pass.

- [ ] **Step 13: Verify the full build is green**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: build succeeds, zero warnings (`TreatWarningsAsErrors=true` is set on every project).

- [ ] **Step 14: Smoke-pass startup with --migrate-only**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: app initializes, runs migrations, exits cleanly. (This is the safe "DI graph resolves" check per the project's "NEVER run the app normally" rule.)

- [ ] **Step 15: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(config): rewire ConfigService to typed surface with audit, update consumers

ConfigService body rewritten to delegate to typed IConfigRepository
methods, emit AuditEventType.ConfigurationChanged on every save/delete,
and invalidate cache per-config. IDataProtectionProvider dependency
dropped (encryption now lives in repo). IAuditService dependency added.

IConfigService rewritten to typed surface (8 configs × {Get, GetEffective,
Save, Delete} + bot token). ConfigType-based generic API retired
entirely. Save/Delete signatures take ChatIdentity + Actor for log
context and audit attribution.

Consumer sweep:
- Razor settings pages migrated to typed methods + WebUser!.ToActor().
- Background jobs migrated to typed reads.
- Telegram services / handlers migrated to typed methods.
- BotProtectionConfig and InviteCommandConfig consumers updated to use
  the new TelegramGroupsAdmin.Configuration.Models namespace.
- AI consumers updated to use TelegramGroupsAdmin.AI.Services namespace
  (resolves commit 2's deferred consumer updates).
- ContentDetection access via MessageEditProcessor switched to direct
  IContentDetectionConfigRepository injection (no longer routes through
  the generic IConfigService API).
- RuntimeLoggingService uses Actor.FromSystem("runtime_logging") for
  audit attribution on log-config mutations.
- Component test mocks updated to typed-method signatures.

Tests added:
- ConfigServiceTests (NSubstitute-based unit tests for delegate/audit/
  cache-invalidation paths, including bot-token plaintext-not-in-audit).
- ConfigServiceIntegrationTests (real DI graph + real audit_logs table
  growth assertion per config).

Smoke: dotnet run --migrate-only succeeds. PR is buildable from this
commit forward.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Task 7: Retire dead types and column-routing scaffolding

**Commit:** `chore(config): retire dead config types and column-routing scaffolding`

Final cleanup commit. Deletes `ConfigType`, `ConfigRecord`, the old anemic `IConfigRepository` methods, and any `*ConfigData` / `*ConfigMappings` files that ended up unused. Build stays **green**.

**Files (deletes):**
- `TelegramGroupsAdmin.Configuration/ConfigType.cs`
- `TelegramGroupsAdmin.Configuration/Models/ConfigRecord.cs`

**Files (modifies):**
- `TelegramGroupsAdmin.Configuration/Repositories/IConfigRepository.cs` (drop anemic methods)
- `TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs` (drop the corresponding implementations)

- [ ] **Step 1: Verify ConfigType has zero remaining usages**

Run: `Grep` with pattern `\bConfigType\.` across the repo (exclude `bin/`, `obj/`, `Migrations/`).
Expected: zero matches. If any remain, return to commit 6 and fix them — do NOT delete the enum yet.

- [ ] **Step 2: Delete ConfigType.cs**

```bash
git rm TelegramGroupsAdmin.Configuration/ConfigType.cs
```

- [ ] **Step 3: Verify ConfigRecord has zero remaining usages**

Run: `Grep` for `\bConfigRecord\b` (model, not Dto). Should match only the file itself.

- [ ] **Step 4: Delete the unused ConfigRecord.cs model**

```bash
git rm TelegramGroupsAdmin.Configuration/Models/ConfigRecord.cs
```

- [ ] **Step 5: Verify the old anemic IConfigRepository methods have zero callers (besides the ConfigRepository itself)**

Run: `Grep` patterns:
- `IConfigRepository.*\.GetAsync\(`
- `IConfigRepository.*\.UpsertAsync\(`
- `_configRepository\.GetAsync\(`
- `_configRepository\.UpsertAsync\(`
- `configRepository\.GetAsync\(`
- `configRepository\.UpsertAsync\(`

Expected: only matches inside `ConfigRepository.cs` itself (where the methods are defined). If `SystemConfigRepository` still uses `context.Configs` directly, that's fine. If anything else still calls `IConfigRepository.GetAsync(long)`, fix that consumer to use a typed method first — do NOT delete the methods yet.

- [ ] **Step 6: Drop the anemic methods from IConfigRepository**

Edit `TelegramGroupsAdmin.Configuration/Repositories/IConfigRepository.cs`. Remove:

```csharp
Task<ConfigRecordDto?> GetAsync(long chatId, CancellationToken cancellationToken = default);
Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default);
Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);
```

(Keep `SaveInviteLinkAsync` / `ClearInviteLinkAsync` / `ClearAllInviteLinksAsync` — those are real domain methods, not the anemic CRUD shape.)

- [ ] **Step 7: Drop the corresponding implementations from ConfigRepository**

Edit `TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs`. Remove the same four methods.

- [ ] **Step 8: Verify no `*ConfigData` or `*ConfigMappings` files are now orphaned**

Run: `Grep` for each `*ConfigData` and `*ConfigMappings` type name to confirm they have ≥ 1 caller. If any has zero callers, delete it.

(Note: AI mapping types like `AIConnectionData` / `AIFeatureConfigData` may have zero direct callers in this PR's scope but be consumed by the `AIProviderConfig` DTO via JSON deserialization — verify by inspecting `AIProviderConfig.cs` before deleting.)

- [ ] **Step 9: Run the full test suite**

Run: `dotnet test TelegramGroupsAdmin.sln --logger "trx" --results-directory ./TestResults` (background recommended).
Expected: full suite passes. Note any flaky tests for follow-up (out of scope for this PR).

- [ ] **Step 10: Final smoke-pass with --migrate-only**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: clean exit.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -F- <<'EOF'
chore(config): retire dead config types and column-routing scaffolding

Delete ConfigType enum (no callers remain after commit 6's typed-method
sweep). Delete the unused ConfigRecord business model. Drop the anemic
GetAsync(long) / UpsertAsync(ConfigRecordDto) / DeleteAsync(long) /
GetByChatIdAsync methods from IConfigRepository — these were retained
in commit 4 only to bridge ConfigService's transition to typed methods.

Repository surface is now domain-typed end-to-end with no DTO leakage.

Refs spec: docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md
EOF
```

---

## Final Verification Before PR

After all 7 commits land:

- [ ] **Acceptance criteria sweep**

Walk every box in the spec's "Acceptance Criteria" section (lines 514-541) and verify each:

1. `Core.csproj` no longer references `Configuration` — check the csproj.
2. `Configuration.csproj` references `Core` — check the csproj.
3. `TelegramGroupsAdmin.AI` project exists at peer level — check the .sln.
4. `IConfigService` and `ConfigService` live at `Configuration/Services/` — `ls` the folder.
5. All 7 AI service files at `AI/Services/` — `ls` the folder.
6. `IConfigRepository` exposes typed methods — read the interface file.
7. `ConfigService` constructor no longer injects `IDataProtectionProvider` — read constructor.
8. `ConfigService` constructor injects `IAuditService` — read constructor.
9. `ConfigType` enum is deleted — `git ls-files | grep ConfigType` returns nothing.
10. All 8 configs flow through their `*ConfigMappings` at runtime — verified by integration tests passing.
11. Every save/delete on `IConfigService` emits an `AuditEventType.ConfigurationChanged` event — verified by `ConfigServiceIntegrationTests`.
12. All 8 configs have round-trip mapping unit tests — `ls TelegramGroupsAdmin.UnitTests/Configuration/`.
13. All 8 configs have integration tests — `ls TelegramGroupsAdmin.IntegrationTests/Configuration/`.
14. Bot-token integration test verifies encryption at rest — verified.
15. Pre-existing E2E test suite passes — run `dotnet test TelegramGroupsAdmin.E2ETests/` (background; long-running).
16. PR final state builds green — `dotnet build TelegramGroupsAdmin.sln` succeeds with zero warnings.

- [ ] **Open the PR against `develop`**

Per the project's CLAUDE.md: never PR feature branches to `master`. Always PR to `develop`. Include `Closes #341`, `Closes #342`, `Closes #453` at the top of the PR body. Reference (don't close) #458.

```bash
gh pr create --base develop --title "refactor(config): restore Core/Configuration layering + relocate AI services" --body-file <(cat <<'EOF'
Closes #341
Closes #342
Closes #453

## Summary

Restores project-layer purity by:
- Inverting the Core ↔ Configuration project reference (Core no longer references Configuration).
- Relocating ConfigService and AI services out of Core into their proper homes (Configuration and a new AI project).
- Expanding IConfigRepository to typed methods that own JSON serialization, mapping, encryption, column dispatch, and per-field merge end-to-end.
- Wiring the previously-dead mapping layer for all 8 configs flowing through ConfigService.
- Moving bot token encryption from the service layer into the repository.
- Emitting AuditEventType.ConfigurationChanged on every config save/delete with the threaded Actor.

## Test Plan

- [ ] All unit tests pass (~80 added)
- [ ] All integration tests pass (~30 added; full PostgreSQL via TestContainers)
- [ ] Component tests pass (mock signatures updated)
- [ ] E2E tests pass (smoke-only — no functional changes through the UI)
- [ ] `dotnet run --migrate-only` succeeds (DI graph resolution check)

## References

- Spec: `docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md`
- Plan: `docs/superpowers/plans/2026-04-25-config-and-ai-relocation.md`
- Future work: #458 (DB-side merge optimization)
EOF
)
```

---

## Self-Review Notes

The plan has been checked against the spec. Coverage:

- **Spec sections covered:** Problem Statement (addressed by Tasks 1-7); Goals (addressed: project purity by 1+5, boundary rule by 4+6, mapping wiring by 3+4, bot token encryption by 4, type-safe API by 2+4+6, audit by 6, test coverage by 3+4+6); Non-Goals (respected — no in-JSON encryption, no DB-side merge, no Moderation column split, no `ConfigType` backward compat, no AI internal refactors); Target Architecture (final csproj edges land in commits 1, 2, 5; layering rules enforced); Domain Surface (IConfigRepository in commit 4, IConfigService in commit 6); Repository Internals (commit 4); Service Internals (commit 6); Test Strategy (Layers 1-3 covered; Layer 4 E2E is smoke-pass only per spec); Commit Sequence (matches the spec's 7-commit table exactly); Acceptance Criteria (all 16 items mapped to verification steps); Folds and Spawns (PR closes #341/#342/#453, references #458).

- **Spec gap noted and folded:** `BotProtectionConfig` and `InviteCommandConfig` model relocations from `Telegram/Models/` to `Configuration/Models/` were not explicitly scheduled in the spec's commit table but are required (the typed `IConfigRepository` methods must return them, and `Configuration` cannot reference `Telegram`). Folded into commit 3 per user direction.

- **Type/signature consistency:** Verified that `GetWelcomeAsync` / `SaveWelcomeAsync` / `DeleteWelcomeAsync` / `GetEffectiveWelcomeAsync` names appear consistently in IConfigRepository (Task 4), IConfigService (Task 6), the consumer sweep (Task 6.6-6.9), and the test classes (Task 4 + Task 6). Same for the other 7 configs by template substitution.

- **Placeholder scan:** Plan contains no `TBD`, `implement later`, "similar to Task N", or other deferred-work shorthand. Where the engineer must repeat a template (e.g., 7 more six-method blocks in ConfigService, 7 more four-method blocks in ConfigRepository), the canonical block is shown in full and the substitution targets are enumerated.
