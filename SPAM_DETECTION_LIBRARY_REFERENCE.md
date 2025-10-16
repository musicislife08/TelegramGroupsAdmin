# Spam Detection Library Reference

## Overview

The TelegramGroupsAdmin spam detection library is a comprehensive, production-ready system based on the proven tg-spam implementation with modern enhancements. It provides 9 specialized spam detection algorithms with self-improving capabilities, database-driven configuration, and advanced AI integration.

## Core Architecture

### ISpamDetectorFactory
Central orchestration service that coordinates all spam checks and aggregates results.

```csharp
// Run all checks with OpenAI veto
var result = await _spamDetectorFactory.CheckMessageAsync(request);

// Run without OpenAI (for veto preparation)
var preResult = await _spamDetectorFactory.CheckMessageWithoutOpenAIAsync(request);
```

**Key Features:**
- Confidence aggregation from multiple checks
- OpenAI veto system (only runs when other checks flag spam)
- Recommended actions (Allow/ReviewQueue/AutoBan) based on thresholds
- Comprehensive result details for audit trails

### SpamDetectionResult
```csharp
public record SpamDetectionResult
{
    public bool IsSpam { get; init; }
    public int MaxConfidence { get; init; }        // Highest confidence from any check
    public int AvgConfidence { get; init; }        // Average of spam-flagging checks
    public int SpamFlags { get; init; }            // Number of checks that flagged spam
    public List<SpamCheckResponse> CheckResults { get; init; }
    public string PrimaryReason { get; init; }     // From highest confidence check
    public SpamAction RecommendedAction { get; init; }
    public bool ShouldVeto { get; init; }          // Whether to run OpenAI veto
}
```

## Spam Detection Algorithms

### 1. StopWords Check ✅
**Enhanced with database storage and multi-field checking**

```csharp
// Checks: message text, username, userID
// Database: stop_words table with UI management
// Features: Emoji preprocessing, caching, confidence scoring
```

**Improvements over tg-spam:**
- Database-driven patterns (UI manageable)
- Checks all 3 fields like tg-spam (message/username/userID)
- Shared tokenizer for consistent preprocessing
- Real-time cache refresh

### 2. CAS (Combot Anti-Spam) ✅
**Simplified with reliable fail-open behavior**

```csharp
// Global Telegram user database
// Features: HTTP caching, rate limit handling, fail-open design
```

**Improvements:**
- Streamlined error handling
- Consistent fail-open behavior
- Reduced complexity while maintaining effectiveness

### 3. Similarity Check ✅
**TF-IDF with database samples and early exit optimization**

```csharp
// Database: spam_samples table with usage tracking
// Algorithm: TF-IDF cosine similarity with early exit
// Features: Detection count tracking, pattern effectiveness
```

**Improvements:**
- Database-stored patterns with effectiveness tracking
- Early exit after high-confidence match (performance)
- Continuous learning through sample collection
- Pattern usage statistics for optimization

### 4. Bayes Classifier ✅
**Self-learning with certainty scoring**

```csharp
// Database: training_samples table
// Algorithm: Naive Bayes with Laplace smoothing + certainty scoring
// Features: Automatic retraining, confidence adjustment
```

**Improvements:**
- Database training data with automatic retraining (hourly)
- Certainty scoring (confidence × certainty)
- Continuous learning pipeline
- Shared tokenizer integration

### 5. MultiLanguage Check ✅
**Translation-based approach using OpenAI**

```csharp
// Replaces complex Unicode script analysis
// Uses OpenAI translation + runs spam checks on translated content
// Still detects invisible characters (highly effective)
```

**Improvements:**
- Simplified approach using proven AI translation
- Context-aware foreign language detection
- Runs full spam suite on translated content
- Maintains invisible character detection

### 6. Spacing Check ✅
**Focused on core effective patterns**

```csharp
// Core patterns: space ratios, invisible chars, letter spacing
// Removed complex alternating patterns
// Enhanced scoring for high-impact indicators
```

**Improvements:**
- Streamlined to most effective patterns
- Higher scoring for invisible characters
- Reduced false positives
- Optimized performance

### 7. OpenAI Check ✅
**Enhanced veto system with context and fallback**

```csharp
// Features: Message history context, JSON responses, fallback parsing
// Veto mode: Only runs when other checks flag spam
// Robust: JSON → legacy parsing → fail-open
```

**Improvements:**
- Message history context for better decisions
- Structured JSON responses with reason and confidence
- Multiple fallback mechanisms
- True veto mode (prevents false positives)

