# Dual Spam Detection Engine Architecture - Research & Design

## Executive Summary

**Goal**: Implement new spam detection engine alongside existing ContentDetectionEngine with zero-impact on current code and instant rollback via simple config flag.

**Current State**: Interface-based architecture is ALREADY in place with excellent separation of concerns.

**Complexity Assessment**:
- **Infrastructure Changes**: Very Low (mostly copy-paste + 30-40 LOC)
- **Risk Level**: Low (no breaking changes, pure additive)
- **Time to Setup**: 1-2 hours (excludes new engine logic)
- **Rollback**: Single-line config change

---

## Part 1: Current Architecture (As-Is)

### 1.1 Interface Definition (Already Exists!)

**Location**: `/TelegramGroupsAdmin.ContentDetection/Services/IContentDetectionEngine.cs`

```csharp
public interface IContentDetectionEngine
{
    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// </summary>
    Task<ContentDetectionResult> CheckMessageAsync(
        ContentCheckRequest request, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// Used internally for two-tier decision system (veto mode)
    /// </summary>
    Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(
        ContentCheckRequest request, 
        CancellationToken cancellationToken = default);
}
```

**Result Contract**: `ContentDetectionResult`

```csharp
public record ContentDetectionResult
{
    public bool IsSpam { get; init; }                           // Overall spam determination
    public int MaxConfidence { get; init; }                     // Highest confidence from checks
    public int AvgConfidence { get; init; }                     // Average from spam-flagging checks
    public int SpamFlags { get; init; }                         // Number of spam-flagging checks
    public int NetConfidence { get; init; }                     // Weighted voting score
    public List<ContentCheckResponse> CheckResults { get; init; }  // Individual check results
    public string PrimaryReason { get; init; }                  // Why flagged as spam
    public SpamAction RecommendedAction { get; init; }          // Recommended action (Allow/ReviewQueue/AutoBan)
    public bool ShouldVeto { get; init; }                       // Should submit to OpenAI veto
    public Models.HardBlockResult? HardBlock { get; init; }     // Hard-blocked URL result
}
```

### 1.2 Current Registration (Scoped)

**Location**: `/TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs:23`

```csharp
public static IServiceCollection AddContentDetection(this IServiceCollection services)
{
    // Register main spam detection engine (loads config from repository dynamically)
    services.AddScoped<IContentDetectionEngine, ContentDetectionEngine>();
    
    // ... all individual IContentCheck implementations ...
    services.AddScoped<IContentCheck, Checks.StopWordsSpamCheck>();
    services.AddScoped<IContentCheck, Checks.CasSpamCheck>();
    services.AddScoped<IContentCheck, Checks.SimilaritySpamCheck>();
    // etc (11 total check implementations)
    
    // ... repositories, utilities ...
}
```

### 1.3 Where It's Used (Single Injection Point)

**Location**: `/TelegramGroupsAdmin.Telegram/Services/ContentCheckCoordinator.cs:18`

```csharp
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly IContentDetectionEngine _spamDetectionEngine;
    
    public ContentCheckCoordinator(
        IContentDetectionEngine spamDetectionEngine,
        IServiceProvider serviceProvider,
        ILogger<ContentCheckCoordinator> logger)
    {
        _spamDetectionEngine = spamDetectionEngine;  // <- Only one injection point!
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        // ...
        var fullResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);
        // ...
    }
}
```

**Call Chain**: 
- Bot message → `ContentDetectionOrchestrator.RunDetectionAsync()` → `ContentCheckCoordinator.CheckAsync()` → `IContentDetectionEngine.CheckMessageAsync()`

### 1.4 Configuration Storage

**Database Table**: `configs` (JSONB column: `spam_detection_config`)

**Loaded By**: `SpamDetectionConfigRepository.GetEffectiveConfigAsync(chatId, cancellationToken)`

**Approach**: 
- Per-chat config (if exists)
- Falls back to global config (chat_id = 0)
- JSON deserialized to `SpamDetectionConfig` object

**Config Class**: `SpamDetectionConfig`
- Contains per-check configuration (enabled flag, thresholds)
- Feature flags: `TrainingMode`, `FirstMessageOnly`, etc.
- Threshold settings: `AutoBanThreshold`, `ReviewQueueThreshold`, etc.

