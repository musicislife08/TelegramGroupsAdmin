# TelegramGroupsAdmin Spam Detection Engine Architecture Analysis

## Executive Summary

**Current State:** The codebase has a well-designed, abstracted spam detection engine that already implements the Strategy pattern via interfaces. Implementing a dual-engine system is **moderately feasible** with minimal changes to the core architecture.

**Key Finding:** `IContentDetectionEngine` is already an interface with a single implementation (`ContentDetectionEngine`). The architecture is almost ready for multiple implementations—it just needs DI registration flexibility and a selection strategy.

**Estimated Effort:** 2-3 days (moderate complexity)
**Risk Level:** Low (no breaking changes required)
**Recommendation:** Implementable without major refactoring. Biggest task is designing the selection strategy.

---

## 1. Current Architecture Analysis

### 1.1 Interface Already Exists ✓

**File:** `/TelegramGroupsAdmin.ContentDetection/Services/IContentDetectionEngine.cs`

```csharp
public interface IContentDetectionEngine
{
    Task<ContentDetectionResult> CheckMessageAsync(
        ContentCheckRequest request, 
        CancellationToken cancellationToken = default);

    Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(
        ContentCheckRequest request, 
        CancellationToken cancellationToken = default);
}
```

**Status:** Perfect foundation for multiple implementations. The interface is:
- Minimal and focused (2 methods only)
- Async-first design
- Takes generic `ContentCheckRequest` (not engine-specific types)
- Returns generic `ContentDetectionResult` (works with any engine)

### 1.2 Current Implementation

**File:** `/TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngine.cs` (697 lines)

**Key Responsibilities:**
1. **Configuration Loading:** Fetches `SpamDetectionConfig` from database per-chat
2. **Orchestration:** Decides which checks run based on config + request properties
3. **Request Building:** Builds strongly-typed requests for each check
4. **Result Aggregation:** Combines results using weighted voting (net confidence = spam votes - ham votes)
5. **OpenAI Veto Logic:** Implements two-tier decision system with safety checks
6. **Pre-filtering:** URL hard-block check before running checks
7. **Preprocessing:** Message translation before spam checks

**Architecture Pattern:** Orchestrator pattern—the engine doesn't do spam detection itself; it coordinates 13 individual checks.

### 1.3 DI Registration

**File:** `/TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs` (line 23)

```csharp
services.AddScoped<IContentDetectionEngine, ContentDetectionEngine>();
```

**Status:** Uses interface-based registration (✓ Good)
**Change Needed:** Currently hardcoded to single implementation

---

## 2. Dependency Analysis

### 2.1 Engine Dependencies (Constructor Injection)

```csharp
public ContentDetectionEngine(
    ILogger<ContentDetectionEngine> logger,
    ISpamDetectionConfigRepository configRepository,
    IFileScanningConfigRepository fileScanningConfigRepo,
    IEnumerable<IContentCheck> spamChecks,              // ← Critical: injected checks
    IOpenAITranslationService translationService,
    IUrlPreFilterService preFilterService,
    IOptions<SpamDetectionOptions> spamDetectionOptions)
```

**Key Insight:** The engine gets a **collection of `IContentCheck`** implementations:

```csharp
private readonly IEnumerable<IContentCheck> _spamChecks;
```

This is crucial—all checks are registered as implementations of `IContentCheck`:

```csharp
// From ServiceCollectionExtensions.cs
services.AddScoped<IContentCheck, Checks.InvisibleCharsSpamCheck>();
services.AddScoped<IContentCheck, Checks.StopWordsSpamCheck>();
services.AddScoped<IContentCheck, Checks.CasSpamCheck>();
services.AddScoped<IContentCheck, Checks.SimilaritySpamCheck>();
services.AddScoped<IContentCheck, Checks.BayesSpamCheck>();
services.AddScoped<IContentCheck, Checks.SpacingSpamCheck>();
services.AddScoped<IContentCheck, Checks.OpenAIContentCheck>();
services.AddScoped<IContentCheck, Checks.UrlBlocklistSpamCheck>();
services.AddScoped<IContentCheck, Checks.ThreatIntelSpamCheck>();
services.AddScoped<IContentCheck, Checks.ImageSpamCheck>();
services.AddScoped<IContentCheck, Checks.VideoSpamCheck>();
services.AddScoped<IContentCheck, Checks.FileScanningCheck>();
```

