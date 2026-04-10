# Best Practices

Follow these recommendations to get the best results from TelegramGroupsAdmin.

## Initial Setup

### 1. Start with Training Mode

Enable Training Mode for 7-14 days to:
- Build training samples for ML algorithms
- Validate detection thresholds
- Identify false positive patterns
- Understand your group's spam profile

### 2. Configure Stop Words

Create a stop words list based on:
- Common spam keywords in your community's language
- Scam/phishing terms
- Inappropriate content for your group

### 3. Enable AI Veto Mode

Use Veto Mode (not Detection Mode) to:
- Reduce false positives
- Lower API costs
- Maintain high accuracy

## Content Detection Scoring

TelegramGroupsAdmin uses **additive scoring** (V2) where each of the 14 detection checks contributes 0.0 to 5.0 points. Scores are summed and compared against two configurable thresholds:

| Total Score | Default | Action |
|-------------|---------|--------|
| Auto-Ban | 4.0 points | Immediate ban + message deletion |
| Review Queue | 2.5 points | Routed for manual admin review |
| Below review threshold | < 2.5 | Message passes through |

Both thresholds are configurable per chat in **Settings > Content Detection**.

### Threshold Tuning Tips

- **Conservative start**: Set AutoBan to 4.5 and ReviewQueue to 3.0 during the first week, then lower as you gain confidence in detection accuracy.
- **Use Training Mode** to route all detections to the review queue before enabling auto-ban.
- **Review false positives**: Mark incorrectly flagged messages as ham to improve ML training data.
- **Review algorithm performance** in Analytics -> Performance to see which checks are most effective for your groups.

## Ongoing Maintenance

### Review Queue Management

- Check review queue daily during first week
- Mark false positives to improve ML training
- Add new spam patterns to training samples

### Performance Monitoring

Watch for:
- High false positive rate (>5%)
- Slow detection times (>1s average)
- API quota limits

## Kick Escalation

Kick escalation uses exponential backoff to increase the temporary ban duration for repeat offenders. The formula is:

```
Duration = 1 minute x 2^(prior kick count)
```

| Prior Kicks | Ban Duration |
|-------------|-------------|
| 0 (first kick) | 1 minute |
| 1 | 2 minutes |
| 2 | 4 minutes |
| 5 | 32 minutes |
| 10 | ~17 hours |
| 11+ | Capped at 24 hours |

Kick counts are tracked per-user across all time and are visible in the user detail dialog.

### Auto-Ban After N Kicks

The `MaxKicksBeforeBan` setting (per chat, under **Chat Management > Configure > Welcome System**) controls whether repeated kicks automatically escalate to a permanent ban. Set to 0 (default) to disable auto-ban escalation.

When enabled, after the configured number of kicks, the next kick action becomes a permanent ban with a ban celebration (if configured).

### Recommended Settings by Group Size

**Small groups (<100 members)**:
- `MaxKicksBeforeBan`: 0 (disabled) — admins know most members and can manually ban if needed.
- Exponential backoff alone is usually sufficient to deter repeat offenders.

**Medium groups (100-1,000 members)**:
- `MaxKicksBeforeBan`: 3-5 — gives users multiple chances but prevents indefinite rejoining.
- Monitor the review queue for users who are repeatedly kicked and consider whether the welcome flow needs adjusting.

**Large groups (>1,000 members)**:
- `MaxKicksBeforeBan`: 2-3 — faster escalation reduces admin overhead.
- Combine with profile scanning for proactive ban of suspicious accounts on join.

## Ban Celebration

Ban celebration posts a GIF with a caption to the chat when a user is banned. This feature is purely for fun and community engagement.

### How It Works

- A random GIF and a random caption are selected using a **shuffle-bag algorithm** (Fisher-Yates shuffle). Every GIF and every caption is shown once before any can repeat, preventing the same celebration from appearing twice in a row.
- GIFs are independent from captions — they are shuffled separately, so the combinations stay fresh.
- Uploaded GIF file IDs are cached after the first send for instant delivery on subsequent uses.
- Captions support placeholders: `{username}` (banned user's name), `{chatname}` (group name), `{bancount}` (today's total ban count).
- Each caption has a chat version and a DM version (DM uses "You" grammar).

### Configuration

Ban celebration is configured per chat with global defaults. Navigate to the ban celebration settings page to manage it.

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | Off | Master toggle for the feature |
| TriggerOnAutoBan | On | Fire when spam detection auto-bans |
| TriggerOnManualBan | On | Fire when an admin manually bans |
| SendToBannedUser | On | Also DM the GIF to the banned user (requires DM-based welcome mode) |

### Content Guidelines