---

## Part 2: Proposed Dual-Engine Architecture

### 2.1 Strategy: Factory Pattern + Configuration Flag

**Approach**: 
- Keep `ContentDetectionEngine` untouched (legacy)
- Create new `ContentDetectionEngineV2` (new implementation)
- Both implement `IContentDetectionEngine`
- Use **Factory Pattern** to select which implementation to use
- Configuration flag: `EngineVersion` in `SpamDetectionConfig`

**Why Factory?**
- Single DI registration point
- Config-driven behavior (can switch per-chat or globally)
- Deterministic behavior (no reflection, no magic)
- Easy to A/B test (per-chat switching)
- Immediate rollback (flip config flag, no restart needed)

---

### 2.2 Step 1: Add Engine Version to Config

**File**: `/TelegramGroupsAdmin.ContentDetection/Configuration/SpamDetectionConfig.cs`

**Change**: Add property to `SpamDetectionConfig` class (around line 6-50)

```csharp
namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Configuration for spam detection, based on tg-spam's configuration structure
/// </summary>
public class SpamDetectionConfig
{
    /// <summary>
    /// Spam detection engine version to use
    /// v1 = Original ContentDetectionEngine (legacy)
    /// v2 = New/experimental ContentDetectionEngine
    /// Default: v1 (for backward compatibility)
    /// </summary>
    public string EngineVersion { get; set; } = "v1";  // <- ADD THIS
    
    /// <summary>
    /// Enable auto-whitelisting after users prove themselves with non-spam messages...
    /// </summary>
    public bool FirstMessageOnly { get; set; } = true;
    
    // ... rest of existing config properties ...
}
```

**Database Migration**: The JSON column will handle this automatically (new property in JSON = no migration needed until you need strict schema).

### 2.3 Step 2: Create Factory Interface & Implementation

**New File**: `/TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineFactory.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Factory to select spam detection engine implementation based on config
/// Allows easy switching between engine versions for A/B testing or rollback
/// </summary>
public interface IContentDetectionEngineFactory
{
    /// <summary>
    /// Get the appropriate engine implementation based on config
    /// </summary>
    Task<IContentDetectionEngine> GetEngineAsync(
        long chatId, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory implementation that loads engine version from spam detection config
/// </summary>
public class ContentDetectionEngineFactory : IContentDetectionEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly ILogger<ContentDetectionEngineFactory> _logger;

    public ContentDetectionEngineFactory(
        IServiceProvider serviceProvider,
        ISpamDetectionConfigRepository configRepository,
        ILogger<ContentDetectionEngineFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configRepository = configRepository;
        _logger = logger;
    }

    public async Task<IContentDetectionEngine> GetEngineAsync(
        long chatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config for this chat
            var config = await _configRepository.GetEffectiveConfigAsync(chatId, cancellationToken);

            // Determine which engine version to use
            var engineVersion = config.EngineVersion ?? "v1";  // Default to v1 if not set

            _logger.LogDebug(
                "Selecting spam detection engine version '{EngineVersion}' for chat {ChatId}",
                engineVersion,
                chatId);

            // Get appropriate implementation from DI container
            var engine = engineVersion.ToLowerInvariant() switch
            {
                "v2" => _serviceProvider.GetRequiredService<ContentDetectionEngineV2>(),
                "v1" or _ => _serviceProvider.GetRequiredService<ContentDetectionEngine>(),
            };

            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting spam detection engine for chat {ChatId}, falling back to v1", chatId);
            // Safe fallback to v1
            return _serviceProvider.GetRequiredService<ContentDetectionEngine>();
        }
    }
}
```

### 2.4 Step 3: Create New Engine Implementation

**New File**: `/TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs`

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// NEW: Experimental spam detection engine with [YOUR IMPROVEMENTS HERE]
/// Implements same IContentDetectionEngine contract as v1 for drop-in compatibility
/// </summary>
public class ContentDetectionEngineV2 : IContentDetectionEngine
{
    private readonly ILogger<ContentDetectionEngineV2> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IFileScanningConfigRepository _fileScanningConfigRepo;
    private readonly IEnumerable<IContentCheck> _spamChecks;
    private readonly IOpenAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly SpamDetectionOptions _spamDetectionOptions;