**Result:** Any engine can use the same check implementations. Checks are engine-agnostic.

### 2.2 Individual Check Interface

**File:** `/TelegramGroupsAdmin.ContentDetection/Abstractions/IContentCheck.cs`

```csharp
public interface IContentCheck
{
    CheckName CheckName { get; }
    
    ValueTask<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request);
    
    bool ShouldExecute(ContentCheckRequest request);
}
```

**Status:** Already abstracted ✓
- Checks don't know which engine uses them
- Checks are data-driven (config comes in request)
- Each check returns `ContentCheckResponse` (generic output)

### 2.3 Service Dependencies Reusability

All engine dependencies are interfaces, not concrete classes:
- `ISpamDetectionConfigRepository` ✓ Reusable
- `IFileScanningConfigRepository` ✓ Reusable
- `IOpenAITranslationService` ✓ Reusable
- `IUrlPreFilterService` ✓ Reusable
- `IEnumerable<IContentCheck>` ✓ Reusable

**Complexity:** **TRIVIAL** — No service dependencies need to change.

---

## 3. Configuration Patterns

### 3.1 Current Configuration Model

**File:** `/TelegramGroupsAdmin.ContentDetection/Configuration/SpamDetectionConfig.cs` (116 lines)

```csharp
public class SpamDetectionConfig
{
    public bool FirstMessageOnly { get; set; } = true;
    public int FirstMessagesCount { get; set; } = 3;
    public int MinMessageLength { get; set; } = 10;
    public int AutoBanThreshold { get; set; } = 80;
    public int ReviewQueueThreshold { get; set; } = 50;
    public int MaxConfidenceVetoThreshold { get; set; } = 85;
    public bool TrainingMode { get; set; } = false;
    
    public StopWordsConfig StopWords { get; set; } = new();
    public SimilarityConfig Similarity { get; set; } = new();
    public CasConfig Cas { get; set; } = new();
    public BayesConfig Bayes { get; set; } = new();
    // ... 9 more check configs
}
```

**Database Storage:** 
```csharp
[Table("spam_detection_configs")]
public class SpamDetectionConfigRecordDto
{
    [Column("config_json")]
    public string ConfigJson { get; set; } = string.Empty;  // Stored as JSON
    
    [Column("chat_id")]
    public long? ChatId { get; set; }  // NULL = global config
}
```

### 3.2 Engine Selection Needs

**Current:** No engine selection mechanism exists
**Needed:** Add optional field to enable alternative engines

Options:
1. **Add to `SpamDetectionConfig`:**
   ```csharp
   public string DetectionEngine { get; set; } = "v1";  // or "v1", "v2", "experimental"
   ```
   Pros: Per-chat engine selection
   Pros: Stored with existing config
   Cons: Requires migration to add field

2. **Add to global `GeneralConfig`:**
   ```csharp
   public string SpamDetectionEngine { get; set; } = "v1";
   ```
   Pros: Global toggle (simpler for shadow mode testing)
   Cons: Can't do per-chat A/B testing

3. **Environment variable + feature flag:**
   Pros: No database migration
   Cons: Requires restart, less flexible

**Recommendation:** Option 1 (add to `SpamDetectionConfig`) for maximum flexibility.

### 3.3 Configuration Complexity

**Complexity:** **MODERATE** — Would need:
1. New field in `SpamDetectionConfig` (simple)
2. Database migration to add field (simple)
3. Settings UI to select engine (medium, already has precedent)
4. Repository logic to return correct engine (simple)

---

## 4. Integration Points (Where Engine is Used)

### 4.1 Primary Consumer: ContentCheckCoordinator

**File:** `/TelegramGroupsAdmin.Telegram/Services/ContentCheckCoordinator.cs`

```csharp
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly IContentDetectionEngine _spamDetectionEngine;

    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        // ... trust/admin checks ...
        
        var fullResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);
        
        // ... process critical violations ...
        
        return new ContentCheckCoordinatorResult { SpamResult = fullResult };
    }
}
```

**Status:** Only knows about `IContentDetectionEngine`, not the concrete implementation ✓

