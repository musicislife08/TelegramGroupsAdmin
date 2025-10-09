# tg-spam Codebase Reference

## Overview
This document maps the tg-spam Go codebase to understand their proven spam detection implementations. Use this as a reference when implementing the C# equivalents.

**Repository:** https://github.com/umputun/tg-spam
**Language:** Go
**Architecture:** Modular spam checks orchestrated by central detector

## Core Architecture Files

### Main Detector (`lib/tgspam/detector.go`)
**Purpose:** Central orchestrator that runs all spam checks in sequence

**Key Components:**
- `Detector` struct - Main detection engine
- `Check()` method - Runs full detection pipeline
- `ApprovedUsers` tracking - Skip checks for trusted users
- Thread-safe operations with RWMutex
- Configurable check pipeline

**Pipeline Flow:**
1. Pre-approved user check (early exit)
2. Stop words → Emoji → Duplicates → Meta checks
3. CAS → Multi-language → Abnormal spacing
4. Skip expensive checks for short messages
5. Similarity → Bayes → OpenAI (final arbiter)
6. Update approved users if ham

**Key Functions:**
- `NewDetector()` - Constructor with config
- `Check(req Request)` - Main detection entry point
- `isApproved()` - Check if user bypasses detection
- `updateApproved()` - Add user to approved list

### Request/Response Models (`lib/spamcheck/spamcheck.go`)
**Purpose:** Data structures for spam check input/output

**Request Structure:**
```go
type Request struct {
    Msg       string   // message text
    UserID    string   // telegram user ID
    UserName  string   // telegram username
    Meta      MetaData // telegram metadata
    CheckOnly bool     // don't update approved users
}

type MetaData struct {
    Images      int
    Links       int
    Mentions    int
    HasVideo    bool
    HasAudio    bool
    HasForward  bool
    HasKeyboard bool
    MessageID   int
}
```

**Response Structure:**
```go
type Response struct {
    Name           string // check name
    Spam           bool   // is spam
    Details        string // human reason
    Error          error  // check error
    ExtraDeleteIDs []int  // bulk delete IDs
}
```

## Individual Spam Check Files

### Stop Words (`lib/tgspam/detector.go` - `checkStopWords()`)
**Purpose:** Simple substring matching against banned phrases

**Implementation:**
- Load stop words from database
- Case-insensitive substring search
- Text normalization before matching
- Fast early-exit for obvious spam

**Database:** `stop_words` table with phrases

### Duplicate Detection (`lib/tgspam/duplicate.go`)
**Purpose:** Track repeated messages per user with time windows

**Key Components:**
- `DuplicateChecker` struct
- SHA256 message hashing
- Per-user message tracking
- LRU cache with TTL
- ExtraDeleteIDs for bulk deletion

**Configuration:**
- `Threshold` - Max duplicates allowed
- `Window` - Time window for counting
- Memory limits (10k users, 200 msgs/user)

**Key Functions:**
- `NewDuplicateChecker()` - Constructor
- `Check()` - Main duplicate detection
- `cleanup()` - Memory management

### Meta Checks (`lib/tgspam/metachecks.go`)
**Purpose:** Configurable metadata-based spam detection

**Check Types:**
- **Links limit** - Count HTTP/HTTPS URLs
- **Links only** - Messages with only links
- **Images/Videos/Audio only** - Media without text
- **Forward check** - Forwarded messages
- **Keyboard check** - Inline keyboards/buttons
- **Mentions limit** - @username count
- **Username symbols** - Prohibited chars in usernames

**Implementation:**
- `MetaChecker` struct with config
- `Check()` method runs enabled checks
- Configurable thresholds per check type
- Boolean enable/disable per check

### Naive Bayes Classifier (`lib/tgspam/classifier.go`)
**Purpose:** Statistical spam/ham classification

**Key Components:**
- `Classifier` struct
- Token frequency maps for spam/ham
- Log probability calculations
- Softmax normalization
- Excluded tokens (common words)

**Training Data:**
- Spam samples from database
- Ham samples from database
- Token exclusion list
- Minimum probability thresholds

**Key Functions:**
- `NewClassifier()` - Load and train
- `Check()` - Classify message
- `tokenize()` - Text preprocessing
- `probability()` - Bayes calculation

### Spam Similarity (`lib/tgspam/detector.go` - `checkSimilarity()`)
**Purpose:** Cosine similarity against known spam samples

**Implementation:**
- TF (term frequency) vectors
- Cosine similarity calculation
- Tokenized spam sample database
- Configurable similarity threshold (0.5 default)
- Skip short messages (<50 chars)

**Algorithm:**
1. Tokenize input message
2. Create TF vector
3. Compare against all spam sample vectors
4. Return highest similarity score
5. Flag if above threshold

### CAS Integration (`lib/tgspam/detector.go` - `checkCAS()`)
**Purpose:** Combot Anti-Spam System lookup

**Implementation:**
- HTTP GET to `https://api.cas.chat/check?user_id={id}`
- 5-second timeout
- Configurable User-Agent header
- Simple JSON response parsing

**Response Handling:**
- HTTP 200 + `"ok": true` = User is banned on CAS
- HTTP errors or timeouts = Skip check (fail open)
- No rate limiting (CAS handles this)

### Multi-Language Detection (`lib/tgspam/detector.go` - `checkMultiLang()`)
**Purpose:** Detect words mixing multiple Unicode scripts

