# Comprehensive Documentation Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 6 documentation issues (#383, #313, #438, #439, #440, #441) in a single PR with user-facing documentation covering Getting Started fixes, new feature docs, and wayfinding indexes.

**Architecture:** 16 tasks organized into 4 independent slices. Slices 1-4 can run in parallel. Task 15 (cross-references) depends on all prior tasks. Task 16 (verification) depends on Task 15. All docs live under `TelegramGroupsAdmin/Docs/`.

**Tech Stack:** Markdown documentation only — no code changes.

**Base path:** `TelegramGroupsAdmin/Docs` (all file paths below are relative to repo root unless prefixed with `Docs/`)

---

## Slice 1: Getting Started Rewrite + Dead Reference Cleanup

### Task 1: Fix Getting Started Document

**Files:**
- Modify: `TelegramGroupsAdmin/Docs/01-getting-started.md`

This task makes 10 targeted edits. Apply them in order. After all edits, read the file end-to-end to confirm it flows correctly.

- [ ] **Step 1: Fix 2FA label (line 52)**

Find:
```markdown
### 4. Set Up Two-Factor Authentication (Recommended)
```
Replace with:
```markdown
### 4. Set Up Two-Factor Authentication (Required)
```

Also find (a few lines below):
```markdown
1. After first login, you'll be prompted to set up 2FA
```
Replace with:
```markdown
1. After first login, you are required to set up 2FA (Owner accounts can override this for other admins)
```

- [ ] **Step 2: Remove bad threshold advice in Step 2 (lines ~82-85)**

Find:
```markdown
2. Set conservative thresholds:
   - **Auto-Ban Threshold**: 5.0 points (very high confidence required)
   - **Review Queue Threshold**: 2.5 points

3. Click **Save All Changes**
```
Replace with:
```markdown
2. The default thresholds (Auto-Ban: 4.0, Review Queue: 2.5) are well-calibrated — no changes needed

3. Click **Save All Changes**
```

- [ ] **Step 3: Add AI Provider setup step**

Find:
```markdown
### Step 4: Review Your First Detections
```
Insert BEFORE that line:
```markdown
### Step 4: Connect an AI Provider (Optional)

Connecting an AI provider unlocks several powerful features: AI Veto (reduces false positives by 80-90%), Image Spam Detection, Video Spam Detection, and the AI Prompt Builder.

1. Navigate to **Settings** -> **System** -> **AI Providers**
2. Enter your API key (OpenAI or compatible provider)
3. Click **Save**

You can enable AI-powered checks later in **Settings** -> **Content Detection** -> **AI Integration**.

```

Then rename the existing Step 4 to Step 5:
```markdown
### Step 5: Review Your First Detections
```

- [ ] **Step 4: Delete the AI-Powered Threshold Tuning section**

Find and DELETE this entire block (the section between "### Enable Machine Learning Algorithms" ending and "### Disable Training Mode"):
```markdown
### Use AI-Powered Threshold Tuning

Let the system recommend optimal settings:

1. Navigate to **Settings** -> **Content Detection** -> **Detection Algorithms**
2. Select **Analysis Period**: Last 30 days
3. Click **Generate Recommendations**
4. Review each recommendation:
   - Shows current vs. recommended threshold
   - Displays expected false positive reduction
   - Shows model confidence (prefer >=85%)
5. Click **Apply Threshold** on high-confidence recommendations
```

- [ ] **Step 5: Fix Disable Training Mode section**

Find:
```markdown
### Disable Training Mode

1. Go to **Settings** -> **Content Detection** -> **Detection Algorithms**
2. Toggle **Training Mode** to OFF
3. Reduce **Auto-Ban Threshold** from 5.0 to 4.0 points
4. **Save All Changes**
```
Replace with:
```markdown
### Disable Training Mode

1. Go to **Settings** -> **Content Detection** -> **Detection Algorithms**
2. Toggle **Training Mode** to OFF
3. **Save All Changes**
```

- [ ] **Step 6: Move Dashboard section before Production Configuration**

Cut the entire "## Understanding the Dashboard" section (including all subsections: Main Dashboard, Analytics Page, Messages Page, Reports Page) and paste it BEFORE "## Initial Configuration (First 24 Hours)". This orients users on what the UI pages are before they start configuring, matching the spec.

- [ ] **Step 7: Fix Reports Page description**

Find:
```markdown
### Reports Page

- **Review Queue** - Borderline spam needing manual review
- **Spam Reports** - Confirmed spam detections
- **User Reports** - Flagged users and ban history
```
Replace with:
```markdown
### Reports Page

Use the **Type** dropdown to filter reports:

- **Moderation Reports** - Spam detections needing review (borderline scores, auto-deleted confirmations)
- **Impersonation Alerts** - Users flagged for impersonating admins or other members
- **Exam Reviews** - Failed welcome exam attempts requiring admin review
- **Profile Scan Alerts** - Suspicious profiles detected on join (see [Profile Scanning](features/08-profile-scanning.md))

Use the **Status** filter to show only pending items or all history.
```

- [ ] **Step 8: Consolidate URL filtering and fix Common Workflows**

Find the "### Reviewing Spam Detections" section in Common Workflows and fix the first instruction:

Find:
```markdown
2. Select **Review Queue** tab
```
Replace with:
```markdown
2. Use the **Type** dropdown to filter by report type
```

Remove the duplicate URL filtering content in Common Workflows. Find these two sections and DELETE them — "### Adding Custom Blocked Domains" and "### Whitelisting Trusted Domains" — since they duplicate Step 3's URL Filtering content.