### 4.2 Secondary Consumers: ImageSpamCheck & VideoSpamCheck

**Files:** 
- `/TelegramGroupsAdmin.ContentDetection/Checks/ImageSpamCheck.cs`
- `/TelegramGroupsAdmin.ContentDetection/Checks/VideoSpamCheck.cs`

These checks **inject the engine itself** for OCR/frame extraction:

```csharp
// In ImageSpamCheck.cs
var ocrResult = await contentDetectionEngine.CheckMessageAsync(ocrRequest, req.CancellationToken);
```

**Status:** Uses interface `IContentDetectionEngine` ✓
**Implication:** Any engine implementation will work here too

### 4.3 Usage Pattern Summary

```
ContentCheckCoordinator
    ↓ (calls via IContentDetectionEngine)
IContentDetectionEngine
    ├─ Current: ContentDetectionEngine (v1)
    └─ Future: AlternativeContentDetectionEngine (v2, experimental, etc.)
        ↓ (coordinates)
    IContentCheck[] (13 check implementations)
        ├─ StopWordsSpamCheck
        ├─ BayesSpamCheck
        ├─ CasSpamCheck
        ├─ ... (others)
        └─ FileScanningCheck
```

**Dependency Coupling:** 
- ✓ ContentCheckCoordinator → `IContentDetectionEngine` (interface, good)
- ✓ Checks → `IContentCheck` (interface, good)
- Checks have NO coupling to engine implementation

**Complexity:** **TRIVIAL** — The interface boundary is already in place.

---

## 5. Feasibility Assessment

### 5.1 Creating a Second Engine Implementation

**Task Breakdown:**

| Task | Complexity | Time | Notes |
|------|-----------|------|-------|
| Create new class `AlternativeContentDetectionEngine : IContentDetectionEngine` | Trivial | 1-2 hours | Copy current engine, modify logic |
| Implement `CheckMessageAsync()` | Medium | 4-6 hours | Core logic changes vary by design |
| Implement `CheckMessageWithoutOpenAIAsync()` | Medium | 2-3 hours | Similar to above |
| Register in DI container (conditional) | Trivial | 15 minutes | Add factory or conditional registration |
| Add config field for engine selection | Trivial | 15 minutes | One property in `SpamDetectionConfig` |
| Database migration | Trivial | 15 minutes | Simple column addition |
| Tests for new engine | Medium | 2-3 days | Depends on test strategy |
| **Subtotal** | | **2-3 days** | |

### 5.2 DI Registration Strategy

**Option A: Factory Pattern (Recommended)**

```csharp
// In ServiceCollectionExtensions.cs
services.AddScoped<IContentDetectionEngine>(provider => 
{
    var config = provider.GetRequiredService<IOptionsSnapshot<SpamDetectionOptions>>();
    var engine = config.Value.Engine ?? "v1";
    
    return engine switch
    {
        "v1" => ActivatorUtilities.CreateInstance<ContentDetectionEngine>(provider),
        "v2" => ActivatorUtilities.CreateInstance<AlternativeContentDetectionEngine>(provider),
        "experimental" => ActivatorUtilities.CreateInstance<ExperimentalContentDetectionEngine>(provider),
        _ => throw new InvalidOperationException($"Unknown engine: {engine}")
    };
});
```

**Pros:**
- Single registration point
- Clean switch logic
- Can read from config

**Cons:**
- Requires `IOptionsSnapshot` (scoped option)
- Adds slight complexity

**Option B: Per-Request Selection**

```csharp
// In ContentCheckCoordinator
var engineName = request.DetectionEngineOverride ?? _defaultEngine;
var engine = _serviceProvider.GetKeyedService<IContentDetectionEngine>(engineName);
```

Uses .NET 8+ keyed services.

**Pros:** Per-request engine selection
**Cons:** Requires newer .NET, more verbose

**Option C: Repository-Based (Most Flexible)**

```csharp
// In ContentCheckCoordinator
var config = await _configRepository.GetEffectiveConfigAsync(request.ChatId);
var engineName = config.DetectionEngine; // "v1" or "v2" or "experimental"

var engine = engineName switch
{
    "v1" => _engineV1,
    "v2" => _engineV2,
    // ...
};

var result = await engine.CheckMessageAsync(request);
```

