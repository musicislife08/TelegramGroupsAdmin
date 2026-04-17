# Comprehensive Documentation Update

**Date:** 2026-04-09
**Closes:** #383, #313, #438, #439, #440, #441
**Branch:** `docs/comprehensive-documentation-update`
**PR Target:** `develop`

## Philosophy

User-facing documentation: "What does this feature do for me and how do I use it?"

No internal class names, no architecture diagrams, no cache TTLs. Every section is written from the perspective of an admin managing Telegram groups through TGA. Depth scaled per feature — light for simple concepts, medium for features that require understanding.

## Slice 1: Getting Started Rewrite + Dead Reference Cleanup

**Issues:** #383, #313 (cleanup portion)

### Files Modified

| File | Changes |
|------|---------|
| `Docs/01-getting-started.md` | Rewrite per 10 fixes below |
| `Docs/04-best-practices.md` | Remove threshold tuning ref (line 46) |
| `Docs/getting-started/02-first-configuration.md` | Remove ML Threshold Tuning ref (line 399) |

### Getting Started Changes

1. **Delete** "AI-Powered Threshold Tuning" section (lines 121-132) — feature does not exist
2. **Fix** Reports page description to match actual UI (Type dropdown: Moderation Reports, Impersonation Alerts, Exam Reviews, Profile Scan Alerts)
3. **Remove** advice to change default thresholds — defaults (4.0/2.5) are correct
4. **Add** "Step 5: Connect an AI Provider" to First-Time Setup section
5. **Move** "Understanding the Dashboard" earlier — before "Initial Configuration"
6. **Fix** 2FA label from "Recommended" to "Required" with Owner override note
7. **Consolidate** redundant URL filtering sections into single cohesive flow
8. **Fix** "Need Help?" first bullet (auto-ban, not review queue)
9. **Rewrite** "Use Custom Prompts" with context and link to `features/06-ai-prompt-builder.md`
10. **Update** performance numbers

### #313 Cleanup — Items NOT Changed

- **ML.NET/SDCA references:** Confirmed accurate for V2. Similarity detection still uses ML.NET SDCA. No changes.
- **`future-docs` broken links:** None found. Already clean.

## Slice 2: Existing Doc Enhancements

**Issues:** #438, #439

### Profile Scanning Alert Lifecycle (#438)

**File:** `Docs/features/08-profile-scanning.md`

Add a **"What Happens When Someone Is Flagged"** section after the existing "Scoring Engine" section and before "Change Detection." User-facing perspective:

- A user joins with a suspicious profile score — what you see as an admin
- The alert appears on your Reports page as a Profile Scan Alert card
- Three actions available: **Ban**, **Kick**, or **Allow** — what each does
- Multi-group behavior: acting on one alert auto-cleans alerts in your other groups (sibling cleanup)
- Welcome gate interaction: flagged users stay restricted until you decide

No Mermaid diagrams, no internal class names.

### Chat Health Check (#439)

**New file:** `Docs/admin/03-chat-health.md`

Dedicated doc for the health monitoring system. Medium depth:

- **What it does for you:** TGA automatically monitors whether your bot is working in each group
- **What the colors mean:** Green (Healthy), Yellow (Warning), Red (Error), Gray (Unknown) with plain-language explanations
- **What gets checked:** Bot reachability, admin status, delete permissions, ban permissions, promote permissions, invite link validity
- **The 3-strike rule:** 3 consecutive failures → group marked inactive. What that means, how to reactivate.
- **Health-gated moderation:** TGA won't moderate a group it can't confirm is healthy — safety feature, not a bug
- **Manual refresh:** How to trigger a health check from the Chat Management page

Note: `admin/02-chat-management.md` already has a brief health status section. Add a cross-link from there to the new dedicated doc rather than duplicating content.

## Slice 3: New Feature Docs

**Issue:** #313 (remaining 8 missing features)

New files in `Docs/features/`, numbered 12-19 continuing from existing `11-per-user-messages.md`.

### Light Docs (~150-300 words)

Feature card style: what it does → where to find it → key settings → done.

| File | Feature | Content |
|------|---------|---------|
| `13-background-jobs.md` | Background Jobs | What runs automatically (health checks, blocklist sync, DB maintenance, backups), schedule overview, Background Jobs UI page for monitoring |
| `15-cross-chat-bans.md` | Cross-Chat Bans | Ban someone in one group → TGA cleans them from all groups. Where to find it, settings, scope |
| `16-stop-word-recommendations.md` | Stop Word Recommendations | TGA suggests stop words based on spam patterns. Where to find it, how to accept/reject suggestions |
| `18-dm-notifications.md` | DM Notifications | How TGA sends direct messages to users, what triggers DMs, the `/start` requirement, pending notification queue |
| `19-database-maintenance.md` | Database Maintenance | TGA automatically keeps your database healthy (VACUUM, REINDEX). Runs in background, no action needed. When and why. |