- [ ] **Step 9: Fix remaining issues**

Fix "Need Help?" section — find:
```markdown
- **Review Queue not working?** Check that Training Mode is OFF
```
Replace with:
```markdown
- **Auto-ban not working?** Check that Training Mode is OFF and your thresholds are set
```

Fix "Use Custom Prompts" section — find:
```markdown
### Use Custom Prompts (OpenAI)

Tailor spam detection to your group's context:
```
Replace with:
```markdown
### Use Custom Prompts (OpenAI)

Tailor AI spam detection to your group's context using the [AI Prompt Builder](features/06-ai-prompt-builder.md). Navigate to **Settings** -> **Content Detection** -> **AI Integration** to customize the system prompt:
```

Update performance numbers — find:
```markdown
- Handles 500-5,000 messages per day easily
```
Replace with:
```markdown
- Handles thousands of messages per day easily
```

- [ ] **Step 10: Read the file end-to-end and verify flow**

Read the full file. Verify:
- Steps are numbered correctly (1-5 in First-Time Setup)
- Dashboard section appears before Production Configuration
- No references to "AI-Powered Threshold Tuning" remain
- No advice to change thresholds from defaults
- All section cross-references resolve

- [ ] **Step 11: Commit**

```bash
git add TelegramGroupsAdmin/Docs/01-getting-started.md
git commit -F- <<'EOF'
docs: rewrite Getting Started document

- Remove dead AI-Powered Threshold Tuning section
- Fix Reports page to match actual UI (Type dropdown filter)
- Remove bad advice to change default thresholds
- Add Step 4: Connect an AI Provider
- Move Dashboard section before Production Configuration
- Fix 2FA label to Required
- Consolidate duplicate URL filtering sections
- Fix Need Help bullet and Custom Prompts section

Closes #383
EOF
```

### Task 2: Remove Dead References in Collateral Files

**Files:**
- Modify: `TelegramGroupsAdmin/Docs/04-best-practices.md`
- Modify: `TelegramGroupsAdmin/Docs/getting-started/02-first-configuration.md`

- [ ] **Step 1: Fix best-practices.md**

In `TelegramGroupsAdmin/Docs/04-best-practices.md`, find:
```markdown
- **Use AI-Powered Threshold Tuning** once you have 100+ total detections and 50+ AI veto events to let the system recommend optimal settings.
```
Replace with:
```markdown
- **Review algorithm performance** in Analytics -> Performance to see which checks are most effective for your groups.
```

- [ ] **Step 2: Fix first-configuration.md**

In `TelegramGroupsAdmin/Docs/getting-started/02-first-configuration.md`, find:
```markdown
- **ML Threshold Tuning** - Let AI optimize your thresholds
```
Replace with:
```markdown
- **[Stop Word Recommendations](../features/16-stop-word-recommendations.md)** - Let TGA suggest stop words based on your spam patterns
```

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Docs/04-best-practices.md TelegramGroupsAdmin/Docs/getting-started/02-first-configuration.md
git commit -F- <<'EOF'
docs: remove dead threshold tuning references

Remove stale AI-Powered Threshold Tuning and ML Threshold Tuning
references from best-practices and first-configuration docs.

Partial #313
EOF
```

---

## Slice 2: Existing Doc Enhancements

### Task 3: Add Profile Scanning Alert Lifecycle Section

**Files:**
- Modify: `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md` (insert after Scoring Engine section ~line 197, before Change Detection ~line 199)

- [ ] **Step 1: Insert "What Happens When Someone Is Flagged" section**

In `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md`, find the line:
```markdown
## Change Detection
```
Insert BEFORE that line:
```markdown
## What Happens When Someone Is Flagged

When a user's profile score crosses the review threshold, TGA creates a **Profile Scan Alert** and notifies you.

### Seeing the Alert

1. Navigate to **Reports** in the sidebar
2. Use the **Type** dropdown and select **Profile Scan Alerts**
3. Each alert card shows the user's profile details, score breakdown, and flagged reasons

The user remains restricted in the group until you take action — they cannot send messages or interact until you decide.

### Your Three Options

| Action | What It Does |
|--------|-------------|
| **Allow** (green) | Clears the alert, restores the user's permissions, and lets them participate normally |
| **Ban** (red) | Permanently bans the user from the group and removes their messages |
| **Kick** (orange) | Removes the user from the group without a permanent ban — they can rejoin |

### Multi-Group Behavior

If you manage multiple groups and a user triggered alerts in several of them, acting on one alert **automatically resolves the matching alerts in your other groups**. You don't need to review the same user separately in each group.

### How This Connects to the Welcome System

Profile scanning works alongside the Welcome system to gate new user admission:

1. User joins the group
2. Profile scan runs and scores their profile
3. If flagged, the user stays restricted until you review the alert
4. If the user also needs to pass a Welcome exam, both gates must clear before they get full access

This means a user won't slip through just because they passed the exam — if their profile looks suspicious, you still get the final say.

```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/08-profile-scanning.md
git commit -F- <<'EOF'
docs: add alert lifecycle section to profile scanning doc

Documents what admins see when a user is flagged, the three
review actions (Ban/Kick/Allow), multi-group sibling cleanup,
and Welcome system gate interaction.

Closes #438
EOF
```

### Task 4: Create Chat Health Check Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/admin/03-chat-health.md`
- Modify: `TelegramGroupsAdmin/Docs/admin/02-chat-management.md` (add cross-link)

- [ ] **Step 1: Write the chat health check doc**