    public ContentDetectionEngineV2(
        ILogger<ContentDetectionEngineV2> logger,
        ISpamDetectionConfigRepository configRepository,
        IFileScanningConfigRepository fileScanningConfigRepo,
        IEnumerable<IContentCheck> spamChecks,
        IOpenAITranslationService translationService,
        IUrlPreFilterService preFilterService,
        IOptions<SpamDetectionOptions> spamDetectionOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _fileScanningConfigRepo = fileScanningConfigRepo;
        _spamChecks = spamChecks;
        _translationService = translationService;
        _preFilterService = preFilterService;
        _spamDetectionOptions = spamDetectionOptions.Value;
    }

    public async Task<ContentDetectionResult> CheckMessageAsync(
        ContentCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "V2 Engine: Running spam detection for user {UserId} in chat {ChatId}",
            request.UserId,
            request.ChatId);

        // TODO: Implement your new engine logic here
        // For now, delegate to v1 to ensure backward compatibility during development
        throw new NotImplementedException("V2 engine implementation pending");
    }

    public async Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(
        ContentCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement your new engine logic here
        throw new NotImplementedException("V2 engine implementation pending");
    }
}
```

### 2.5 Step 4: Update DI Registration

**File**: `/TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs:23`

**Change**: Register factory and both implementations

```csharp
public static IServiceCollection AddContentDetection(this IServiceCollection services)
{
    // Register BOTH engine implementations (keyed or by type)
    services.AddScoped<ContentDetectionEngine>();           // v1: Original (still directly injectable)
    services.AddScoped<ContentDetectionEngineV2>();         // v2: New experimental

    // Register factory to select which implementation to use
    services.AddScoped<IContentDetectionEngineFactory, ContentDetectionEngineFactory>();

    // BREAKING: ContentCheckCoordinator now injects factory instead of engine directly
    // But this is INTERNAL to ContentDetection lib, not breaking for callers

    // ... all other existing registrations unchanged ...
    services.AddScoped<ITokenizerService, TokenizerService>();
    services.AddScoped<IOpenAITranslationService, OpenAITranslationService>();
    // ... etc ...
}
```

### 2.6 Step 5: Update ContentCheckCoordinator to Use Factory

**File**: `/TelegramGroupsAdmin.Telegram/Services/ContentCheckCoordinator.cs`

**Before**:
```csharp
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly IContentDetectionEngine _spamDetectionEngine;

    public ContentCheckCoordinator(
        IContentDetectionEngine spamDetectionEngine,
        IServiceProvider serviceProvider,
        ILogger<ContentCheckCoordinator> logger)
    {
        _spamDetectionEngine = spamDetectionEngine;
        // ...
    }

    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        // ...
        var fullResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);
        // ...
    }
}
```

**After**:
```csharp
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly IContentDetectionEngineFactory _engineFactory;  // <- Changed
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentCheckCoordinator> _logger;

    public ContentCheckCoordinator(
        IContentDetectionEngineFactory engineFactory,         // <- Changed
        IServiceProvider serviceProvider,
        ILogger<ContentCheckCoordinator> logger)
    {
        _engineFactory = engineFactory;                       // <- Changed
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        // ... existing logic ...

        // Get the appropriate engine based on config
        var spamDetectionEngine = await _engineFactory.GetEngineAsync(request.ChatId, cancellationToken);

        // Use selected engine
        var fullResult = await spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);
        
        // ... rest of existing logic ...
    }
}
```

---

## Part 3: Current State & Wire-up Summary

### 3.1 Current Registration (Line-by-line)

**Interface**: Already exists at `/TelegramGroupsAdmin.ContentDetection/Services/IContentDetectionEngine.cs`

**Implementation**: `ContentDetectionEngine` at `/TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngine.cs:21`

**DI Registration**: Line 23 in `/TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs`

```csharp
services.AddScoped<IContentDetectionEngine, ContentDetectionEngine>();
```

**Scope**: **Scoped** (new instance per HTTP request/service scope)

**Constructor Dependencies** (ContentDetectionEngine):
1. `ILogger<ContentDetectionEngine>` - logging
2. `ISpamDetectionConfigRepository` - load config from DB
3. `IFileScanningConfigRepository` - load OpenAI config from DB
4. `IEnumerable<IContentCheck>` - all 11 spam check implementations
5. `IOpenAITranslationService` - translate foreign languages
6. `IUrlPreFilterService` - check hard-blocked URLs
7. `IOptions<SpamDetectionOptions>` - VirusTotal API key, timeouts

### 3.2 Who Injects ContentDetectionEngine

**ONLY ONE place**:
- `ContentCheckCoordinator` at `/TelegramGroupsAdmin.Telegram/Services/ContentCheckCoordinator.cs:18`

This is injected through its constructor:
```csharp
public ContentCheckCoordinator(
    IContentDetectionEngine spamDetectionEngine,
    IServiceProvider serviceProvider,
    ILogger<ContentCheckCoordinator> logger)