### 8. ThreatIntel Check ✅
**VirusTotal + Google Safe Browsing with caching**

```csharp
// APIs: VirusTotal, Google Safe Browsing
// Features: URL extraction, caching, rate limit handling
```

### 9. Image Spam Check ✅
**OpenAI Vision integration**

```csharp
// Uses OpenAI Vision API for image content analysis
// Integrates with existing HistoryBot message caching
```

## Database Schema

### Core Tables

#### stop_words
```sql
CREATE TABLE stop_words (
    id INTEGER PRIMARY KEY,
    word TEXT UNIQUE NOT NULL,
    enabled BOOLEAN DEFAULT 1,
    added_date INTEGER NOT NULL,
    added_by TEXT,
    notes TEXT
);
```

#### training_samples (Bayes)
```sql
CREATE TABLE training_samples (
    id INTEGER PRIMARY KEY,
    message_text TEXT NOT NULL,
    is_spam BOOLEAN NOT NULL,
    added_date INTEGER NOT NULL,
    source TEXT NOT NULL,
    confidence_when_added INTEGER,
    group_id TEXT,
    added_by TEXT
);
```

#### spam_samples (Similarity)
```sql
CREATE TABLE spam_samples (
    id INTEGER PRIMARY KEY,
    sample_text TEXT NOT NULL,
    added_date INTEGER NOT NULL,
    source TEXT NOT NULL,
    enabled BOOLEAN DEFAULT 1,
    group_id TEXT,
    added_by TEXT,
    detection_count INTEGER DEFAULT 0,
    last_detected_date INTEGER
);
```

#### group_prompts (OpenAI)
```sql
CREATE TABLE group_prompts (
    id INTEGER PRIMARY KEY,
    group_id TEXT NOT NULL,
    custom_prompt TEXT NOT NULL,
    enabled BOOLEAN DEFAULT 1,
    added_date INTEGER NOT NULL,
    added_by TEXT,
    notes TEXT
);
```

#### spam_check_configs
```sql
CREATE TABLE spam_check_configs (
    id INTEGER PRIMARY KEY,
    group_id TEXT NOT NULL,
    check_name TEXT NOT NULL,
    enabled BOOLEAN DEFAULT 1,
    confidence_threshold INTEGER,
    configuration_json TEXT,
    modified_date INTEGER NOT NULL,
    modified_by TEXT,
    UNIQUE(group_id, check_name)
);
```

## Configuration

### SpamDetectionConfig
```csharp
public record SpamDetectionConfig
{
    public int AutoBanThreshold { get; init; } = 80;
    public int ReviewQueueThreshold { get; init; } = 50;
    public int MinMessageLength { get; init; } = 50;

    public StopWordsConfig StopWords { get; init; } = new();
    public SimilarityConfig Similarity { get; init; } = new();
    public CasConfig Cas { get; init; } = new();
    public BayesConfig Bayes { get; init; } = new();
    public MultiLanguageConfig MultiLanguage { get; init; } = new();
    public SpacingConfig Spacing { get; init; } = new();
    public OpenAIConfig OpenAI { get; init; } = new();
    // ... other configs
}
```

### Example Check Configurations
```csharp
// Stop Words
public record StopWordsConfig
{
    public bool Enabled { get; init; } = true;
    public int ConfidenceThreshold { get; init; } = 50;
}

// OpenAI
public record OpenAIConfig
{
    public bool Enabled { get; init; } = true;
    public bool VetoMode { get; init; } = true;
    public bool CheckShortMessages { get; init; } = false;
    public string? SystemPrompt { get; init; }
}
```

## Dependency Injection Setup

```csharp
// Program.cs or Startup.cs
services.AddSpamDetection(new SpamDetectionConfig
{
    AutoBanThreshold = 85,
    ReviewQueueThreshold = 60,
    OpenAI = new OpenAIConfig { VetoMode = true }
});

// Or with defaults
services.AddSpamDetection();
```

**Registered Services:**
- `ISpamDetectorFactory` - Main orchestration
- `ITokenizerService` - Shared text preprocessing
- `IOpenAITranslationService` - Language detection/translation
- `IMessageHistoryService` - Context retrieval
- `IStopWordsRepository`, `ISpamSamplesRepository`, `ITrainingSamplesRepository`
- All `ISpamCheck` implementations

## Usage Examples