Create `TelegramGroupsAdmin/Docs/admin/03-chat-health.md`:

```markdown
# Chat Health Monitoring

TGA automatically monitors whether your bot is working correctly in each of your Telegram groups. You can see the health status at a glance on the **Chat Management** page.

## What the Colors Mean

Each group shows a color-coded health indicator:

| Color | Status | What It Means |
|-------|--------|---------------|
| Green | Healthy | Everything is working — your bot has all the permissions it needs |
| Yellow | Warning | Something needs attention — the bot is missing a permission or can't perform an action |
| Red | Error | The bot cannot reach this group — it may have been removed or kicked |
| Gray | Unknown | The group hasn't been checked yet (happens briefly after TGA starts up) |

Click **View Details** on any group to see the complete health report, including which specific checks passed or failed and when the last successful check occurred.

## What Gets Checked

TGA runs these checks on each group every 30 minutes:

| Check | Why It Matters |
|-------|---------------|
| **Bot is reachable** | Can the bot connect to the group at all? If the bot was removed or the group was deleted, this fails. |
| **Bot is an admin** | The bot needs admin status to moderate messages and manage users. |
| **Can delete messages** | Required for removing spam and cleaning up after bans. |
| **Can ban users** | Required for auto-ban and manual moderation actions. |
| **Can promote members** | Required for managing user permissions during welcome exam and profile scan gates. |
| **Invite link is valid** | TGA checks that the group's invite link is accessible for user verification features. |

## The 3-Strike Rule

If TGA cannot reach a group three times in a row, it automatically marks that group as **inactive**. This prevents TGA from wasting resources trying to moderate a group it can't access.

**What inactive means:**
- TGA stops trying to moderate messages in that group
- The group appears dimmed on the Chat Management page
- Health checks continue at a reduced frequency

**How to reactivate:**
1. Fix the underlying issue (re-add the bot to the group, restore admin permissions)
2. Go to **Chat Management** and trigger a manual health check
3. Once the bot can reach the group again, TGA automatically reactivates it

## Health-Gated Moderation

TGA will not moderate a group it cannot confirm is healthy. This is a safety feature:

- If health status is Unknown (e.g., right after TGA restarts), moderation pauses for that group until the first health check completes
- This prevents TGA from attempting actions it might not have permission to perform
- Moderation resumes automatically once the health check confirms the bot has the right permissions

## Running a Manual Health Check

You don't need to wait for the automatic 30-minute cycle. To check a group immediately:

1. Navigate to **Chat Management** in the sidebar
2. Find the group you want to check
3. Click the **refresh** button in the actions column
4. The health status updates within a few seconds

This is useful after making permission changes in Telegram to verify TGA has picked them up.

## Related

- **[Chat Management](02-chat-management.md)** — Managing your groups, adding new chats, per-chat configuration
- **[Settings → System → Background Jobs](04-settings-reference.md)** — Adjust the health check schedule
```

- [ ] **Step 2: Add cross-link from chat-management.md**

In `TelegramGroupsAdmin/Docs/admin/02-chat-management.md`, find the health check section (around line 350, heading "## Monitoring Chat Health") and add at the end of that section:

```markdown
For a complete guide to health statuses, checks, the 3-strike rule, and troubleshooting, see **[Chat Health Monitoring](03-chat-health.md)**.
```

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Docs/admin/03-chat-health.md TelegramGroupsAdmin/Docs/admin/02-chat-management.md
git commit -F- <<'EOF'
docs: add dedicated chat health monitoring documentation

New admin doc covering health statuses, permission checks,
3-strike inactive rule, health-gated moderation, and manual
refresh. Cross-links from existing chat management doc.

Closes #439
EOF
```

---

## Slice 3: New Feature Docs

### Task 5: Analytics Dashboard Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/12-analytics.md`

- [ ] **Step 1: Write the analytics doc**

Create `TelegramGroupsAdmin/Docs/features/12-analytics.md`:

```markdown
# Analytics Dashboard

The Analytics page gives you a bird's-eye view of how your spam detection is performing across all your groups. Navigate to **Analytics** in the sidebar to access it.

Use the date range selector (Last 7 / 30 / 90 Days) at the top to adjust the reporting window.

## Content Detection

The default tab shows your overall detection activity:

- **Total content checks** — how many messages TGA has analyzed
- **Spam detected** — count and percentage of messages flagged as spam
- **Stop words in use** — how many of your stop words are active
- **Training samples** — how many spam/ham examples you've provided

The **recent spam checks table** shows individual detections with timestamps, scores, and which checks triggered. Use this to spot-check whether detections look accurate.

The **OpenAI False Positive Prevention** section shows how often the AI veto overrode algorithmic detections — useful for gauging whether the AI provider is earning its keep.

## Message Trends

Switch to this tab to understand messaging patterns in your groups:

- **Key metrics** — Total messages, daily average, active users, spam rate
- **Daily volume chart** — See when your groups are most active and when spam peaks
- **Most active users** — Ranking of who's posting the most
- **Per-chat breakdown** — Compare activity across your different groups

Use the chat filter to focus on a single group or view all groups combined.

## Performance

This tab answers the question: "How accurate is my spam detection?"

- **Overall Accuracy** — Percentage of correct decisions (true positives + true negatives)
- **False Positive Rate** — How often legitimate messages get flagged as spam. Lower is better.
- **False Negative Rate** — How often actual spam slips through. Lower is better.
- **Response Time** — Average and P95 detection latency

The **Algorithm Performance table** breaks down each detection check individually, showing hit rate, average score contribution, and false positive count. This helps you decide which checks to tune or disable.

## Welcome Analytics

If you use the Welcome system (exams, profile scanning), this tab tracks join activity:

- **Total joins** and **Acceptance Rate** — What percentage of new users pass your gates
- **Average time to accept** — How quickly users complete your welcome process
- **Timeout Rate** — How many users fail to respond in time
- **Per-chat breakdown** — Compare join patterns across groups

A high timeout rate might mean your welcome exam is too difficult or your timeout is too short.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/12-analytics.md
git commit -F- <<'EOF'
docs: add Analytics Dashboard feature documentation

Covers all 4 tabs: Content Detection, Message Trends,
Performance, and Welcome Analytics with user-facing
explanations of what the metrics mean.

Partial #313
EOF
```