**Pros:**
- Per-chat engine selection
- No DI container complexity
- Config already flows to ContentCheckCoordinator

**Cons:**
- Manual switching logic
- Need to inject all engines

**Recommendation:** Use **Option C** (Repository-Based). Why?
1. `SpamDetectionConfig` is already per-chat
2. `ContentCheckCoordinator` already loads config
3. Allows shadow mode testing (run both engines, compare)
4. No factory pattern complexity

### 5.3 Can We Run Both Engines in Parallel (Shadow Mode)?

**YES** — This is ideal for validation:

```csharp
public async Task<ContentCheckCoordinatorResult> CheckAsync(SpamLibRequest request, CancellationToken cancellationToken)
{
    var config = await _configRepository.GetEffectiveConfigAsync(request.ChatId, cancellationToken);
    
    // Always run current engine
    var v1Result = await _engineV1.CheckMessageAsync(request, cancellationToken);
    
    // If enabled, run new engine in parallel for comparison
    ContentDetectionResult? v2Result = null;
    if (config.EnableShadowMode && config.ShadowEngine == "v2")
    {
        v2Result = await _engineV2.CheckMessageAsync(request, cancellationToken);
        
        // Log comparison for analytics
        await _analyticsRepository.LogEngineComparisonAsync(
            chatId: request.ChatId,
            userId: request.UserId,
            v1Result: v1Result,
            v2Result: v2Result,
            cancellationToken: cancellationToken);
    }
    
    // Always use primary engine result for action
    return new ContentCheckCoordinatorResult 
    { 
        SpamResult = v1Result,
        ShadowResult = v2Result  // For analytics/debugging
    };
}
```

**Shadow Mode Benefits:**
- Zero risk (secondary engine is ignored for actual decisions)
- Collect comparison metrics
- Validate new engine before switching
- Can track false positives vs. false negatives

**Complexity:** **MODERATE** — Adds analytics, but no breaking changes.

---

## 6. Migration Strategy

### 6.1 Phase 1: Foundation (1 day)

1. Create new engine class:
   ```
   TelegramGroupsAdmin.ContentDetection/Services/AlternativeContentDetectionEngine.cs
   ```

2. Add config field:
   ```csharp
   public class SpamDetectionConfig
   {
       public string DetectionEngine { get; set; } = "v1"; // NEW
       public bool ShadowModeEnabled { get; set; } = false; // NEW
       public string? ShadowEngine { get; set; } = null; // NEW
   }
   ```

3. Create migration:
   ```sql
   ALTER TABLE spam_detection_configs ADD COLUMN detection_engine VARCHAR(50) DEFAULT 'v1';
   ALTER TABLE spam_detection_configs ADD COLUMN shadow_mode_enabled BOOLEAN DEFAULT FALSE;
   ALTER TABLE spam_detection_configs ADD COLUMN shadow_engine VARCHAR(50);
   ```

4. Register both engines:
   ```csharp
   services.AddScoped<ContentDetectionEngine>();
   services.AddScoped<AlternativeContentDetectionEngine>();
   ```

### 6.2 Phase 2: Selection Logic (0.5 day)

Modify `ContentCheckCoordinator` to select engine:

```csharp
var engineName = config.DetectionEngine ?? "v1";
var engine = engineName switch 
{
    "v1" => _engineV1,
    "v2" => _engineV2,
    _ => _engineV1
};
```

### 6.3 Phase 3: Shadow Mode Testing (1 day)

1. Add analytics table:
   ```sql
   CREATE TABLE engine_comparison_logs (
       id BIGSERIAL PRIMARY KEY,
       chat_id BIGINT NOT NULL,
       user_id BIGINT NOT NULL,
       message_text TEXT,
       v1_result JSONB,
       v2_result JSONB,
       agreement BOOLEAN,
       confidence_delta INT,
       created_at TIMESTAMP DEFAULT NOW()
   );
   ```

2. Log comparisons in shadow mode
3. Query analytics to find discrepancies

### 6.4 Phase 4: Switchover (1 hour)

Update UI to allow per-chat engine selection, or:
- Set `DetectionEngine = "v2"` globally in database
- Monitor logs
- Rollback if issues (revert to "v1")