- **Keep it light**: The feature is meant to be fun. Avoid GIFs or captions that could be seen as bullying or harassment.
- **Variety matters**: Upload at least 5-10 GIFs and 5-10 captions. The shuffle-bag guarantees full rotation before repeats, so more content means longer gaps between repeats.
- **Review captions**: Make sure placeholder text reads naturally in both the chat version (`{username}` = "John") and the DM version ("You").
- **File format**: GIFs and MP4 files are supported. Telegram will play them as animations in-chat.
- **Per-chat overrides**: You can disable celebrations for specific chats (e.g., a more formal group) while keeping them enabled globally.

### DM Delivery

The `SendToBannedUser` option sends the celebration GIF via DM to the banned user. This requires:
- DM-based welcome mode (DmWelcome or EntranceExam) to be enabled for the chat.
- The banned user must have previously started the bot.

DM delivery is best-effort and will silently fail if the user has blocked the bot or never started it.

## Profile Scanning

Profile scanning uses the Telegram User API (WTelegram) to fetch a user's full profile on join and score it with AI. This is a proactive security measure that catches suspicious accounts before they post.

### What Gets Scanned

- **Bio/about text**: Checked for spam keywords, scam language, promotional content.
- **Profile photo**: Downloaded and analyzed with AI Vision for inappropriate or suspicious content.
- **Personal channel**: Title, about text, and channel photo are analyzed.
- **Pinned stories**: Up to 4 story images (photos and video thumbnails) are analyzed with AI Vision.
- **Story captions**: Text from pinned stories is included in the AI analysis.
- **Account flags**: Telegram's built-in `scam`, `fake`, and `verified` flags are checked.

### Configuration

Profile scanning is configured per chat under **Chat Management > Configure > Welcome System > Join Security > Profile Scan**.

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | Off | Master toggle |
| BanThreshold | 4.0 | Score at or above this triggers an automatic ban |
| NotifyThreshold | 2.0 | Score at or above this creates an admin alert for review |
| ScanOnJoin | On | Scan when a user joins the chat |
| ScanOnProfileChange | On | Re-scan when a user's name or username changes |

### Threshold Recommendations

The profile scan scoring engine returns a score from 0.0 to 5.0 (same scale as content detection). The default thresholds are deliberately conservative:

- **Ban threshold (4.0)**: Only auto-bans accounts with very strong spam/scam signals. Start here and lower only if you see suspicious accounts slipping through.
- **Notify threshold (2.0)**: Sends an admin alert without taking action. Use this to monitor borderline accounts and decide whether to lower the ban threshold.

**Adjustment guidance**:
- If you see many false positive bans (legitimate users being banned on join), raise the ban threshold to 4.5.
- If spam accounts consistently score 3.0-3.9 and are not caught, lower the ban threshold to 3.5.
- The notify threshold at 2.0 is intentionally sensitive to surface accounts for human review. Lower it only if alerts become overwhelming.

### Photo Censoring

When a profile scan results in a ban and the AI detects nudity in the user's profile photo, the stored photo is automatically censored with a Gaussian blur. This prevents explicit content from appearing in the admin UI (user detail dialog, reports).

### Multi-Chat Deduplication

If a user joins multiple managed chats simultaneously, only one profile scan runs. Results are cached for 60 seconds and reused across chats. This prevents duplicate API calls and duplicate moderation actions.

### Scan Freshness and Diff Detection

The service tracks metadata fingerprints (photo IDs, story IDs, bio text, channel details) and skips the expensive AI scoring step if nothing has changed since the last scan. This means re-scans on profile change are cheap unless the profile actually changed.

### Requirements

- A connected Telegram User API session (WTelegram) — configured in **Settings > Telegram > User API**.
- An AI provider API key for Vision analysis (configured in **Settings > System > AI Providers**).
- Profile scanning runs independently of the bot API and does not count against bot rate limits.

## Group Size Recommendations

### Small Groups (<100 members)

- Enable all 14 detection checks except AI Veto
- Use AI Veto Mode if you have an API key
- Manual review queue is manageable
- Kick escalation: exponential backoff alone is sufficient (disable auto-ban)
- Profile scanning: optional, but useful if you receive frequent spam join waves
- Ban celebration: fun for small communities, upload a few GIFs

### Medium Groups (100-1,000 members)

- Enable all 14 detection checks
- Use AI Veto with AutoBan threshold at 4.0 and ReviewQueue at 2.5
- Set `MaxKicksBeforeBan` to 3-5
- Enable profile scanning with default thresholds (ban: 4.0, notify: 2.0)
- Ban celebration: upload 5-10 GIFs for variety

### Large Groups (>1,000 members)

- Enable all 14 detection checks
- Consider lowering ReviewQueue threshold to 2.0 for faster triage
- Keep AutoBan threshold at 4.0 unless false positive rate is very low, then consider 3.5
- Set `MaxKicksBeforeBan` to 2-3 for faster escalation
- Enable profile scanning — critical for catching spam bots before they post
- Consider raising the profile scan ban threshold to 4.5 if false positives are a concern
- Consider a dedicated moderation team for review queue management
- Ban celebration: upload 10+ GIFs and captions for maximum variety