### Task 6: Background Jobs Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/13-background-jobs.md`

- [ ] **Step 1: Write the background jobs doc**

Create `TelegramGroupsAdmin/Docs/features/13-background-jobs.md`:

```markdown
# Background Jobs

TGA runs several automated tasks in the background to keep your system healthy and up to date. You can view and manage these from **Settings** -> **System** -> **Background Jobs**.

## What Runs Automatically

| Job | Default Schedule | What It Does |
|-----|-----------------|--------------|
| **Chat Health Check** | Every 30 minutes | Monitors bot connectivity and permissions in each group |
| **Classifier Retraining** | Every 8 hours | Retrains spam detection models using your latest training data |
| **User Photo Refresh** | Daily at 3 AM | Updates cached profile photos for active users |
| **Blocklist Sync** | Weekly (Sunday 3 AM) | Downloads the latest URL blocklist data |
| **Scheduled Backup** | Daily at 2 AM | Creates an encrypted database backup (disabled by default — see [Backup & Restore](14-backup-restore.md)) |
| **Data Cleanup** | Daily | Removes expired messages and reports based on retention settings (disabled by default) |
| **Database Maintenance** | Weekly (Sunday 4 AM) | Optimizes database performance (disabled by default — see [Database Maintenance](19-database-maintenance.md)) |
| **Profile Rescan** | Every 6 hours | Re-scans user profiles for changes (disabled by default) |

## Managing Jobs

For each job you can:

- **Enable or disable** it with the toggle
- **Change the schedule** using natural language (e.g., "every 30 minutes", "every day at 2pm", "every week on sunday at 3am")
- **Run it immediately** with the "Run Now" button
- **Configure job-specific settings** where applicable (e.g., retention periods for Data Cleanup, VACUUM/ANALYZE options for Database Maintenance)

The jobs table shows the last run time and next scheduled run for each job, so you can confirm everything is running on schedule.

## You Don't Need to Touch Most of These

The defaults are designed to work well out of the box. The three jobs disabled by default (Scheduled Backup, Data Cleanup, Database Maintenance) are optional and depend on your preferences — enable them when you're ready.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/13-background-jobs.md
git commit -F- <<'EOF'
docs: add Background Jobs feature documentation

Lists all automated jobs, schedules, and management options.

Partial #313
EOF
```

### Task 7: Backup & Restore Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/14-backup-restore.md`

- [ ] **Step 1: Write the backup & restore doc**

Create `TelegramGroupsAdmin/Docs/features/14-backup-restore.md`:

```markdown
# Backup & Restore

TGA can create encrypted backups of your entire database and restore from them if needed. Navigate to **Settings** -> **System** -> **Backup Configuration** to get started.

## Setting Up Backups

Before creating your first backup, you need to set an encryption passphrase:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Set your encryption passphrase — **save this somewhere safe**, you'll need it to restore
3. Configure your backup directory path
4. Click **Save Configuration**

All backups are encrypted with AES-256-GCM. Without the passphrase, backup files cannot be read.

## What's Included

A backup contains everything TGA needs to fully restore your instance:

- All users and their profiles
- Message history across all groups
- Spam detection configuration and thresholds
- Bans, warnings, and moderation history
- Telegram chat mappings
- Reports and review queue items
- Audit logs
- Training data (spam/ham samples, stop words)

## Creating a Backup

**Manual backup:** Go to **Settings** -> **System** -> **Backup Configuration** and click **Backup Now**.

**Automatic backups:** Enable the Scheduled Backup job in **Settings** -> **System** -> **Background Jobs**. By default it runs daily at 2 AM.

## Retention Strategy

TGA uses a Grandfather-Father-Son rotation to keep backups manageable:

| Tier | Default | What It Keeps |
|------|---------|---------------|
| Hourly | Up to 168 (1 week) | Recent backups for quick recovery |
| Daily | Up to 31 | One backup per day for the last month |
| Weekly | Up to 52 | One backup per week for the last year |
| Monthly | Up to 60 | One backup per month for 5 years |
| Yearly | Up to 20 | Long-term archive |

Older backups are automatically pruned when new ones are created. Adjust these numbers in the Backup Configuration settings to match your storage capacity.

## Restoring from a Backup

**Warning:** Restoring a backup **permanently deletes all current data** and replaces it with the backup contents. You will be logged out after the restore completes.

To restore:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Either upload a `.tar.gz` backup file or select one from the backup browser
3. Review the backup details (creation date, version, table count)
4. Check the confirmation box: "I understand this will permanently delete all current data"
5. Click **Wipe & Restore**
6. Wait for the restore to complete, then log back in

## Rotating the Encryption Passphrase

If you need to change your encryption passphrase:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Click **Rotate Passphrase**
3. Enter your new passphrase
4. TGA will re-encrypt all existing backups with the new passphrase

This is an atomic operation — either all backups get re-encrypted or none do.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/14-backup-restore.md
git commit -F- <<'EOF'
docs: add Backup & Restore feature documentation

Covers encryption setup, manual/automatic backups, GFS
retention strategy, restore process, and passphrase rotation.

Partial #313
EOF
```