### Medium Docs (~400-700 words)

Existing feature doc structure without Mermaid diagrams unless the admin interacts with a multi-step flow.

| File | Feature | Content |
|------|---------|---------|
| `12-analytics.md` | Analytics Dashboard | 4 tabs (Overview, Performance, Trends, Detection History). What each tab shows, what the metrics mean for your group health, time range controls |
| `14-backup-restore.md` | Backup & Restore | How to create backups, what's included, encryption, retention settings, how to restore, download backups |
| `17-audit-log.md` | Audit Log | What gets logged (web actions, Telegram actions, system actions), what "actor" means, how to filter/search, why it matters for accountability |

## Slice 4: Index & Wayfinding Docs

**Issues:** #440, #441

### Settings Wayfinding Page (#440)

**New file:** `Docs/admin/04-settings-reference.md`

"Where do I find the setting for X?" index organized by the 6 Settings page sections:

- **System** (General, Security, Accounts, AI Providers, Email, ClamAV, VirusTotal, Logging, Background Jobs, Backup Config)
- **Telegram** (Bot Config, User API, Service Messages)
- **Moderation** (Ban Celebration)
- **Notifications** (Web Push)
- **Content Detection** (Algorithms, AI Integration, URL Filtering, File Scanning)
- **Training Data** (Stop Words, Training Samples)

Each subsection row: name as shown in UI, one-line plain-language summary, cross-link to feature doc. Per-chat overridable settings get a "per-chat" badge.

**Not included:** Default values, valid ranges, config model class names. This is wayfinding, not a technical reference.

### External Integrations Reference (#441)

**New file:** `Docs/admin/05-integrations.md`

Single page: "What external services does TGA talk to, and what does that mean for me?"

| Service | Coverage |
|---------|----------|
| **OpenAI** | What it powers (AI veto, image analysis, vision scoring), where to configure, what happens if key expires |
| **VirusTotal** | Scans shared files, free tier limits (500/day, 4/min), what happens when quota is hit |
| **ClamAV** | Local virus scanning on your server, no API key needed, what it catches vs. VirusTotal |
| **CAS (Combot Anti-Spam)** | Checks new joiners against global spam database, automatic, what happens if CAS is down |
| **Block List Project** | URL reputation database, categories with risk tiers, updated weekly |
| **WTelegram User API** | What it unlocks (profile scanning, send-as-admin), credential setup, session management |

Per service: what it does for you → do you need to configure it → what happens when it's unavailable.

Cross-links added from existing feature docs back to this page where services are mentioned.

## Cross-Cutting Concerns

### Cross-References

After all docs are written, a consistency pass adds cross-links:
- New feature docs link to Settings Wayfinding where relevant
- Integrations reference links to/from feature docs that mention each service
- Getting Started references new feature docs where appropriate

### Verification

- Grep for "AI-Powered Threshold Tuning", "Generate Recommendations", "5.0 points" across `Docs/` — confirm no stale references remain
- All markdown links resolve to actual files
- Final end-to-end read of Getting Started for flow and cohesion

## Files Summary

| Action | File | Issue |
|--------|------|-------|
| **Rewrite** | `Docs/01-getting-started.md` | #383 |
| **Edit** | `Docs/04-best-practices.md` | #383, #313 |
| **Edit** | `Docs/getting-started/02-first-configuration.md` | #383, #313 |
| **Edit** | `Docs/features/08-profile-scanning.md` | #438 |
| **Edit** | `Docs/admin/02-chat-management.md` | #439 (cross-link to new dedicated doc) |
| **New** | `Docs/admin/03-chat-health.md` | #439 |
| **New** | `Docs/features/12-analytics.md` | #313 |
| **New** | `Docs/features/13-background-jobs.md` | #313 |
| **New** | `Docs/features/14-backup-restore.md` | #313 |
| **New** | `Docs/features/15-cross-chat-bans.md` | #313 |
| **New** | `Docs/features/16-stop-word-recommendations.md` | #313 |
| **New** | `Docs/features/17-audit-log.md` | #313 |
| **New** | `Docs/features/18-dm-notifications.md` | #313 |
| **New** | `Docs/features/19-database-maintenance.md` | #313 |
| **New** | `Docs/admin/04-settings-reference.md` | #440 |
| **New** | `Docs/admin/05-integrations.md` | #441 |