**Total Time:** ~3 days (if implementing new algorithm)
**Rollback Time:** ~15 minutes (database change + restart)

---

## 7. Risk Analysis

### 7.1 Low-Risk Factors ✓

1. **Interface Already Exists** — No breaking changes needed
2. **Checks Are Reusable** — Can share all 13 checks between engines
3. **Config Is Flexible** — Already supports per-chat configuration
4. **No Breaking Changes** — Both engines implement same interface
5. **Shadow Mode Possible** — Can validate before switching
6. **Easy Rollback** — Just change config field value

### 7.2 Potential Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| New engine produces different results | Medium | High | Shadow mode testing, comparison analytics |
| Performance degradation | Medium | Medium | Benchmark both engines, set timeouts |
| Regression in edge cases | Medium | High | Comprehensive test coverage, gradual rollout |
| Database migration on large DB | Low | Low | Run offline, test on staging first |
| Circular dependency in new engine | Low | High | Enforce same dependency pattern as v1 |

### 7.3 Mitigation Strategies

1. **Shadow Mode First** — Run both engines for 1-2 weeks, compare results
2. **Gradual Rollout** — Enable v2 in 1-2 test chats first
3. **Feature Flag** — Can disable new engine instantly via config change
4. **Monitoring** — Track false positive/negative rates per engine
5. **Fast Rollback** — Revert `DetectionEngine` in database (no restart needed)

---

## 8. Code Examples

### 8.1 Basic Engine Registration

**Before (current):**
```csharp
// ServiceCollectionExtensions.cs
services.AddScoped<IContentDetectionEngine, ContentDetectionEngine>();
```

**After (dual engines):**
```csharp
// ServiceCollectionExtensions.cs
services.AddScoped<ContentDetectionEngine>();
services.AddScoped<AlternativeContentDetectionEngine>();

// Factory registration (optional, if using factory pattern)
services.AddScoped<IContentDetectionEngine>(provider =>
{
    return new ContentDetectionEngine(
        provider.GetRequiredService<ILogger<ContentDetectionEngine>>(),
        provider.GetRequiredService<ISpamDetectionConfigRepository>(),
        // ... other dependencies
    );
});
```

### 8.2 Engine Selection in ContentCheckCoordinator

```csharp
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly ContentDetectionEngine _engineV1;
    private readonly AlternativeContentDetectionEngine _engineV2;
    private readonly ISpamDetectionConfigRepository _configRepository;

    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        // Load config (already done)
        var config = await _configRepository.GetEffectiveConfigAsync(
            request.ChatId, cancellationToken);

        // Select engine
        var engine = (config.DetectionEngine ?? "v1") switch
        {
            "v1" => _engineV1,
            "v2" => _engineV2,
            _ => _engineV1 // Fallback to v1
        };

        // Run selected engine
        var result = await engine.CheckMessageAsync(enrichedRequest, cancellationToken);

        // Shadow mode (optional)
        if (config.ShadowModeEnabled && config.ShadowEngine != null)
        {
            var shadowEngine = config.ShadowEngine switch
            {
                "v1" => _engineV1,
                "v2" => _engineV2,
                _ => null
            };

            if (shadowEngine != null && shadowEngine != engine)
            {
                var shadowResult = await shadowEngine.CheckMessageAsync(
                    enrichedRequest, cancellationToken);
                
                // Log comparison
                await LogEngineComparisonAsync(result, shadowResult, request, cancellationToken);
            }
        }

        return new ContentCheckCoordinatorResult { SpamResult = result };
    }
}
```

### 8.3 New Engine Template

```csharp
/// <summary>
/// Alternative spam detection engine (v2)
/// Implements different orchestration logic while reusing individual checks
/// </summary>
public class AlternativeContentDetectionEngine : IContentDetectionEngine
{
    private readonly ILogger<AlternativeContentDetectionEngine> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IFileScanningConfigRepository _fileScanningConfigRepo;
    private readonly IEnumerable<IContentCheck> _spamChecks;
    private readonly IOpenAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly SpamDetectionOptions _spamDetectionOptions;

    public AlternativeContentDetectionEngine(
        ILogger<AlternativeContentDetectionEngine> logger,
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
        // NEW LOGIC HERE
        // Same inputs (ContentCheckRequest)
        // Same outputs (ContentDetectionResult)
        // Can use same checks (_spamChecks)
        // Can use same config repository (_configRepository)
        
        throw new NotImplementedException("Implement your algorithm here");
    }

    public async Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(
        ContentCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        // NEW LOGIC HERE
        throw new NotImplementedException("Implement your algorithm here");
    }
}
```