### Task 8: Cross-Chat Bans Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/15-cross-chat-bans.md`

- [ ] **Step 1: Write the cross-chat bans doc**

Create `TelegramGroupsAdmin/Docs/features/15-cross-chat-bans.md`:

```markdown
# Cross-Chat Bans

When you ban a user in one of your Telegram groups, TGA automatically cleans up their messages across **all** your managed groups — not just the one where the ban happened.

## How It Works

1. A user gets banned (either by auto-ban, manual action, or through the Reports page)
2. TGA queues a background job to find and delete that user's messages in every group you manage
3. Messages are deleted within Telegram's 48-hour deletion window (Telegram API limitation)

You don't need to configure anything — this happens automatically for every ban.

## What Gets Cleaned Up

- All messages from the banned user across all your groups
- Only messages within the last 48 hours (Telegram's API limit for message deletion)
- Older messages remain visible but the user is still banned from all groups

## Where to See It

The [Audit Log](17-audit-log.md) records every ban action with details about which groups were affected and who issued the ban (system auto-ban, admin action, or Telegram-side moderation).
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/15-cross-chat-bans.md
git commit -F- <<'EOF'
docs: add Cross-Chat Bans feature documentation

Explains automatic cross-group message cleanup on ban.

Partial #313
EOF
```

### Task 9: Stop Word Recommendations Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/16-stop-word-recommendations.md`

- [ ] **Step 1: Write the stop word recommendations doc**

Create `TelegramGroupsAdmin/Docs/features/16-stop-word-recommendations.md`:

```markdown
# Stop Word Recommendations

TGA can analyze your spam patterns and suggest which words to add or remove from your stop words list. Navigate to **Settings** -> **Training Data** -> **Stop Words Library** and look for the **Generate Recommendations** button.

## What It Does

The recommendation engine compares your spam training data against legitimate messages to find:

- **Words to add** — Words that appear frequently in spam but rarely in normal messages (high spam-to-legit ratio)
- **Words to remove** — Words in your stop list that trigger too many false positives (precision below 70%)
- **Performance cleanup** — Words that are slowing down detection without being effective (when check execution time exceeds 200ms)

## Generating Recommendations

1. Select an analysis period (defaults to the last 30 days)
2. Click **Generate Recommendations**
3. Review the results

You need at least **50 spam samples** and **100 legitimate message samples** for the analysis to produce meaningful results. If you don't have enough training data yet, keep reviewing detections in the Reports page to build up your sample set.

## Reviewing Suggestions

Each recommendation shows you the evidence:

**For additions:** The word, how often it appears in spam vs. legitimate messages, and the spam-to-legit ratio. A word that appears 5x more often in spam than in normal messages is a strong candidate.

**For removals:** The word, its precision percentage, and why it's being recommended for removal. A word with 60% precision means 40% of the time it flags a legitimate message — that's too high.

**For performance cleanup:** The word, its efficiency score, and estimated time savings if removed. These appear only when your stop word check is running slower than 200ms.

## Accepting Suggestions

Click **Add** or **Remove** on individual recommendations to apply them. Each action immediately updates your stop words list — there's no batch apply. This lets you cherry-pick the suggestions you agree with.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/16-stop-word-recommendations.md
git commit -F- <<'EOF'
docs: add Stop Word Recommendations feature documentation

Covers recommendation generation, review process, and
acceptance workflow for additions, removals, and cleanup.

Partial #313
EOF
```

### Task 10: Audit Log Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/17-audit-log.md`

- [ ] **Step 1: Write the audit log doc**

Create `TelegramGroupsAdmin/Docs/features/17-audit-log.md`:

```markdown
# Audit Log

The Audit Log records every significant action taken in TGA — whether by an admin through the web interface, by the bot automatically, or by a Telegram user. Navigate to **Audit** in the sidebar to access it.

This feature requires GlobalAdmin or Owner permissions.

## Two Logs, Two Perspectives

### Web Admin Log

Tracks everything that happens in the TGA web interface:

| Event Types | Examples |
|-------------|----------|
| **Login / Password Changed** | Admin authentication activity |
| **System Config Changed** | Any settings modification |
| **Permission Changed** | Role or access changes for admin accounts |
| **User Registration / Deletion** | Admin account lifecycle |
| **Invite Created / Revoked** | Admin invitation management |
| **TOTP events** | Two-factor authentication setup and changes |
| **Data Export** | When data is exported from the system |

Each entry shows:
- **Timestamp** — When it happened
- **Event Type** — Color-coded chip for quick scanning
- **Actor (Who)** — Which admin or SYSTEM performed the action
- **Target** — What or who was affected
- **Details** — Additional context (configuration values, reason, etc.)

### Telegram Moderation Log

Tracks all moderation actions across your Telegram groups:

| Action Type | Color | What It Means |
|-------------|-------|---------------|
| **Ban** | Red | User was permanently or temporarily banned |
| **Warn** | Yellow | User received a warning |
| **Mute** | Yellow | User was muted |
| **Trust** | Green | User was marked as trusted |
| **Unban** | Blue | User's ban was lifted |

Each entry shows:
- **Telegram User** — Avatar, display name, and Telegram ID
- **Issued By** — Who triggered the action: "Bot Protection" (automatic), "Auto-system" (rule-based), a web admin email, or a Telegram user
- **Reason** — Why the action was taken (if provided)
- **Expires At** — When the action expires, or "Permanent"

## Filtering

Both logs support filtering to help you find what you're looking for:

- **Web Admin Log** — Filter by event type, actor, or target user
- **Telegram Moderation Log** — Filter by action type, Telegram user ID, or issuer

Pagination options: 25, 50, or 100 entries per page.

## Why It Matters

The audit log gives you accountability and transparency. If something unexpected happens — a setting was changed, a user was banned incorrectly, or an admin account was compromised — you can trace exactly what happened, when, and who did it.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/17-audit-log.md
git commit -F- <<'EOF'
docs: add Audit Log feature documentation

Covers both Web Admin Log and Telegram Moderation Log tabs,
event types, filtering, and why audit logging matters.

Partial #313
EOF
```