```

### 3.3 Call Chain

```
Telegram Bot Message
    ↓
MessageProcessingService.ProcessNewMessageAsync()
    ↓
ContentDetectionOrchestrator.RunDetectionAsync()
    ↓
ContentCheckCoordinator.CheckAsync()
    ↓
IContentDetectionEngine.CheckMessageAsync()  ← Factory selects v1 or v2 here
    ↓
ContentDetectionResult (same for both versions)
```

### 3.4 Configuration Storage & Access

**Database Table**: `configs` 
**Column**: `spam_detection_config` (JSONB)
**Chat Scope**: Per-chat override or global (chat_id = 0)

**Loaded By**: `SpamDetectionConfigRepository.GetEffectiveConfigAsync()`

```csharp
public async Task<SpamDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken)
{
    // Load per-chat config if exists, fall back to global
    // Deserialize JSON to SpamDetectionConfig object
    return config;
}
```

**Access Pattern**: Config loaded fresh on every message (no caching at engine level, by design for hot-reload support)

---

## Part 4: Rollback Plan

### 4.1 Instant Rollback (At Runtime)

**Single Change**: Update database config entry

```sql
-- Rollback all chats to v1 instantly (no restart)
UPDATE configs 
SET spam_detection_config = jsonb_set(
    spam_detection_config,
    '{EngineVersion}',
    '"v1"'
)
WHERE chat_id = 0;  -- Global config

-- Or rollback specific chat
UPDATE configs 
SET spam_detection_config = jsonb_set(
    spam_detection_config,
    '{EngineVersion}',
    '"v1"'
)
WHERE chat_id = <YOUR_CHAT_ID>;
```

**UI-Based Rollback**: Add dropdown in Settings → Spam Detection → Engine Version → [v1 / v2]

**No restart required**: Config is loaded fresh per-message

### 4.2 Code-Level Rollback (If Needed)

If V2 has critical bug and you need immediate code rollback:

```bash
# 1. Delete ContentDetectionEngineV2.cs
rm TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs

# 2. Remove V2 registration from ServiceCollectionExtensions.cs
# (Remove: services.AddScoped<ContentDetectionEngineV2>();)

# 3. Update factory to never select V2
# (Change switch to only return v1)

# 4. Optionally remove factory entirely (but no need, it works fine)