**Implementation:**
- Unicode script analysis per character
- Count words with mixed scripts (Cyrillic + Latin, etc.)
- Configurable threshold for flagging
- Common spam evasion technique

**Algorithm:**
1. Split message into words
2. Analyze Unicode script for each character
3. Count words with >1 script type
4. Flag if above threshold

### Abnormal Spacing (`lib/tgspam/detector.go` - `checkSpacing()`)
**Purpose:** Detect text spacing manipulation to evade detection

**Metrics:**
- **Space ratio** - Spaces / total characters
- **Short word ratio** - Words ≤3 chars / total words
- Configurable thresholds (space: 0.3, short: 0.7)
- Minimum word count requirement (5 words)

**Common Patterns:**
- "B u y   c r y p t o   n o w"
- "Free$$money$$$here"
- Excessive spacing to break keyword detection

### OpenAI Integration (`lib/tgspam/openai.go`)
**Purpose:** GPT-based spam detection as final layer

**Key Features:**
- **Veto mode** - Confirms spam from other checks (reduces false positives)
- **Enhancement mode** - Catches missed spam (reduces false negatives)
- Historical context support (previous N messages)
- Token limiting with GPT tokenizer
- Custom prompts per spam type

**Configuration:**
- Model selection (GPT-4, GPT-3.5, etc.)
- Max tokens per request
- Temperature settings
- Reasoning effort for thinking models
- Context message count

**Request Format:**
```json
{
  "spam": boolean,
  "reason": "Human readable explanation",
  "confidence": 85
}
```

**Key Functions:**
- `NewOpenAIChecker()` - Constructor with config
- `Check()` - Main detection method
- `buildPrompt()` - Context + system prompt
- `parseResponse()` - JSON response handling

## Configuration System

### Main Config (`lib/lib.go`)
**Purpose:** Central configuration loading and validation

**Config Sections:**
- Database connections
- Spam check enable/disable flags
- Per-check thresholds and settings
- OpenAI API configuration
- CAS integration settings

**Key Structures:**
- `Config` - Main config struct
- `SpamConfig` - Spam detection settings
- `OpenAIConfig` - AI integration settings
- `CASConfig` - CAS API settings

### Database Schema (`lib/storage/`)
**Purpose:** Spam samples, stop words, user tracking

**Key Tables:**
- `spam_samples` - Training data for Bayes/similarity
- `stop_words` - Banned phrase list
- `approved_users` - Users who bypass checks
- `excluded_tokens` - Common words for Bayes

## Utility Files

### Text Processing (`lib/tgspam/util.go`)
**Purpose:** Common text manipulation functions

**Functions:**
- `cleanText()` - Normalize text for analysis
- `extractLinks()` - Find HTTP/HTTPS URLs
- `tokenize()` - Split text into words
- `removeAccents()` - Unicode normalization

### Caching (`lib/tgspam/cache.go`)
**Purpose:** Memory management for performance

**Features:**
- LRU cache with TTL
- Size limits per cache type
- Automatic cleanup routines
- Thread-safe operations

## Testing Structure

### Test Files
- `detector_test.go` - Main detection pipeline tests
- `classifier_test.go` - Bayes classifier tests
- `duplicate_test.go` - Duplicate detection tests
- `openai_test.go` - OpenAI integration tests

**Test Data:**
- Sample spam messages
- Sample ham messages
- Edge cases and boundary conditions
- Performance benchmarks

## Key Design Patterns

### Modular Checks
- Each check is independent
- Configurable enable/disable
- Consistent Request/Response interface
- Fail-open error handling

### Early Exit Optimization
- Approved users skip all checks
- Stop words can exit pipeline early
- Short message filtering
- Performance over completeness

### Thread Safety
- RWMutex for detector state
- Concurrent-safe caching
- Atomic operations for counters
- No shared mutable state

### Graceful Degradation
- External API failures don't block
- Database errors are logged, not fatal
- Missing config uses sensible defaults
- Always prefer false negatives over false positives

## Implementation Notes for C# Port

### Direct Ports
- Stop words, CAS, multi-language, spacing checks are straightforward
- Duplicate detection logic is well-defined
- Request/Response models map directly

### Complex Ports
- **Naive Bayes** - Need C# statistical libraries or custom implementation
- **Cosine similarity** - Requires vector math libraries
- **OpenAI** - Already have integration, just adapt the prompting

### Architecture Adaptations
- Replace Go interfaces with C# interfaces
- Use Dependency Injection instead of struct composition
- Leverage async/await for external API calls
- Use IMemoryCache instead of custom LRU cache

### Testing Strategy
- Port test cases to verify equivalent behavior
- Use same test data for consistency
- Performance benchmarks to ensure no regression
- Integration tests with real Telegram data

## Reference Commands

### Useful Go Code Patterns
```bash
# View main detection pipeline
cat lib/tgspam/detector.go | grep -A 20 "func.*Check"

# See all spam check implementations
grep -r "func.*Check" lib/tgspam/

# Find configuration options
grep -r "Config" lib/ | grep struct

# View test cases for specific check
cat lib/tgspam/*_test.go
```

### Database Schema Extraction
```bash
# Find table definitions
grep -r "CREATE TABLE" lib/storage/

# See sample data structures
grep -r "INSERT INTO" lib/storage/
```

This reference document captures the proven architecture and implementations from tg-spam that we'll adapt for our C# implementation.