### Basic Spam Check
```csharp
var request = new SpamCheckRequest
{
    Message = "Buy bitcoin now! Guaranteed profits!",
    UserId = "12345",
    UserName = "spammer",
    GroupId = "group_123"
};

var result = await _spamDetectorFactory.CheckMessageAsync(request);

if (result.IsSpam)
{
    switch (result.RecommendedAction)
    {
        case SpamAction.AutoBan:
            // Automatically ban user
            break;
        case SpamAction.ReviewQueue:
            // Add to review queue
            break;
        case SpamAction.Allow:
            // Allow but log
            break;
    }
}
```

### Continuous Learning
```csharp
// Automatically add training samples
var bayesCheck = serviceProvider.GetService<BayesSpamCheck>();
await bayesCheck.AddTrainingSampleAsync(
    messageText: "Legitimate conversation",
    isSpam: false,
    source: "manual_review",
    confidence: 95,
    groupId: "group_123"
);
```

### Custom Configuration
```csharp
// Per-group configuration via database
await _configRepository.UpdateCheckConfigAsync(
    groupId: "group_123",
    checkName: "StopWords",
    enabled: true,
    confidenceThreshold: 70,
    configuration: JsonSerializer.Serialize(new StopWordsConfig { Enabled = true })
);
```

## Performance Features

### Caching
- **OpenAI responses**: 1-hour cache by message hash
- **CAS lookups**: 1-hour cache by user ID
- **Stop words**: 5-minute cache refresh
- **Spam samples**: 10-minute cache refresh

### Early Exit Optimization
- **Similarity check**: Stops after high-confidence match or 20 samples
- **OpenAI veto**: Only runs when other checks flag spam
- **Short message skip**: Expensive checks skip messages < 50 chars

### Database Optimization
- **Indexed queries**: All lookups use proper indexes
- **Batch operations**: Efficient bulk sample insertion
- **Connection pooling**: Reused database connections

## Error Handling & Reliability

### Fail-Open Design
All checks fail open (return non-spam) on errors to prevent false positives:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Check failed for user {UserId}", request.UserId);
    return new SpamCheckResponse
    {
        CheckName = CheckName,
        IsSpam = false, // Fail open
        Details = "Check failed due to error",
        Confidence = 0,
        Error = ex
    };
}
```

### Fallback Systems
- **OpenAI**: JSON parsing → legacy parsing → fail-open
- **Translation**: Translation failure → treat as English
- **Database**: Query failure → use cached data → skip check

### Rate Limiting
- **VirusTotal**: 4 requests/minute with queue
- **OpenAI**: HTTP 429 detection with retry-after
- **Google Safe Browsing**: Built-in quota management

## Monitoring & Logging

### Key Metrics
- Spam detection rates by algorithm
- False positive/negative tracking
- API response times and error rates
- Cache hit/miss ratios
- Training sample collection rates

### Log Levels
```csharp
// Debug: Individual check results and performance
_logger.LogDebug("StopWords check: IsSpam={IsSpam}, Confidence={Confidence}", isSpam, confidence);

// Information: Important operational events
_logger.LogInformation("Retrained Bayes classifier with {Count} samples", sampleCount);

// Warning: Rate limits, API errors
_logger.LogWarning("OpenAI API rate limited for user {UserId}", userId);

// Error: Unexpected failures
_logger.LogError(ex, "Spam detection failed for user {UserId}", userId);
```

## Migration from tg-spam

### API Compatibility
The enhanced `/check` endpoint maintains backward compatibility while providing additional detail:

```json
// Legacy response (still supported)
{"spam": true, "reason": "...", "confidence": 85}

// Enhanced response (new features)
{
  "spam": true,
  "reason": "...",
  "confidence": 85,
  "max_confidence": 90,
  "spam_flags": 3,
  "recommended_action": "ReviewQueue",
  "check_results": [...]
}
```

### Configuration Migration
tg-spam configurations can be imported into the database:

```csharp
// Import stop words from tg-spam files
var stopWord = new StopWord(
    Id: 0,
    Word: "bitcoin",
    Enabled: true,
    AddedDate: DateTimeOffset.UtcNow,
    AddedBy: "import",
    Notes: "Imported from tg-spam"
);
await _stopWordsRepository.AddStopWordAsync(stopWord);

// Import spam samples
await _samplesRepository.AddSampleAsync(spamText, "tg-spam", groupId: null, "import");
```

### Gradual Transition
1. **Phase 1**: Use enhanced API with existing tg-spam bot
2. **Phase 2**: Migrate detection logic to call our factory directly
3. **Phase 3**: Replace with native Telegram bot implementation

This spam detection library provides a robust, scalable foundation for comprehensive Telegram spam management with significant improvements over the original tg-spam implementation.