# OpenAIContentCheck Test Suite

## Overview

Comprehensive NUnit test suite for `OpenAIContentCheck` using WireMock.Net to mock the OpenAI API. This test suite validates the spam detection logic without making real HTTP calls to external services.

## Testing Strategy

### WireMock.Net Architecture

The tests use **WireMock.Net** to create a real HTTP server that mimics the OpenAI API. This approach provides several benefits over mocking HttpClient directly:

1. **Realistic HTTP Behavior**: Tests actual HTTP request/response handling including headers, status codes, and network errors
2. **Request Verification**: Can inspect what was sent to the API (headers, body, query params)
3. **Flexible Response Mocking**: Easy to simulate various API responses (success, errors, timeouts)
4. **No HttpClient Mocking Complexity**: Avoids brittle mocks of HttpMessageHandler or HttpClient internals

### Test Organization

Tests are organized into logical groups:

1. **ShouldExecute Tests**: Validation of execution conditions
2. **JSON Response Parsing Tests**: Modern JSON response format handling
3. **Veto Mode Tests**: OpenAI veto behavior (confirming/overriding other checks)
4. **Legacy Text Response Parsing Tests**: Fallback parsing for non-JSON responses
5. **Error Handling Tests**: HTTP errors, timeouts, malformed responses
6. **Cache Behavior Tests**: MemoryCache integration
7. **Message Length Tests**: Short message handling
8. **History Context Tests**: Message history integration
9. **Confidence Calculation Tests**: Score conversion and rounding
10. **Custom Prompt Tests**: Custom vs default system prompts
11. **API Request Format Tests**: Request structure validation

## Test Coverage

### Core Functionality (34 Tests)

#### Execution Logic
- ✅ Valid messages trigger execution
- ✅ Empty/whitespace messages are skipped
- ✅ Veto mode respects spam flags
- ✅ Short messages handled per configuration

#### JSON Response Parsing
- ✅ "spam" result correctly parsed
- ✅ "clean" result correctly parsed
- ✅ "review" result correctly parsed
- ✅ Case-insensitive result parsing (SPAM, ClEaN, etc.)
- ✅ Unknown result types default to "clean" (fail-open)
- ✅ Null confidence defaults to 80%

#### Legacy Text Parsing (Fallback)
- ✅ Text containing "SPAM" detected as spam
- ✅ Text without "SPAM" marked as clean
- ✅ "NOT_SPAM" keyword overrides spam detection
- ✅ Legacy veto mode responses handled
- ✅ Lower confidence scores for legacy parsing (75%)

#### Error Handling (Fail-Open Philosophy)
- ✅ HTTP 429 (Rate Limit) → Clean with error logged
- ✅ HTTP 500 (Server Error) → Clean with error logged
- ✅ Malformed JSON → Fallback to legacy parsing
- ✅ Empty API response → Clean with error
- ✅ Null choices array → Clean with error
- ✅ Empty content → Clean with error
- ✅ Missing API key → Clean with configuration error
- ✅ Network timeout → Clean with timeout error
- ✅ All errors fail open to preserve user experience

#### Caching
- ✅ First request populates cache
- ✅ Second request uses cached result (no API call)
- ✅ Cached responses marked with "(cached)"
- ✅ Different messages use distinct cache keys
- ✅ 1-hour cache expiration

#### Veto Mode
- ✅ No spam flags → Skips OpenAI (no API call)
- ✅ Spam flags present → Calls OpenAI
- ✅ "spam" result confirms spam
- ✅ "clean" result vetoes (overrides) spam

#### Message Length
- ✅ Short messages skipped when checkShortMessages=false
- ✅ Short messages checked when checkShortMessages=true
- ✅ Messages at minMessageLength boundary checked

#### History Context
- ✅ Recent message history included in API request
- ✅ [OK] and [SPAM] tags in context
- ✅ No history still allows check
- ✅ History service called with correct parameters

#### Confidence Calculation
- ✅ 0.0 → 0%, 0.5 → 50%, 1.0 → 100%
- ✅ Correct rounding (0.845 → 85%)
- ✅ Legacy parsing returns 75% for spam, 0% for clean

#### Custom Prompts
- ✅ Custom system prompts included in request
- ✅ Default rules used when no custom prompt
- ✅ Technical base prompt always included

#### API Request Format
- ✅ Correct JSON structure (model, messages, max_tokens, temperature, top_p)
- ✅ response_format set to "json_object"
- ✅ System and user message roles
- ✅ User info (ID, name) in prompt

## Key Design Decisions

### 1. Fail-Open Philosophy