### Task 11: DM Notifications Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/18-dm-notifications.md`

- [ ] **Step 1: Write the DM notifications doc**

Create `TelegramGroupsAdmin/Docs/features/18-dm-notifications.md`:

```markdown
# Notifications

TGA keeps you informed about important events through real-time notifications in the web interface.

## The Notification Bell

Look for the bell icon in the top navigation bar. When new events occur, it shows a red badge with the unread count.

Click the bell to see your recent notifications. Each notification shows:

- **Icon** — Color-coded by event type for quick recognition
- **Subject** — What happened
- **Message** — Brief details
- **Time** — How long ago it occurred

## What Triggers Notifications

| Event | What It Means |
|-------|---------------|
| **Spam Detected** | A message was flagged by the detection engine |
| **Spam Auto-Deleted** | A high-confidence spam message was automatically removed |
| **User Banned** | A user was banned (automatically or manually) |
| **Message Reported** | A message was reported by a group member |
| **Malware Detected** | A file shared in your group was flagged as malicious |
| **Chat Admin Changed** | Bot admin status or permissions changed in a group |
| **Chat Health Warning** | A group's health check detected an issue |
| **Backup Failed** | A scheduled backup failed to complete |

## Managing Notifications

- **Mark as read** — Click the checkmark on individual notifications
- **Delete** — Click the X to remove a notification
- **Mark all read** — Clears the unread count for all notifications
- **Clear all** — Removes all notifications from the list

Notifications update in real-time — you don't need to refresh the page to see new events.

## Configuring Notifications

To manage notification preferences, go to **Settings** -> **Notifications** -> **Web Push Notifications**.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/18-dm-notifications.md
git commit -F- <<'EOF'
docs: add Notifications feature documentation

Covers notification bell, event types, and management actions.

Partial #313
EOF
```

### Task 12: Database Maintenance Documentation

**Files:**
- Create: `TelegramGroupsAdmin/Docs/features/19-database-maintenance.md`

- [ ] **Step 1: Write the database maintenance doc**

Create `TelegramGroupsAdmin/Docs/features/19-database-maintenance.md`:

```markdown
# Database Maintenance

TGA can automatically optimize your PostgreSQL database to keep it running efficiently. This is managed through the **Database Maintenance** background job.

## What It Does

Two optional maintenance operations:

**VACUUM** — Reclaims disk space from deleted rows. Over time, as TGA processes messages and cleans up old data, PostgreSQL accumulates dead rows. VACUUM reclaims that space and keeps your database compact.

**ANALYZE** — Updates PostgreSQL's query planner statistics. This helps the database choose efficient execution plans for queries, which can improve performance as your data grows.

## Setting It Up

1. Go to **Settings** -> **System** -> **Background Jobs**
2. Find **Database Maintenance** in the jobs list
3. Click **Configure** to choose which operations to run:
   - Check **Run VACUUM** to reclaim storage
   - Check **Run ANALYZE** to update query statistics
4. Enable the job with the toggle
5. Set a schedule (default: weekly on Sunday at 4 AM)

## Do You Need This?

For most installations, PostgreSQL handles maintenance reasonably well on its own through its built-in autovacuum. Enabling this job is useful if:

- Your TGA instance processes a high volume of messages
- You notice database storage growing over time
- You want explicit control over when maintenance runs

If you're unsure, it's fine to leave this disabled. You can always enable it later or run it manually with the **Run Now** button to see if it makes a difference.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/19-database-maintenance.md
git commit -F- <<'EOF'
docs: add Database Maintenance feature documentation

Covers VACUUM/ANALYZE operations, setup, and when to enable.

Partial #313
EOF
```

---

## Slice 4: Index & Wayfinding Docs

### Task 13: Settings Wayfinding Page

**Files:**
- Create: `TelegramGroupsAdmin/Docs/admin/04-settings-reference.md`

- [ ] **Step 1: Write the settings wayfinding doc**

Create `TelegramGroupsAdmin/Docs/admin/04-settings-reference.md`:

```markdown
# Settings Reference

This page helps you find the right settings section for what you want to configure. Navigate to **Settings** in the sidebar to access the settings page.

Settings marked with **per-chat** can be customized for individual groups via the Chat Config Modal on the Chat Management page. All others are global.

## System

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **General** | App display name, timezone, general system behavior | — |
| **Security** | Two-factor authentication enforcement, session timeout | [Getting Started](../01-getting-started.md) |
| **Admin Accounts** | Manage web admin users, roles, invitations | [Web User Management](01-web-user-management.md) |
| **AI Providers** | OpenAI API key, model selection, provider configuration | [AI Prompt Builder](../features/06-ai-prompt-builder.md) |
| **Email** | SendGrid API key, sender address for verification emails | — |
| **ClamAV** | Local antivirus scanner connection settings | [Integrations](05-integrations.md) |
| **VirusTotal** | API key for cloud-based file and URL scanning | [Integrations](05-integrations.md) |
| **Logging** | Log levels, Seq integration for structured logging | — |
| **Background Jobs** | Enable/disable and schedule automated tasks | [Background Jobs](../features/13-background-jobs.md) |
| **Backup Configuration** | Encryption, retention strategy, backup directory | [Backup & Restore](../features/14-backup-restore.md) |

## Telegram

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Bot Configuration** | Bot token, bot behavior settings | [Getting Started](../01-getting-started.md) |
| **User API** | WTelegram credentials for profile scanning and send-as-admin | [Integrations](05-integrations.md) |
| **Service Messages** | Control which Telegram service messages the bot handles | — |

## Moderation

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Ban Celebration** | Celebratory GIFs and captions when spammers are banned | [Ban Celebration](../features/09-ban-celebration.md) |

## Notifications

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Web Push** | Browser push notification preferences | [Notifications](../features/18-dm-notifications.md) |

## Content Detection

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Detection Algorithms** | Enable/disable individual checks, Training Mode, thresholds **per-chat** | [Spam Detection](../features/03-spam-detection.md) |
| **AI Integration** | OpenAI Veto, image/video analysis, custom system prompt **per-chat** | [AI Prompt Builder](../features/06-ai-prompt-builder.md) |
| **URL Filtering** | Blocklists, whitelists, manual domains **per-chat** | [URL Filtering](../features/04-url-filtering.md) |
| **File Scanning** | ClamAV and VirusTotal file scanning toggles | [Spam Detection](../features/03-spam-detection.md) |

## Training Data

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Stop Words Library** | Manage stop words, generate recommendations | [Stop Word Recommendations](../features/16-stop-word-recommendations.md) |
| **Training Samples** | View and manage spam/ham training examples | [Spam Detection](../features/03-spam-detection.md) |
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/admin/04-settings-reference.md
git commit -F- <<'EOF'
docs: add settings wayfinding reference page

Index of all settings sections with one-line descriptions,
per-chat indicators, and cross-links to feature docs.

Closes #440
EOF
```

### Task 14: External Integrations Reference

**Files:**
- Create: `TelegramGroupsAdmin/Docs/admin/05-integrations.md`

- [ ] **Step 1: Write the integrations reference doc**

Create `TelegramGroupsAdmin/Docs/admin/05-integrations.md`:

```markdown
# External Integrations

TGA connects to several external services to provide its detection and moderation capabilities. This page explains what each service does for you, whether you need to set it up, and what happens if it's unavailable.

## OpenAI (or Compatible AI Provider)

**What it does for you:** Powers the AI Veto system (reduces false positives by 80-90%), image spam detection, video spam detection, and the AI Prompt Builder for custom detection rules.

**Do you need to configure it?** Optional but recommended. Go to **Settings** -> **System** -> **AI Providers** and enter your API key.

**What happens if it's unavailable:** AI-powered checks are skipped — TGA falls back to its rule-based and ML detection checks. No messages are missed, but you lose the AI layer's accuracy boost.

**Cost:** Approximately $0.002 per message reviewed. The AI only runs on borderline cases, not every message.

## VirusTotal

**What it does for you:** Scans files shared in your groups against 70+ antivirus engines in the cloud. Catches malware, ransomware, and other threats that local scanning might miss.

**Do you need to configure it?** Optional. Get a free API key from [virustotal.com](https://www.virustotal.com) and enter it in **Settings** -> **System** -> **VirusTotal**.

**Free tier limits:** 500 lookups per day, 4 per minute. TGA optimizes usage by checking file hashes first (cached for 24 hours) — if someone shares the same file twice, it only counts as one lookup.

**What happens if it's unavailable:** File scanning falls back to ClamAV only (local scanning). If neither is configured, file scanning is disabled entirely.

## ClamAV

**What it does for you:** Scans files locally on your server for malware. This is the first line of defense — fast and free, with no API limits.

**Do you need to configure it?** Only if you run ClamAV alongside TGA (e.g., in a Docker sidecar). Configure the connection in **Settings** -> **System** -> **ClamAV**.

**How it works with VirusTotal:** ClamAV runs first (Tier 1, local). If ClamAV doesn't flag the file, VirusTotal checks it in the cloud (Tier 2). If either flags it, the file is treated as malicious.

**What happens if it's unavailable:** File scanning relies on VirusTotal only, or is disabled if neither is configured.

## CAS (Combot Anti-Spam)

**What it does for you:** Checks every new user who joins your groups against the [CAS global spam database](https://cas.chat). This catches known spammers the moment they join, before they can send a single message.

**Do you need to configure it?** No — CAS is enabled by default as a join-time check. No API key required.

**What happens if it's unavailable:** The CAS check is skipped for that join event. TGA still evaluates the user through its other detection checks. CAS uses a fail-open design — a temporary outage won't block legitimate users from joining.

## Block List Project

**What it does for you:** Provides a database of 540,000+ known malicious domains across categories: Phishing, Scam, Malware, Ransomware, Fraud, Abuse, Piracy, Ads, Tracking, and Redirect. When someone posts a URL from one of these domains, TGA flags it.

**Do you need to configure it?** Choose which categories to enable in **Settings** -> **Content Detection** -> **URL Filtering**. Recommended categories:
- **Essential** (low false-positive risk): Phishing, Scam, Malware, Ransomware
- **Moderate** (some false positives): Fraud, Abuse
- **Aggressive** (higher false positives): Piracy, Ads, Tracking, Redirect

**Updates:** TGA automatically syncs the latest blocklist data weekly (Sunday at 3 AM by default). You can trigger a manual sync from **Settings** -> **System** -> **Background Jobs**.

**What happens if it's unavailable:** The last downloaded blocklist data continues to work. URL filtering only loses coverage if the blocklist hasn't been updated in a very long time.

## WTelegram User API

**What it does for you:** Connects to Telegram as a real user account (yours), unlocking features that the Bot API cannot provide:
- **[Profile Scanning](../features/08-profile-scanning.md)** — Inspect full user profiles (bio, photos, linked channels) when someone joins
- **[Send As Admin](../features/10-send-as-admin.md)** — Send messages as your personal Telegram account instead of the bot
- **[Per-User Messages](../features/11-per-user-messages.md)** — Resolve full user details for cross-chat history

**Do you need to configure it?** Optional but unlocks significant capabilities. Go to **Settings** -> **Telegram** -> **User API** and enter your Telegram phone number. You'll need to complete SMS verification to establish the session.

**Session management:** Your Telegram session is stored securely in the database and persists across TGA restarts. You only need to authenticate once unless you log out or your session expires.

**What happens if it's unavailable:** Features that require the User API (profile scanning, send-as-admin) are disabled. The bot continues to work normally for all other detection and moderation tasks.
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Docs/admin/05-integrations.md
git commit -F- <<'EOF'
docs: add external integrations reference

User-facing guide covering OpenAI, VirusTotal, ClamAV, CAS,
Block List Project, and WTelegram User API — what each does,
configuration, and behavior when unavailable.

Closes #441
EOF
```