---

## 9. Configuration Example

**SpamDetectionConfig Addition:**

```csharp
public class SpamDetectionConfig
{
    // ... existing fields ...

    /// <summary>
    /// Which detection engine to use for this chat
    /// "v1" (default), "v2", or other registered engines
    /// </summary>
    public string DetectionEngine { get; set; } = "v1";

    /// <summary>
    /// Enable shadow mode testing (run secondary engine in parallel)
    /// </summary>
    public bool ShadowModeEnabled { get; set; } = false;

    /// <summary>
    /// Which engine to run in shadow mode (compared but not used for action)
    /// Only used if ShadowModeEnabled=true
    /// </summary>
    public string? ShadowEngine { get; set; } = null;
}
```

**Database Migration:**

```sql
-- Add columns to support dual engines
ALTER TABLE spam_detection_configs 
ADD COLUMN detection_engine VARCHAR(50) DEFAULT 'v1' NOT NULL;

ALTER TABLE spam_detection_configs 
ADD COLUMN shadow_mode_enabled BOOLEAN DEFAULT FALSE NOT NULL;

ALTER TABLE spam_detection_configs 
ADD COLUMN shadow_engine VARCHAR(50) DEFAULT NULL;

-- Optional: Create index for engine queries
CREATE INDEX idx_spam_detection_configs_engine 
ON spam_detection_configs(chat_id, detection_engine);
```

---

## 10. Testing Strategy

### 10.1 Unit Tests

Test each engine independently against the same requests:

```csharp
[TestFixture]
public class ContentDetectionEngineTests
{
    private IContentDetectionEngine _engineV1;
    private IContentDetectionEngine _engineV2;

    [Test]
    public async Task BothEngines_ReturnSameType()
    {
        var request = CreateTestRequest(message: "Test spam message");
        
        var resultV1 = await _engineV1.CheckMessageAsync(request);
        var resultV2 = await _engineV2.CheckMessageAsync(request);

        Assert.IsInstanceOf<ContentDetectionResult>(resultV1);
        Assert.IsInstanceOf<ContentDetectionResult>(resultV2);
    }

    [Test]
    public async Task BothEngines_HandleSameChecks()
    {
        var request = CreateTestRequest(message: "Test");
        
        var resultV1 = await _engineV1.CheckMessageAsync(request);
        var resultV2 = await _engineV2.CheckMessageAsync(request);

        Assert.That(resultV1.CheckResults.Count, Is.GreaterThan(0));
        Assert.That(resultV2.CheckResults.Count, Is.GreaterThan(0));
    }
}
```

### 10.2 Integration Tests

Compare engine outputs on real data:

```csharp
[Test]
public async Task Engines_ProduceConsistentResults_OnKnownSpam()
{
    var spamMessage = "CLICK HERE FOR FREE CRYPTO!!!";
    var request = CreateTestRequest(message: spamMessage);
    
    var resultV1 = await _engineV1.CheckMessageAsync(request);
    var resultV2 = await _engineV2.CheckMessageAsync(request);
    
    // Both should detect as spam
    Assert.IsTrue(resultV1.IsSpam);
    Assert.IsTrue(resultV2.IsSpam);
}
```

### 10.3 Shadow Mode Analytics Query

```sql
-- Find cases where engines disagree
SELECT 
    chatId,
    userId,
    COUNT(*) as disagreement_count,
    AVG(ABS(v1_confidence - v2_confidence)) as avg_confidence_delta,
    SUM(CASE WHEN (v1_is_spam AND NOT v2_is_spam) THEN 1 ELSE 0 END) as v1_false_positives,
    SUM(CASE WHEN (NOT v1_is_spam AND v2_is_spam) THEN 1 ELSE 0 END) as v1_false_negatives
FROM engine_comparison_logs
WHERE created_at > NOW() - INTERVAL '7 days'
GROUP BY chatId, userId
HAVING COUNT(*) > 5
ORDER BY disagreement_count DESC
LIMIT 10;
```