**All errors result in CheckResultType.Clean**

```csharp
// Rate limit
return CheckResultType.Clean; // Fail open during rate limits

// Server error
return CheckResultType.Clean; // Fail open on server errors

// Timeout
return CheckResultType.Clean; // Fail open on timeout
```

**Rationale**: False positives (blocking legitimate messages) are worse than false negatives (allowing spam). When the OpenAI service is unavailable, the application should degrade gracefully rather than block all messages.

### 2. Real HTTP Testing with WireMock

```csharp
// Real HttpClient, not mocked
var httpClient = new HttpClient
{
    BaseAddress = new Uri(_mockServer.Url!)
};
```

**Benefits**:
- Tests actual HTTP behavior (headers, timeouts, status codes)
- Verifies request serialization and response deserialization
- Catches issues that mocked HttpClients would miss

### 3. Real MemoryCache

```csharp
_cache = new MemoryCache(new MemoryCacheOptions());
```

**Benefits**:
- Tests actual caching behavior and TTL
- Verifies cache key generation
- Ensures cache isolation between tests

### 4. Legacy Fallback Support

Modern API uses JSON responses:
```json
{
  "result": "spam",
  "reason": "Contains promotional content",
  "confidence": 0.95
}
```

But code falls back to legacy text parsing if JSON fails:
```
"This message contains SPAM content..."
```

**Tests ensure both paths work correctly.**

### 5. Veto Mode Testing

Veto mode is an optimization where OpenAI only runs if other checks flagged spam:

```csharp
if (req.VetoMode && !req.HasSpamFlags)
{
    return Clean; // Skip API call entirely
}
```

Tests verify:
- No API call when hasSpamFlags=false
- API call made when hasSpamFlags=true
- Correct "confirmed"/"vetoed" messaging

## Running the Tests

```bash
# Run all ContentDetection tests
dotnet test --filter "FullyQualifiedName~TelegramGroupsAdmin.Tests.ContentDetection"

# Run only OpenAIContentCheck tests
dotnet test --filter "FullyQualifiedName~OpenAIContentCheckTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~OpenAIContentCheckTests.CheckAsync_WithValidJsonSpamResponse_ReturnsSpamResult"
```

## Test Maintenance

### Adding New Tests

When adding new OpenAI features:

1. **Add test to appropriate section** (e.g., new error handling → Error Handling Tests)
2. **Use helper methods**: `CreateOpenAICheckRequest()`, `MockSuccessfulJsonResponse()`
3. **Follow AAA pattern**: Arrange → Act → Assert
4. **Reset WireMock in SetUp**: Ensures test isolation
5. **Test both success and failure paths**

### Updating Mock Responses

The helper method `MockSuccessfulJsonResponse()` handles most cases:

```csharp
MockSuccessfulJsonResponse("spam", "Reason here", 0.95);
```

For custom responses, use `MockOpenAISuccessResponse()`:

```csharp
MockOpenAISuccessResponse(@"{""custom"": ""json""}");
```

### Debugging Failed Tests

1. **Check WireMock logs**: `_mockServer.LogEntries` contains all requests
2. **Inspect request body**: `logEntry.RequestMessage.Body`
3. **Verify API call count**: `_mockServer.LogEntries.Count()`
4. **Check cache state**: Use debugger to inspect `_cache`

## Dependencies

- **NUnit 4.2.2**: Testing framework
- **WireMock.Net 1.15.0**: HTTP mocking server
- **NSubstitute 5.3.0**: Mocking framework for logger and history service
- **Microsoft.Extensions.Caching.Memory 9.0.0**: Real MemoryCache implementation

## Performance Considerations

- **WireMock server started once** (OneTimeSetUp) and reused across tests
- **Reset between tests** (SetUp) for isolation
- **In-memory only**: No disk I/O or external dependencies
- **Fast execution**: 34 tests run in ~1-2 seconds

## Coverage Gaps (Future Enhancements)

While coverage is comprehensive, consider adding tests for:

1. **Concurrent cache access** (race conditions)
2. **Cache expiration timing** (TTL validation)
3. **Very large message handling** (performance)
4. **Unicode and emoji handling** in prompts
5. **Request cancellation mid-flight**
6. **HTTP redirect scenarios**

## Related Files

- **Source**: `/TelegramGroupsAdmin.ContentDetection/Checks/OpenAIContentCheck.cs`
- **Models**: `/TelegramGroupsAdmin.ContentDetection/Models/OpenAICheckRequest.cs`
- **Interfaces**: `/TelegramGroupsAdmin.ContentDetection/Abstractions/IContentCheck.cs`
