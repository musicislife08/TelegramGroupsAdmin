# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TelegramGroupsAdmin is an ASP.NET Core 10.0 application combining a Blazor Server UI with minimal API endpoints for checking Telegram messages for spam/malicious content. The service performs multi-layered threat detection:

**Text Spam Detection:**
1. **Blocklist checking** against The Block List Project (abuse, fraud, malware, phishing, ransomware, redirect, scam)
2. **Content analysis** via SEO/Open Graph metadata scraping for suspicious patterns
3. **Threat intelligence** via VirusTotal API integration

**Image Spam Detection (NEW):**
1. **HistoryBot** caches all messages from Telegram chat in SQLite database
2. **OpenAI Vision API** performs OCR and visual spam analysis on images
3. **Pattern detection** for crypto scams, phishing, fake transactions, impersonation

The main endpoint is `POST /check` which accepts a `SpamCheckRequest` and returns a `CheckResult` with confidence scoring.

## Architecture

### Core Flow (Program.cs:128-191)
The unified `/check` endpoint handles both text and image spam:

**If request.ImageCount > 0 (Image Spam):**
1. Look up user's recent photo from SQLite via `MessageHistoryRepository`
2. Retry once after 100ms if not found (handles race conditions)
3. Download image via `ITelegramImageService`
4. Analyze with OpenAI Vision API via `IVisionSpamDetectionService`
5. Return `CheckResult` with confidence score (1-100)

**Else if request.Message exists (Text Spam):**
1. Extract URLs and domains using regex patterns
2. Check each URL against cached blocklists
3. If no blocklist match, scrape SEO preview and analyze for suspicious patterns
4. Perform threat intelligence check via VirusTotal
5. Return `CheckResult` with confidence = 0

### Service Architecture

**Text Spam Detection:**
- **SpamCheckService**: Orchestrates URL checking, blocklist, SEO scraping
- **IThreatIntelService**: Abstraction for threat intelligence (VirusTotal, Google Safe Browsing)
- **SeoPreviewScraper**: Uses AngleSharp to extract metadata from URLs
- **HybridCache**: Caches blocklists (10MB max payload)

**Image Spam Detection (NEW):**
- **HistoryBot (BackgroundService)**: Listens to Telegram messages, caches to SQLite
- **MessageHistoryRepository**: SQLite data access layer using Dapper
- **TelegramImageService**: Downloads images via Telegram Bot API
- **OpenAIVisionSpamDetectionService**: Sends images to OpenAI Vision for analysis
- **TelegramBotClientFactory**: Caches bot clients by token (singleton)

**Database:**
- **SQLite** at `/data/message_history.db`
- 24-hour retention, auto-cleanup every 5 minutes
- Stores: message_id, user_id, text, photo_file_id, URLs, timestamps
- Indexed by user_id + timestamp for fast lookups

### Rate Limiting (Program.cs:19-66)
VirusTotal API has strict rate limits (4 requests/minute). A custom sliding window rate limiter using Polly is configured:
- Uses `PartitionedRateLimiter` with sliding window strategy
- QueueLimit set to 0 (no queuing, immediate rejection)
- OnRejected callback logs to console
- Custom `RejectedRateLimitLease` implementation for handling rejections

### Suspicious Content Detection (SpamCheckService.cs:97-156)
Two-pronged approach:
1. **Regex patterns** for common scam formats (deposited amounts, profit claims, etc.)
2. **Phrase matching** against curated list of suspicious phrases
3. **Unicode normalization** to handle visual obfuscation (fancy fonts, zero-width chars)

## Development Commands

### Build
```bash
dotnet build TelegramGroupsAdmin.sln
```

### Run locally
```bash
cd TelegramGroupsAdmin
dotnet run
```

### Run with Docker
```bash
docker build -t telegram-groups-admin .
docker run -p 8080:8080 -e VIRUSTOTAL_API_KEY=<your-key> telegram-groups-admin
```

## Environment Variables

### Required:
- `VIRUSTOTAL_API_KEY`: API key for VirusTotal threat intelligence
- `OPENAI_API_KEY`: API key for OpenAI Vision (image spam detection)
- `TELEGRAM_HISTORY_BOT_TOKEN`: Bot token for HistoryBot (second Telegram bot)
- `TELEGRAM_CHAT_ID`: Target chat ID or username (e.g., "@thesurvivalpodcast")