---

## 11. Complexity Estimates

### 11.1 Task-Level Complexity

| Component | Complexity | Notes |
|-----------|-----------|-------|
| **Architecture** | TRIVIAL | Interface exists, no breaking changes |
| **DI Registration** | TRIVIAL | Add factory or conditional logic |
| **Engine Selection** | TRIVIAL | Simple switch statement |
| **Configuration** | TRIVIAL | Add one field to `SpamDetectionConfig` |
| **Database Migration** | TRIVIAL | Add 3 columns |
| **New Engine Implementation** | MEDIUM-HIGH | Depends on new algorithm |
| **Shadow Mode** | MODERATE | Requires analytics table + queries |
| **Testing** | MODERATE | Need comparison tests + metrics |
| **UI Changes** | MEDIUM | Add engine selector to settings |

### 11.2 Overall Complexity: **MODERATE**

**If just adding empty v2 engine:** 1-2 days
**If implementing new spam detection algorithm:** 2-5 days (depends on complexity)
**If adding shadow mode testing:** +1-2 days

---

## 12. Summary & Recommendations

### 12.1 Can We Do This?

**YES** — The architecture is already 80% ready. The interface boundary is perfect.

### 12.2 Complexity Level

**MODERATE** (2-3 days for foundation + engine selection, not including new algorithm)

### 12.3 Risk Level

**LOW** — No breaking changes, can use shadow mode for validation, easy rollback

### 12.4 Recommended Approach

1. **Phase 1 (Days 1):** Create `AlternativeContentDetectionEngine` stub, add config fields, register both engines
2. **Phase 2 (Days 1-2):** Implement new engine logic (algorithm-dependent)
3. **Phase 3 (Day 2-3):** Add shadow mode analytics, create comparison queries
4. **Phase 4:** Run shadow mode for 1-2 weeks, collect metrics, validate, then switch

### 12.5 Key Advantages of This Codebase

1. ✓ Interface already exists and is well-designed
2. ✓ Checks are reusable (no duplication needed)
3. ✓ Config is per-chat (can test gradually)
4. ✓ No service dependencies need changes
5. ✓ Easy to shadow mode (run both engines)
6. ✓ Fast rollback (config change, no restart needed)

### 12.6 Files That Would Need Changes

```
NEW FILES:
- TelegramGroupsAdmin.ContentDetection/Services/AlternativeContentDetectionEngine.cs
- TelegramGroupsAdmin.Data/Migrations/[timestamp]_AddEngineSelection.cs (migration)

MODIFIED FILES:
- TelegramGroupsAdmin.ContentDetection/Configuration/SpamDetectionConfig.cs (+3 fields)
- TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs (+1 registration)
- TelegramGroupsAdmin.Telegram/Services/ContentCheckCoordinator.cs (+engine selection logic)
- TelegramGroupsAdmin.Data/AppDbContext.cs (if adding analytics table)

OPTIONAL:
- Blazor UI pages for engine selection
- Analytics repository for shadow mode comparisons
- Unit/integration tests
```

---

## 13. Proof of Concept: Minimal Shadow Mode

Here's the minimal change needed to enable shadow mode testing WITHOUT a new engine:

```csharp
// ContentCheckCoordinator.cs - Just add shadow mode support
var fullResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);

// Shadow check: run same engine again with verbose logging (just for testing)
if (config.DebugMode)
{
    var shadowResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);
    
    if (fullResult.IsSpam != shadowResult.IsSpam)
    {
        _logger.LogWarning("Shadow mode discrepancy: {Result1} vs {Result2}", 
            fullResult.IsSpam, shadowResult.IsSpam);
    }
}
```

This proves the pattern works with zero new infrastructure. When ready, replace with actual v2 engine.

---

## Conclusion

**Implementing a dual-engine system is straightforward and low-risk.** The codebase is already architecturally prepared for it. The main effort is:

1. Designing and implementing the new engine's algorithm
2. Adding configuration/selection logic (trivial)
3. Setting up analytics to validate the switch

The interface-based design means you can iterate without touching the rest of the application.