# 5. Rebuild and deploy
dotnet publish
```

### 4.3 Safety Guarantees

**Old Code Completely Untouched**: 
- `ContentDetectionEngine.cs` - zero changes
- `IContentDetectionEngine.cs` - zero changes
- Individual checks - zero changes
- Repositories - zero changes

**Only Changes**:
1. Add `EngineVersion` property to `SpamDetectionConfig` (additive, default "v1")
2. Add factory interface & class
3. Add `ContentDetectionEngineV2` (new file)
4. Update `ContentCheckCoordinator` to use factory instead of direct injection
5. Update ServiceCollectionExtensions to register factory & V2

**Backward Compatibility**:
- If `EngineVersion` not in config → defaults to "v1"
- If V2 registration missing → factory throws exception, caught, falls back to v1
- Same `ContentDetectionResult` contract for both → no caller changes

---

## Part 5: Detailed Implementation Plan

### 5.1 Files to Create (3 new files)

1. **`ContentDetectionEngineFactory.cs`** (60 LOC)
   - Interface: `IContentDetectionEngineFactory`
   - Implementation: selects v1 or v2 based on config

2. **`ContentDetectionEngineV2.cs`** (50+ LOC, pending your logic)
   - Stub implementation of IContentDetectionEngine
   - Ready for your experimental logic

3. **Migration Script** (optional, for strict schema tracking)
   - Add `EngineVersion` column to `spam_detection_configs` table (if using that table)
   - Or rely on JSONB schema flexibility (recommended)

### 5.2 Files to Modify (3 files)

1. **`SpamDetectionConfig.cs`** (1 line added)
   ```csharp
   public string EngineVersion { get; set; } = "v1";
   ```

2. **`ServiceCollectionExtensions.cs`** (2 lines added)
   ```csharp
   services.AddScoped<ContentDetectionEngine>();
   services.AddScoped<ContentDetectionEngineV2>();
   services.AddScoped<IContentDetectionEngineFactory, ContentDetectionEngineFactory>();
   ```

3. **`ContentCheckCoordinator.cs`** (constructor & 1 method call updated)
   - Inject `IContentDetectionEngineFactory` instead of `IContentDetectionEngine`
   - Call `await _engineFactory.GetEngineAsync(request.ChatId, cancellationToken)` before checking

### 5.3 Lines of Code Estimate

| File | Type | LOC | Impact |
|------|------|-----|--------|
| ContentDetectionEngineFactory.cs | New | 65 | Core factory logic |
| ContentDetectionEngineV2.cs | New | 55+ | Stub + your logic |
| SpamDetectionConfig.cs | Modify | +1 | Config property |
| ServiceCollectionExtensions.cs | Modify | +3 | Register factory & V2 |
| ContentCheckCoordinator.cs | Modify | +5 | Use factory |
| **TOTAL** | | **~75** | **Very light footprint** |

**Excludes**: New engine implementation logic (depends on your requirements)

---

## Part 6: A/B Testing & Monitoring

### 6.1 Per-Chat Switching

You can enable V2 for specific chats to test:

```sql
-- Enable V2 for one chat (testing)
UPDATE configs 
SET spam_detection_config = jsonb_set(
    spam_detection_config,
    '{EngineVersion}',
    '"v2"'
)
WHERE chat_id = <TEST_CHAT_ID>;

-- Enable V2 globally
UPDATE configs 
SET spam_detection_config = jsonb_set(
    spam_detection_config,
    '{EngineVersion}',
    '"v2"'
)
WHERE chat_id = 0;
```

### 6.2 Monitoring & Logging

Factory logs which engine is selected:

```csharp
_logger.LogDebug(
    "Selecting spam detection engine version '{EngineVersion}' for chat {ChatId}",
    engineVersion,
    chatId);