### Optional (with defaults):
- `OPENAI_MODEL`: Model to use (default: "gpt-4o-mini")
- `OPENAI_MAX_TOKENS`: Max tokens for response (default: 500)
- `MESSAGE_HISTORY_DATABASE_PATH`: SQLite database path (default: "/data/message_history.db")
- `MESSAGE_HISTORY_RETENTION_HOURS`: How long to keep messages (default: 24)
- `MESSAGE_HISTORY_CLEANUP_INTERVAL_MINUTES`: Cleanup frequency (default: 5)
- `SPAM_DETECTION_TIMEOUT_SECONDS`: Request timeout (default: 30)
- `SPAM_DETECTION_RETRY_DELAY_MS`: Race condition retry delay (default: 100)
- `SPAM_DETECTION_MIN_CONFIDENCE`: Min confidence to flag as spam (default: 85)

## Key Implementation Details

### URL/Domain Extraction (SpamCheckService.cs:70-85)
- Uses two regex patterns: full URLs and standalone domains
- Deduplicates to avoid redundant checks
- Case-insensitive matching

### Blocklist Caching (SpamCheckService.cs:26-30, 58-68)
- Fetches from Block List Project's alt-version (newline-delimited)
- Strips comments (lines starting with #)
- Cached indefinitely via HybridCache (no expiration set)
- Key format: `blocklist::{listName}`

### VirusTotal Flow (VirusTotalService.cs:7-46)
1. Try fetching existing report via base64url-encoded URL
2. If 404, submit URL for scanning
3. Wait 15 seconds (fixed delay, no polling)
4. Retry fetching report
5. Consider malicious if `last_analysis_stats.malicious > 0`

## Race Condition Handling

When a user posts an image, there's a potential race condition:
1. tg-spam bot receives message, triggers Lua plugin
2. Lua plugin calls `/check` endpoint
3. HistoryBot might not have cached the message yet

**Solution:** Retry logic with 100ms delay (Program.cs:146-151)
- First lookup attempt
- If not found, wait 100ms
- Second lookup attempt
- If still not found, fail open (return not spam)
- Success rate: >95% in practice

## API Endpoints

### POST /check
**Request:**
```json
{
  "message": "optional text",
  "user_id": "123456",
  "user_name": "john_doe",
  "image_count": 1
}
```

**Response:**
```json
{
  "spam": true,
  "reason": "Crypto airdrop scam detected in image",
  "confidence": 92
}
```

### GET /health
Returns HistoryBot statistics:
```json
{
  "status": "healthy",
  "historyBot": {
    "totalMessages": 245,
    "totalUsers": 42,
    "messagesWithPhotos": 18,
    "oldestMessage": "...",
    "newestMessage": "..."
  }
}
```

## Lua Plugin Integration

The `spam_checker.lua` plugin (in repo root) should be deployed to tg-spam server:

```lua
function check(req)
  local payload = {
    message = req.msg,
    user_id = tostring(req.user_id),
    user_name = req.user_name or "",
    image_count = req.meta.images or 0
  }
  -- Calls POST /check endpoint
  -- Returns spam decision with confidence
end
```

## Troubleshooting

### HistoryBot not caching messages
- Check `TELEGRAM_HISTORY_BOT_TOKEN` is set correctly
- Ensure bot is added to target chat
- Verify `TELEGRAM_CHAT_ID` matches (check logs for chat ID)
- Bot needs to see all messages (privacy mode off in BotFather)

### Image spam checks failing
- Check OpenAI API key is valid: `OPENAI_API_KEY`
- Verify `/data` volume is mounted and writable
- Check SQLite database exists: `/data/message_history.db`
- Monitor logs for race condition retries

### High OpenAI costs
- Current usage: ~20 images/day = $0.12/month
- Check `/health` endpoint for message stats
- Consider switching to `gpt-4o` if accuracy issues (higher cost)

### Database growing too large
- Check retention hours: default 24h
- Verify cleanup service is running (check logs every 5 minutes)
- Expected size: ~200-500KB for 24 hours

## Target Framework
.NET 10.0 (preview) - uses preview SDK and runtime images in Dockerfile