---

## Finalization

### Task 15: Cross-Reference Pass

**Files:**
- Modify: `TelegramGroupsAdmin/Docs/01-getting-started.md`
- Modify: `TelegramGroupsAdmin/Docs/features/03-spam-detection.md`

- [ ] **Step 1: Add links from Getting Started to new docs**

In `TelegramGroupsAdmin/Docs/01-getting-started.md`, find the "## Next Steps" section and add these links:

```markdown
- **[Analytics Dashboard](features/12-analytics.md)** - Understand your spam detection performance
- **[Backup & Restore](features/14-backup-restore.md)** - Protect your data with encrypted backups
- **[Audit Log](features/17-audit-log.md)** - Track every action taken in your system
- **[Settings Reference](admin/04-settings-reference.md)** - Find any setting quickly
- **[External Integrations](admin/05-integrations.md)** - Understand the services TGA connects to
```

- [ ] **Step 2: Verify all markdown links resolve**

Run from the repo root:
```bash
cd TelegramGroupsAdmin/Docs && find . -name "*.md" -exec grep -l "\[.*\](.*\.md)" {} \; | while read f; do grep -oP '\[.*?\]\(\K[^)]+\.md' "$f" | while read link; do dir=$(dirname "$f"); target=$(cd "$dir" && realpath --relative-to=. "$link" 2>/dev/null || echo "BROKEN"); if [ ! -f "$dir/$link" ]; then echo "BROKEN: $f -> $link"; fi; done; done
```

Fix any broken links found.

- [ ] **Step 3: Commit**

```bash
git add -A TelegramGroupsAdmin/Docs/
git commit -F- <<'EOF'
docs: add cross-references between new and existing docs

Wire up links from Getting Started to new feature docs,
settings reference, and integrations guide.
EOF
```

### Task 16: Final Verification

- [ ] **Step 1: Verify no stale references remain**

```bash
grep -r "AI-Powered Threshold Tuning" TelegramGroupsAdmin/Docs/
grep -r "5\.0 points" TelegramGroupsAdmin/Docs/
grep -r "ML Threshold Tuning" TelegramGroupsAdmin/Docs/
```

All three should return zero results. (Note: do NOT grep for "Generate Recommendations" — the Stop Word Recommendations feature legitimately uses that button label.)

- [ ] **Step 2: Verify all new files exist**

```bash
ls -la TelegramGroupsAdmin/Docs/features/12-analytics.md \
       TelegramGroupsAdmin/Docs/features/13-background-jobs.md \
       TelegramGroupsAdmin/Docs/features/14-backup-restore.md \
       TelegramGroupsAdmin/Docs/features/15-cross-chat-bans.md \
       TelegramGroupsAdmin/Docs/features/16-stop-word-recommendations.md \
       TelegramGroupsAdmin/Docs/features/17-audit-log.md \
       TelegramGroupsAdmin/Docs/features/18-dm-notifications.md \
       TelegramGroupsAdmin/Docs/features/19-database-maintenance.md \
       TelegramGroupsAdmin/Docs/admin/03-chat-health.md \
       TelegramGroupsAdmin/Docs/admin/04-settings-reference.md \
       TelegramGroupsAdmin/Docs/admin/05-integrations.md
```

All 11 files should exist.

- [ ] **Step 3: Count total changes**

```bash
git log --oneline docs/comprehensive-documentation-update..HEAD
git diff --stat docs/comprehensive-documentation-update~1..HEAD -- TelegramGroupsAdmin/Docs/
```

Verify the commit history matches expectations: ~14 commits covering all 6 issues.