```

Both engines can log their own metrics:

```csharp
_logger.LogInformation("V2 Engine: Running spam detection for user {UserId}", request.UserId);
```

### 6.3 Metrics/Analytics

Add to `DetectionResult` storage (if needed):

```sql
-- Track which engine was used
ALTER TABLE detection_results ADD COLUMN engine_version VARCHAR(10) DEFAULT 'v1';
```

Then in `ContentDetectionOrchestrator.StoreDetectionResultAsync()`:

```csharp
detectionResult = new DetectionResultRecord
{
    // ... existing fields ...
    EngineVersion = engineVersion,  // <- Add this
};
```

---

## Part 7: Complexity Assessment

### 7.1 Risk Analysis

| Risk Factor | Level | Mitigation |
|------------|-------|-----------|
| Breaking existing code | Low | Only internal to ContentDetection lib; ContentCheckCoordinator is internal |
| Configuration migration | None | JSONB is schema-flexible; no migration needed |
| Database schema | None | Using JSONB column, schema already supports flexible config |
| Rollback difficulty | None | Single config flag, zero code changes in production |
| Performance impact | Low | One extra async call to factory (negligible) |

### 7.2 Time Estimate

| Task | Time | Notes |
|------|------|-------|
| Create factory interface & impl | 30 min | Straightforward pattern |
| Create V2 stub | 15 min | Copy v1, remove implementation |
| Update DI registration | 10 min | 3 lines |
| Update ContentCheckCoordinator | 20 min | Constructor + 1 call |
| Add config property | 5 min | Single line |
| Testing & validation | 30 min | Verify fallback, config loading |
| **Total (Infrastructure)** | **110 min (~2 hours)** | **Excludes new engine logic** |

### 7.3 New Engine Implementation

Once infrastructure is ready, you can implement V2 logic at your own pace:

```csharp
// Add to ContentDetectionEngineV2.cs
public async Task<ContentDetectionResult> CheckMessageAsync(
    ContentCheckRequest request,
    CancellationToken cancellationToken = default)
{
    // TODO: Your experimental logic here
    // - Different weighting algorithm?
    // - New check ordering?
    // - Parallel vs serial execution?
    // - Different aggregation strategy?
}
```

---

## Part 8: Example Scenarios

### 8.1 Testing a New Weighting Algorithm

1. In `ContentDetectionEngineV2`, override `AggregateResults()` with new algorithm
2. Set global config to `EngineVersion: "v2"`
3. Monitor detection results in test chat
4. If issues → Rollback by setting `EngineVersion: "v1"` (no restart)
5. Fix logic, redeploy V2
6. Re-enable `EngineVersion: "v2"`

### 8.2 Adding New Check to V2 Only

1. Create new `IContentCheck` implementation (e.g., `AdvancedSpamCheck`)
2. In V2, register and use it: `_spamChecks.FirstOrDefault(c => c.CheckName == "AdvancedSpam")`
3. V1 ignores it (it's just another check in the collection, v1 doesn't know about it)
4. Enable V2 for testing

### 8.3 Gradual Rollout

```
Day 1: V2 in one test chat → EngineVersion: "v2" for chat #123
Day 3: V2 in two test chats → EngineVersion: "v2" for chats #123, #456
Day 7: V2 globally → EngineVersion: "v2" for all (chat_id = 0)
Day 14: Monitor, no rollback needed → Keep V2, deprecate V1
```

---

## Part 9: Questions to Consider

Before implementing, answer these:

1. **What's the experimental logic?**
   - Different confidence aggregation?
   - New check ordering?
   - Parallel check execution?
   - Machine learning integration?

2. **Will V2 need new checks?**
   - If yes, register them as `IContentCheck` (both v1 and v2 can use)
   - Or keep new checks V2-only (register, then v1 ignores unknown types)

3. **Will V2 change the `ContentDetectionResult` contract?**
   - If yes, you'll need wrapper/adapter in factory to convert V2 result to contract
   - If no, drop-in replacement works

4. **Performance targets?**
   - Is V2 faster? Slower? Need monitoring?
   - Factory adds ~1ms per request (acceptable?)

5. **Gradual rollout or big-bang?**
   - Gradual (per-chat, then global) = safer
   - Big-bang (enable all at once) = faster, higher risk

---

## Part 10: Code Checklist

### Infrastructure Setup

- [ ] Create `IContentDetectionEngineFactory` interface
- [ ] Create `ContentDetectionEngineFactory` implementation
- [ ] Create `ContentDetectionEngineV2` stub class
- [ ] Add `EngineVersion` property to `SpamDetectionConfig`
- [ ] Register factory & V2 in `ServiceCollectionExtensions`
- [ ] Update `ContentCheckCoordinator` to use factory
- [ ] Add logging for engine selection
- [ ] Test fallback behavior (missing V2 config, missing registry)

### Validation

- [ ] V1 still works with config default ("v1")
- [ ] V2 selection works with config ("v2")
- [ ] Rollback to V1 works instantly
- [ ] Per-chat switching works
- [ ] Factory gracefully handles errors
- [ ] No breaking changes to external callers
- [ ] All tests pass

### Documentation

- [ ] Update BACKLOG.md with completed infrastructure
- [ ] Add inline comments explaining factory pattern
- [ ] Document V2 development process
- [ ] Update CI/CD if deploying new class

---

## Conclusion

**This is a straightforward architecture change with:**
- ✓ Interface already exists (IContentDetectionEngine)
- ✓ Single injection point (ContentCheckCoordinator)
- ✓ Configuration storage already in place
- ✓ Factory pattern is clean and maintainable
- ✓ Zero risk of breaking existing code
- ✓ Instant rollback via config flag
- ✓ Very light footprint (~75 LOC infrastructure)

**Ready to proceed?** Start with Step 1 (add `EngineVersion` property) and work through the checklist above.
